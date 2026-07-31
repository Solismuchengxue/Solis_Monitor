#pragma once

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

#include "esp_err.h"
#include "esp_ota_ops.h"
#include "esp_partition.h"

#define OTA_UPDATE_PROJECT_NAME "solis_monitor"
#define OTA_UPDATE_HEALTH_DELAY_MS 30000U

typedef struct {
    char project_name[33];
    char version[33];
    uint16_t chip_id;
} ota_image_info_t;

typedef struct {
    bool supported;
    bool rollback_enabled;
    size_t max_image_size;
    char project_name[33];
    char version[33];
} ota_update_status_t;

typedef struct {
    const esp_partition_t *partition;
    esp_ota_handle_t handle;
    size_t expected_size;
    size_t written_size;
    bool active;
    ota_image_info_t image;
} ota_update_session_t;

size_t ota_update_required_header_size(void);
esp_err_t ota_update_image_inspect(
    const void *header, size_t header_size, size_t image_size,
    size_t max_image_size, ota_image_info_t *info);
esp_err_t ota_update_get_status(ota_update_status_t *status);
esp_err_t ota_update_session_begin(
    ota_update_session_t *session, const void *header, size_t header_size,
    size_t image_size);
esp_err_t ota_update_session_write(
    ota_update_session_t *session, const void *data, size_t size);
esp_err_t ota_update_session_finish(ota_update_session_t *session);
void ota_update_session_abort(ota_update_session_t *session);
bool ota_update_health_ready(uint32_t uptime_ms, bool network_ready);
bool ota_update_running_pending_verify(void);
esp_err_t ota_update_confirm_running(void);
