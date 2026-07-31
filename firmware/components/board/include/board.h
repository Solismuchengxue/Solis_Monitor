#pragma once

#include <stdbool.h>
#include <stdint.h>

#include "esp_err.h"
#include "button_debounce.h"

esp_err_t board_init(void);
void board_backlight_set(bool on);
esp_err_t board_backlight_set_percent(uint8_t percent);
button_event_t board_button_event(uint32_t now_ms);
