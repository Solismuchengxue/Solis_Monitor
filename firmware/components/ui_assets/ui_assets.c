#include <stddef.h>

#include "ui_assets.h"

extern const uint8_t pc_start[] asm("_binary_generated_page_pc_rgb565_start");
extern const uint8_t codex_start[] asm("_binary_generated_page_codex_rgb565_start");
extern const uint8_t wifi_up_start[] asm("_binary_generated_wifi_up_rgba565_start");
extern const uint8_t wifi_down_start[] asm("_binary_generated_wifi_down_rgba565_start");
#define WEATHER_ICON(index) \
    extern const uint8_t weather_##index##_start[] \
        asm("_binary_generated_weather_m" #index "_rgba565_start")
WEATHER_ICON(00);
WEATHER_ICON(01);
WEATHER_ICON(02);
WEATHER_ICON(03);
WEATHER_ICON(04);
WEATHER_ICON(05);
WEATHER_ICON(06);
WEATHER_ICON(07);
WEATHER_ICON(08);
WEATHER_ICON(09);
WEATHER_ICON(10);
WEATHER_ICON(11);
WEATHER_ICON(12);
WEATHER_ICON(13);
WEATHER_ICON(14);
WEATHER_ICON(15);
WEATHER_ICON(16);
WEATHER_ICON(17);
WEATHER_ICON(18);
WEATHER_ICON(19);
WEATHER_ICON(20);
WEATHER_ICON(21);
WEATHER_ICON(22);
WEATHER_ICON(23);
WEATHER_ICON(24);
WEATHER_ICON(25);
WEATHER_ICON(26);
extern const bitmap_font_t generated_font_20;
extern const bitmap_font_t generated_font_24;
extern const bitmap_font_t generated_font_56;

const uint16_t *ui_assets_page(ui_page_asset_t page)
{
    return (const uint16_t *)(page == UI_ASSET_PAGE_CODEX ? codex_start : pc_start);
}

const uint8_t *ui_assets_weather_icon(unsigned index)
{
    static const uint8_t *const icons[] = {
        weather_00_start, weather_01_start, weather_02_start, weather_03_start,
        weather_04_start, weather_05_start, weather_06_start, weather_07_start,
        weather_08_start, weather_09_start, weather_10_start, weather_11_start,
        weather_12_start, weather_13_start, weather_14_start, weather_15_start,
        weather_16_start, weather_17_start, weather_18_start, weather_19_start,
        weather_20_start, weather_21_start, weather_22_start, weather_23_start,
        weather_24_start, weather_25_start, weather_26_start,
    };
    _Static_assert(sizeof(icons) / sizeof(icons[0]) == UI_ASSETS_WEATHER_ICON_COUNT,
                   "weather icon table and public count must match");
    return index < UI_ASSETS_WEATHER_ICON_COUNT ? icons[index] : NULL;
}

const uint8_t *ui_assets_wifi_icon(bool connected)
{
    return connected ? wifi_up_start : wifi_down_start;
}

const bitmap_font_t *ui_assets_font(unsigned pixel_size)
{
    if (pixel_size == 20) return &generated_font_20;
    if (pixel_size == 24) return &generated_font_24;
    if (pixel_size == 56) return &generated_font_56;
    return NULL;
}
