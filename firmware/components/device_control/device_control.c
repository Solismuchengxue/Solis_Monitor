#include "device_control.h"

#include <stdlib.h>

#include "freertos/FreeRTOS.h"
#include "freertos/semphr.h"
#include "nvs.h"

#define DEVICE_CONTROL_NAMESPACE "device_ctl"

struct device_control {
    SemaphoreHandle_t lock;
    device_control_settings_t settings;
    bool night_settings_updated;
    bool restart_requested;
};

void device_control_settings_default(device_control_settings_t *settings)
{
    if (!settings) return;
    *settings = (device_control_settings_t){
        .brightness_percent = DEVICE_CONTROL_DEFAULT_BRIGHTNESS,
        .night_enabled = false,
        .night_start_minute = DEVICE_CONTROL_DEFAULT_NIGHT_START_MINUTE,
        .night_end_minute = DEVICE_CONTROL_DEFAULT_NIGHT_END_MINUTE,
        .utc_offset_minutes = 0,
    };
}

bool device_control_settings_valid(const device_control_settings_t *settings)
{
    return settings &&
           settings->brightness_percent >= DEVICE_CONTROL_MIN_BRIGHTNESS &&
           settings->brightness_percent <= DEVICE_CONTROL_MAX_BRIGHTNESS &&
           settings->night_start_minute < 24U * 60U &&
           settings->night_end_minute < 24U * 60U &&
           settings->night_start_minute != settings->night_end_minute &&
           settings->utc_offset_minutes >= DEVICE_CONTROL_MIN_UTC_OFFSET_MINUTES &&
           settings->utc_offset_minutes <= DEVICE_CONTROL_MAX_UTC_OFFSET_MINUTES;
}

bool device_control_night_active(uint16_t local_minute,
                                 uint16_t start_minute,
                                 uint16_t end_minute)
{
    if (local_minute >= 24U * 60U || start_minute >= 24U * 60U ||
        end_minute >= 24U * 60U || start_minute == end_minute) {
        return false;
    }
    if (start_minute < end_minute)
        return local_minute >= start_minute && local_minute < end_minute;
    return local_minute >= start_minute || local_minute < end_minute;
}

bool device_control_should_sleep(bool interaction_active,
                                 bool night_active,
                                 bool pc_inactive)
{
    return !interaction_active && (night_active || pc_inactive);
}

bool device_control_wake_active(uint32_t now_ms, uint32_t wake_until_ms)
{
    return (int32_t)(wake_until_ms - now_ms) > 0;
}

static esp_err_t load_settings(device_control_settings_t *settings)
{
    nvs_handle_t handle;
    esp_err_t result = nvs_open(
        DEVICE_CONTROL_NAMESPACE, NVS_READONLY, &handle);
    if (result == ESP_ERR_NVS_NOT_FOUND) return ESP_OK;
    if (result != ESP_OK) return result;

    uint8_t night_enabled = settings->night_enabled ? 1U : 0U;
    esp_err_t item_result = nvs_get_u8(
        handle, "brightness", &settings->brightness_percent);
    if (item_result != ESP_OK && item_result != ESP_ERR_NVS_NOT_FOUND)
        result = item_result;
    item_result = nvs_get_u8(handle, "night", &night_enabled);
    if (item_result != ESP_OK && item_result != ESP_ERR_NVS_NOT_FOUND)
        result = item_result;
    item_result = nvs_get_u16(
        handle, "night_start", &settings->night_start_minute);
    if (item_result != ESP_OK && item_result != ESP_ERR_NVS_NOT_FOUND)
        result = item_result;
    item_result = nvs_get_u16(
        handle, "night_end", &settings->night_end_minute);
    if (item_result != ESP_OK && item_result != ESP_ERR_NVS_NOT_FOUND)
        result = item_result;
    item_result = nvs_get_i16(
        handle, "utc_offset", &settings->utc_offset_minutes);
    if (item_result != ESP_OK && item_result != ESP_ERR_NVS_NOT_FOUND)
        result = item_result;
    nvs_close(handle);
    settings->night_enabled = night_enabled != 0;
    return result;
}

