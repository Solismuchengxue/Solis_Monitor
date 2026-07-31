#pragma once

#include <stdbool.h>
#include <stdint.h>

#include "renderer.h"

typedef enum { UI_ASSET_PAGE_PC = 0, UI_ASSET_PAGE_CODEX = 1 } ui_page_asset_t;

#define UI_ASSETS_WEATHER_ICON_COUNT 27u

const uint16_t *ui_assets_page(ui_page_asset_t page);
const uint8_t *ui_assets_weather_icon(unsigned index);
const uint8_t *ui_assets_wifi_icon(bool connected);
const bitmap_font_t *ui_assets_font(unsigned pixel_size);
