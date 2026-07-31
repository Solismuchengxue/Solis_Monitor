#include "ota_update.h"

#include <stdio.h>
#include <string.h>

#include "esp_app_desc.h"
#include "esp_app_format.h"
#include "sdkconfig.h"

static void copy_fixed_string(char output[33], const char input[32])
{
    memcpy(output, input, 32);
    output[32] = '\0';
}

size_t ota_update_required_header_size(void)
{
    return sizeof(esp_image_header_t) +
           sizeof(esp_image_segment_header_t) +
           sizeof(esp_app_desc_t);
}

esp_err_t ota_update_image_inspect(
    const void *header, size_t header_size, size_t image_size,
    size_t max_image_size, ota_image_info_t *info)
{
    size_t required = ota_update_required_header_size();
    if (!header || !info || header_size < required ||
        image_size < required || max_image_size == 0) {
        return ESP_ERR_INVALID_ARG;
    }
    if (image_size > max_image_size) return ESP_ERR_INVALID_SIZE;

    const uint8_t *bytes = header;
    const esp_image_header_t *image = (const esp_image_header_t *)bytes;
    if (image->magic != ESP_IMAGE_HEADER_MAGIC)
        return ESP_ERR_INVALID_RESPONSE;
    if (image->chip_id != ESP_CHIP_ID_ESP32S3)
        return ESP_ERR_INVALID_VERSION;

    const esp_app_desc_t *description = (const esp_app_desc_t *)(
        bytes + sizeof(esp_image_header_t) +
        sizeof(esp_image_segment_header_t));
    if (description->magic_word != ESP_APP_DESC_MAGIC_WORD)
        return ESP_ERR_INVALID_RESPONSE;

    ota_image_info_t parsed = {
        .chip_id = image->chip_id,
    };
    copy_fixed_string(parsed.project_name, description->project_name);
    copy_fixed_string(parsed.version, description->version);
    if (strcmp(parsed.project_name, OTA_UPDATE_PROJECT_NAME) != 0 ||
        parsed.version[0] == '\0') {
        return ESP_ERR_INVALID_ARG;
    }

    *info = parsed;
    return ESP_OK;
}

esp_err_t ota_update_get_status(ota_update_status_t *status)
{
    if (!status) return ESP_ERR_INVALID_ARG;
    memset(status, 0, sizeof(*status));

    const esp_partition_t *running = esp_ota_get_running_partition();
    const esp_partition_t *next = esp_ota_get_next_update_partition(NULL);
    esp_app_desc_t description = {0};
    if (!running || !next ||
        esp_ota_get_partition_description(running, &description) != ESP_OK) {
        return ESP_ERR_NOT_SUPPORTED;
    }

    status->supported = true;
#if CONFIG_BOOTLOADER_APP_ROLLBACK_ENABLE
    status->rollback_enabled = true;
#endif
    status->max_image_size = next->size;
    snprintf(status->project_name, sizeof(status->project_name), "%s",
             description.project_name);
    snprintf(status->version, sizeof(status->version), "%s",
             description.version);
    return ESP_OK;
}

esp_err_t ota_update_session_begin(
    ota_update_session_t *session, const void *header, size_t header_size,
    size_t image_size)
{
    if (!session || session->active) return ESP_ERR_INVALID_STATE;

    const esp_partition_t *partition =
        esp_ota_get_next_update_partition(NULL);
    if (!partition) return ESP_ERR_NOT_SUPPORTED;

    ota_image_info_t image = {0};
    esp_err_t result = ota_update_image_inspect(
        header, header_size, image_size, partition->size, &image);
    if (result != ESP_OK) return result;

    esp_ota_handle_t handle = 0;
    result = esp_ota_begin(partition, image_size, &handle);
    if (result != ESP_OK) return result;

    memset(session, 0, sizeof(*session));
    session->partition = partition;
    session->handle = handle;
    session->expected_size = image_size;
    session->active = true;
    session->image = image;
    return ESP_OK;
}

esp_err_t ota_update_session_write(
    ota_update_session_t *session, const void *data, size_t size)
{
    if (!session || !session->active || (!data && size > 0))
        return ESP_ERR_INVALID_STATE;
    if (size > session->expected_size - session->written_size)
        return ESP_ERR_INVALID_SIZE;

    esp_err_t result = esp_ota_write(session->handle, data, size);
    if (result == ESP_OK) session->written_size += size;
    return result;
}

esp_err_t ota_update_session_finish(ota_update_session_t *session)
{
    if (!session || !session->active) return ESP_ERR_INVALID_STATE;
    if (session->written_size != session->expected_size) {
        ota_update_session_abort(session);
        return ESP_ERR_INVALID_SIZE;
    }

    esp_ota_handle_t handle = session->handle;
    const esp_partition_t *partition = session->partition;
    session->active = false;
    esp_err_t result = esp_ota_end(handle);
    if (result != ESP_OK) return result;

    result = esp_ota_set_boot_partition(partition);
    if (result != ESP_OK) return result;
    return ESP_OK;
}

void ota_update_session_abort(ota_update_session_t *session)
{
    if (!session || !session->active) return;
    esp_ota_abort(session->handle);
    session->active = false;
}

bool ota_update_health_ready(uint32_t uptime_ms, bool network_ready)
{
    return network_ready && uptime_ms >= OTA_UPDATE_HEALTH_DELAY_MS;
}

bool ota_update_running_pending_verify(void)
{
    const esp_partition_t *running = esp_ota_get_running_partition();
    esp_ota_img_states_t state;
    return running &&
           esp_ota_get_state_partition(running, &state) == ESP_OK &&
           state == ESP_OTA_IMG_PENDING_VERIFY;
}

esp_err_t ota_update_confirm_running(void)
{
    return esp_ota_mark_app_valid_cancel_rollback();
}