static esp_err_t save_settings(const device_control_settings_t *settings)
{
    nvs_handle_t handle;
    esp_err_t result = nvs_open(
        DEVICE_CONTROL_NAMESPACE, NVS_READWRITE, &handle);
    if (result != ESP_OK) return result;

    result = nvs_set_u8(handle, "brightness", settings->brightness_percent);
    if (result == ESP_OK)
        result = nvs_set_u8(handle, "night", settings->night_enabled ? 1U : 0U);
    if (result == ESP_OK)
        result = nvs_set_u16(
            handle, "night_start", settings->night_start_minute);
    if (result == ESP_OK)
        result = nvs_set_u16(
            handle, "night_end", settings->night_end_minute);
    if (result == ESP_OK)
        result = nvs_set_i16(
            handle, "utc_offset", settings->utc_offset_minutes);
    if (result == ESP_OK) result = nvs_commit(handle);
    nvs_close(handle);
    return result;
}

esp_err_t device_control_init(device_control_t **control)
{
    if (!control || *control) return ESP_ERR_INVALID_ARG;
    device_control_t *context = calloc(1, sizeof(*context));
    if (!context) return ESP_ERR_NO_MEM;
    context->lock = xSemaphoreCreateMutex();
    if (!context->lock) {
        free(context);
        return ESP_ERR_NO_MEM;
    }

    device_control_settings_default(&context->settings);
    esp_err_t result = load_settings(&context->settings);
    if (result != ESP_OK || !device_control_settings_valid(&context->settings)) {
        device_control_settings_default(&context->settings);
    }
    *control = context;
    return ESP_OK;
}

void device_control_deinit(device_control_t *control)
{
    if (!control) return;
    vSemaphoreDelete(control->lock);
    free(control);
}

esp_err_t device_control_get(device_control_t *control,
                             device_control_settings_t *settings)
{
    if (!control || !settings) return ESP_ERR_INVALID_ARG;
    if (xSemaphoreTake(control->lock, portMAX_DELAY) != pdTRUE)
        return ESP_ERR_TIMEOUT;
    *settings = control->settings;
    xSemaphoreGive(control->lock);
    return ESP_OK;
}

esp_err_t device_control_update(device_control_t *control,
                                const device_control_settings_t *settings)
{
    if (!control || !device_control_settings_valid(settings))
        return ESP_ERR_INVALID_ARG;
    esp_err_t result = save_settings(settings);
    if (result != ESP_OK) return result;
    if (xSemaphoreTake(control->lock, portMAX_DELAY) != pdTRUE)
        return ESP_ERR_TIMEOUT;
    bool night_changed =
        control->settings.night_enabled != settings->night_enabled ||
        control->settings.night_start_minute != settings->night_start_minute ||
        control->settings.night_end_minute != settings->night_end_minute;
    control->settings = *settings;
    control->night_settings_updated =
        control->night_settings_updated || night_changed;
    xSemaphoreGive(control->lock);
    return ESP_OK;
}

bool device_control_take_night_settings_updated(
    device_control_t *control, device_control_settings_t *settings)
{
    if (!control || !settings) return false;
    bool updated = false;
    if (xSemaphoreTake(control->lock, portMAX_DELAY) == pdTRUE) {
        updated = control->night_settings_updated;
        if (updated) {
            *settings = control->settings;
            control->night_settings_updated = false;
        }
        xSemaphoreGive(control->lock);
    }
    return updated;
}

void device_control_request_restart(device_control_t *control)
{
    if (!control) return;
    if (xSemaphoreTake(control->lock, portMAX_DELAY) == pdTRUE) {
        control->restart_requested = true;
        xSemaphoreGive(control->lock);
    }
}

bool device_control_take_restart_requested(device_control_t *control)
{
    if (!control) return false;
    bool requested = false;
    if (xSemaphoreTake(control->lock, portMAX_DELAY) == pdTRUE) {
        requested = control->restart_requested;
        control->restart_requested = false;
        xSemaphoreGive(control->lock);
    }
    return requested;
}
