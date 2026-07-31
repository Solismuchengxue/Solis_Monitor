#pragma once

#include <stdbool.h>
#include <stdint.h>

#include "esp_err.h"

#define DEVICE_CONTROL_DEFAULT_BRIGHTNESS 100U
#define DEVICE_CONTROL_DEFAULT_NIGHT_START_MINUTE (23U * 60U + 30U)
#define DEVICE_CONTROL_DEFAULT_NIGHT_END_MINUTE (7U * 60U + 30U)
#define DEVICE_CONTROL_MIN_BRIGHTNESS 10U
#define DEVICE_CONTROL_MAX_BRIGHTNESS 100U
#define DEVICE_CONTROL_MIN_UTC_OFFSET_MINUTES (-12 * 60)
#define DEVICE_CONTROL_MAX_UTC_OFFSET_MINUTES (14 * 60)

typedef struct {
    uint8_t brightness_percent;
    bool night_enabled;
    uint16_t night_start_minute;
    uint16_t night_end_minute;
    int16_t utc_offset_minutes;
} device_control_settings_t;

typedef struct device_control device_control_t;

void device_control_settings_default(device_control_settings_t *settings);
bool device_control_settings_valid(const device_control_settings_t *settings);
bool device_control_night_active(uint16_t local_minute,
                                 uint16_t start_minute,
                                 uint16_t end_minute);
bool device_control_should_sleep(bool interaction_active,
                                 bool night_active,
                                 bool pc_inactive);
bool device_control_wake_active(uint32_t now_ms, uint32_t wake_until_ms);

esp_err_t device_control_init(device_control_t **control);
void device_control_deinit(device_control_t *control);
esp_err_t device_control_get(device_control_t *control,
                             device_control_settings_t *settings);
esp_err_t device_control_update(device_control_t *control,
                                const device_control_settings_t *settings);
bool device_control_take_night_settings_updated(
    device_control_t *control, device_control_settings_t *settings);
void device_control_request_restart(device_control_t *control);
bool device_control_take_restart_requested(device_control_t *control);
