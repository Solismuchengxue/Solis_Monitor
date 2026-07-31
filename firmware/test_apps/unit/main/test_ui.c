#include <stdio.h>
#include <string.h>

#include "esp_heap_caps.h"
#include "unity.h"
#include "ui.h"

static dashboard_state_t make_test_state(void)
{
    dashboard_state_t state = {0};

    snprintf(state.system.time, sizeof(state.system.time), "23:00");
    state.system.cpu_usage = 38;
    state.system.cpu_temp_c = 64;
    state.system.cpu_ghz = 4.8f;
    state.system.cpu_w = 95;
    state.system.gpu_usage = 71;
    state.system.gpu_temp_c = 68;
    state.system.gpu_ghz = 2.6f;
    state.system.gpu_w = 245;
    state.system.memory_usage = 46;
    state.system.fps = 144;
    state.system.nvme_temp_c = 43;
    state.system.download_mbps = 128;
    state.system.upload_mbps = 24;
    state.wifi_connected = true;
    snprintf(state.wifi_ssid, sizeof(state.wifi_ssid), "Solis-WiFi");

    state.codex.online = true;
    snprintf(state.codex.project, sizeof(state.codex.project), "Solis_Monitor");
    state.codex.context_used = 45;
    state.codex.main_weekly_remaining = 57;

    snprintf(state.environment.location, sizeof(state.environment.location), "大连");
    snprintf(state.environment.weather, sizeof(state.environment.weather), "阵雨");
    snprintf(state.environment.wind_direction, sizeof(state.environment.wind_direction), "东南风");
    snprintf(state.environment.wind_scale, sizeof(state.environment.wind_scale), "4");
    state.environment.weather_icon = 4;
    state.environment.outdoor_low_c = 20;
    state.environment.outdoor_high_c = 27;
    state.environment.indoor_temp_c = 25;
    state.environment.humidity = 56;
    dashboard_state_sanitize(&state);
    return state;
}

static bool in_dirty_rect(const ui_dirty_list_t *dirty, int x, int y)
{
    for (size_t i = 0; i < dirty->count; ++i) {
        renderer_rect_t rect = dirty->rects[i];
        if (x >= rect.x && x < rect.x + rect.w && y >= rect.y && y < rect.y + rect.h) {
            return true;
        }
    }
    return false;
}

static bool header_pixels_differ(const uint16_t *before, const renderer_surface_t *surface,
                                 const ui_dirty_list_t *dirty)
{
    for (int y = 16; y < 64; ++y) {
        for (int x = 24; x < 776; ++x) {
            size_t index = (size_t)y * 800 + x;
            if (before[index] != surface->pixels[index]) {
                return in_dirty_rect(dirty, x, y);
            }
        }
    }
    return false;
}

TEST_CASE("full page dirties exactly the screen", "[ui]")
{
    uint16_t *pixels = heap_caps_calloc(800 * 480, sizeof(uint16_t),
                                        MALLOC_CAP_SPIRAM | MALLOC_CAP_8BIT);
    TEST_ASSERT_NOT_NULL(pixels);
    renderer_surface_t s = {.pixels = pixels, .width = 800, .height = 480, .stride = 800};
    dashboard_state_t state;
    state = make_test_state();
    ui_dirty_list_t dirty = {0};
    ui_render_full(&s, UI_PAGE_PC, &state, &dirty);
    TEST_ASSERT_EQUAL(1, dirty.count);
    TEST_ASSERT_EQUAL_INT16(0, dirty.rects[0].x);
    TEST_ASSERT_EQUAL_INT16(0, dirty.rects[0].y);
    TEST_ASSERT_EQUAL_INT16(800, dirty.rects[0].w);
    TEST_ASSERT_EQUAL_INT16(480, dirty.rects[0].h);
    heap_caps_free(pixels);
}

TEST_CASE("dynamic rectangles stay inside 800 by 480", "[ui]")
{
    uint16_t *pixels = heap_caps_calloc(800 * 480, sizeof(uint16_t),
                                        MALLOC_CAP_SPIRAM | MALLOC_CAP_8BIT);
    TEST_ASSERT_NOT_NULL(pixels);
    renderer_surface_t s = {.pixels = pixels, .width = 800, .height = 480, .stride = 800};
    dashboard_state_t state;
    state = make_test_state();
    for (int page = UI_PAGE_PC; page <= UI_PAGE_CODEX; ++page) {
        ui_dirty_list_t dirty = {0};
        ui_render_update(&s, page, &state, &dirty);
        TEST_ASSERT_GREATER_THAN(0, dirty.count);
        TEST_ASSERT_LESS_OR_EQUAL(UI_MAX_DIRTY_RECTS, dirty.count);
        for (size_t i = 0; i < dirty.count; ++i) {
            renderer_rect_t r = dirty.rects[i];
            TEST_ASSERT_TRUE(r.x >= 0 && r.y >= 0 && r.x + r.w <= 800 && r.y + r.h <= 480);
        }
    }
    heap_caps_free(pixels);
}

TEST_CASE("PC update writes only returned dirty rectangles", "[ui]")
{
    uint16_t *pixels = heap_caps_calloc(800 * 480, sizeof(uint16_t),
                                        MALLOC_CAP_SPIRAM | MALLOC_CAP_8BIT);
    TEST_ASSERT_NOT_NULL(pixels);
    renderer_surface_t s = {.pixels = pixels, .width = 800, .height = 480, .stride = 800};
    dashboard_state_t state;
    state = make_test_state();
    ui_dirty_list_t dirty = {0};

    ui_render_update(&s, UI_PAGE_PC, &state, &dirty);

    for (int y = 0; y < 480; ++y) {
        for (int x = 0; x < 800; ++x) {
            if (pixels[y * 800 + x] != 0) {
                TEST_ASSERT_TRUE_MESSAGE(in_dirty_rect(&dirty, x, y),
                                         "update changed a pixel outside its dirty rectangles");
            }
        }
    }
    heap_caps_free(pixels);
}

TEST_CASE("Wi-Fi connection state changes PC header pixels", "[ui]")
{
    uint16_t *pixels = heap_caps_calloc(800 * 480, sizeof(uint16_t),
                                        MALLOC_CAP_SPIRAM | MALLOC_CAP_8BIT);
    uint16_t *before = heap_caps_calloc(800 * 480, sizeof(uint16_t),
                                        MALLOC_CAP_SPIRAM | MALLOC_CAP_8BIT);
    TEST_ASSERT_NOT_NULL(pixels);
    TEST_ASSERT_NOT_NULL(before);
    renderer_surface_t s = {.pixels = pixels, .width = 800, .height = 480, .stride = 800};
    dashboard_state_t state = make_test_state();

    state.wifi_connected = true;
    ui_dirty_list_t dirty = {0};
    ui_render_update(&s, UI_PAGE_PC, &state, &dirty);
    memcpy(before, pixels, 800 * 480 * sizeof(uint16_t));
    state.wifi_connected = false;
    state.wifi_ssid[0] = '\0';
    ui_render_update(&s, UI_PAGE_PC, &state, &dirty);

    TEST_ASSERT_TRUE_MESSAGE(header_pixels_differ(before, &s, &dirty),
                             "Wi-Fi state must change the PC header");
    heap_caps_free(before);
    heap_caps_free(pixels);
}

TEST_CASE("provisioning screen overrides either dashboard page", "[ui]")
{
    uint16_t *pixels = heap_caps_calloc(800 * 480, sizeof(uint16_t),
                                        MALLOC_CAP_SPIRAM | MALLOC_CAP_8BIT);
    TEST_ASSERT_NOT_NULL(pixels);
    renderer_surface_t surface = {.pixels = pixels, .width = 800, .height = 480, .stride = 800};
    dashboard_state_t state = make_test_state();
    state.provisioning_active = true;
    snprintf(state.provisioning_ssid, sizeof(state.provisioning_ssid), "Solis-Monitor-1234");
    state.provisioning_remaining_seconds = 599;
    ui_dirty_list_t dirty = {0};

    ui_render_update(&surface, UI_PAGE_CODEX, &state, &dirty);
    TEST_ASSERT_EQUAL_UINT32(1, dirty.count);
    TEST_ASSERT_EQUAL_INT16(800, dirty.rects[0].w);
    TEST_ASSERT_EQUAL_INT16(480, dirty.rects[0].h);
    heap_caps_free(pixels);
}

TEST_CASE("Codex online state changes Codex header pixels", "[ui]")
{
    uint16_t *pixels = heap_caps_calloc(800 * 480, sizeof(uint16_t),
                                        MALLOC_CAP_SPIRAM | MALLOC_CAP_8BIT);
    uint16_t *before = heap_caps_calloc(800 * 480, sizeof(uint16_t),
                                        MALLOC_CAP_SPIRAM | MALLOC_CAP_8BIT);
    TEST_ASSERT_NOT_NULL(pixels);
    TEST_ASSERT_NOT_NULL(before);
    renderer_surface_t s = {.pixels = pixels, .width = 800, .height = 480, .stride = 800};
    dashboard_state_t state = make_test_state();
    ui_dirty_list_t dirty = {0};

    state.codex.online = true;
    ui_render_update(&s, UI_PAGE_CODEX, &state, &dirty);
    memcpy(before, pixels, 800 * 480 * sizeof(uint16_t));
    state.codex.online = false;
    ui_render_update(&s, UI_PAGE_CODEX, &state, &dirty);

    TEST_ASSERT_TRUE_MESSAGE(header_pixels_differ(before, &s, &dirty),
                             "Codex online state must change the Codex header");
    heap_caps_free(before);
    heap_caps_free(pixels);
}

TEST_CASE("device discovery screen overrides either dashboard page", "[ui]")
{
    uint16_t *pixels = heap_caps_calloc(800 * 480, sizeof(uint16_t),
                                        MALLOC_CAP_SPIRAM | MALLOC_CAP_8BIT);
    TEST_ASSERT_NOT_NULL(pixels);
    renderer_surface_t surface = {
        .pixels = pixels, .width = 800, .height = 480, .stride = 800
    };
    dashboard_state_t state = make_test_state();
    state.discovery_active = true;
    snprintf(state.pairing_code, sizeof(state.pairing_code), "123456");
    state.pairing_code_remaining_seconds = 42;

    for (int page = UI_PAGE_PC; page <= UI_PAGE_CODEX; ++page) {
        ui_dirty_list_t dirty = {0};
        ui_render_update(&surface, page, &state, &dirty);
        TEST_ASSERT_EQUAL_UINT32(1, dirty.count);
        TEST_ASSERT_EQUAL_INT16(0, dirty.rects[0].x);
        TEST_ASSERT_EQUAL_INT16(0, dirty.rects[0].y);
        TEST_ASSERT_EQUAL_INT16(800, dirty.rects[0].w);
        TEST_ASSERT_EQUAL_INT16(480, dirty.rects[0].h);
    }

    heap_caps_free(pixels);
}

TEST_CASE("weather icon and wind changes update Codex weather card", "[ui]")
{
    uint16_t *pixels = heap_caps_calloc(800 * 480, sizeof(uint16_t),
                                        MALLOC_CAP_SPIRAM | MALLOC_CAP_8BIT);
    uint16_t *before = heap_caps_calloc(800 * 480, sizeof(uint16_t),
                                        MALLOC_CAP_SPIRAM | MALLOC_CAP_8BIT);
    TEST_ASSERT_NOT_NULL(pixels);
    TEST_ASSERT_NOT_NULL(before);
    renderer_surface_t s = {.pixels = pixels, .width = 800, .height = 480, .stride = 800};
    dashboard_state_t state = make_test_state();
    ui_dirty_list_t dirty = {0};

    state.environment.weather_icon = 0;
    snprintf(state.environment.wind_direction, sizeof(state.environment.wind_direction), "东风");
    ui_render_update(&s, UI_PAGE_CODEX, &state, &dirty);
    memcpy(before, pixels, 800 * 480 * sizeof(uint16_t));

    state.environment.weather_icon = 26;
    snprintf(state.environment.wind_direction, sizeof(state.environment.wind_direction), "西北风");
    ui_render_update(&s, UI_PAGE_CODEX, &state, &dirty);

    bool icon_changed = false;
    bool wind_changed = false;
    for (int y = 378; y < 426; ++y) {
        for (int x = 40; x < 88; ++x)
            icon_changed |= before[y * 800 + x] != pixels[y * 800 + x];
    }
    for (int y = 424; y < 456; ++y) {
        for (int x = 100; x < 360; ++x)
            wind_changed |= before[y * 800 + x] != pixels[y * 800 + x];
    }
    TEST_ASSERT_TRUE_MESSAGE(icon_changed, "weather icon must change rendered pixels");
    TEST_ASSERT_TRUE_MESSAGE(wind_changed, "wind text must change rendered pixels");

    heap_caps_free(before);
    heap_caps_free(pixels);
}

TEST_CASE("night backlight confirmation dirties the full screen", "[ui]")
{
    uint16_t *pixels = heap_caps_calloc(
        800 * 480, sizeof(uint16_t),
        MALLOC_CAP_SPIRAM | MALLOC_CAP_8BIT);
    TEST_ASSERT_NOT_NULL(pixels);
    renderer_surface_t surface = {
        .pixels = pixels, .width = 800, .height = 480, .stride = 800
    };
    ui_dirty_list_t dirty = {0};

    ui_render_night_settings_confirmation(
        &surface, true, 23 * 60 + 30, 7 * 60 + 30, &dirty);

    TEST_ASSERT_EQUAL_UINT32(1, dirty.count);
    TEST_ASSERT_EQUAL_INT16(0, dirty.rects[0].x);
    TEST_ASSERT_EQUAL_INT16(0, dirty.rects[0].y);
    TEST_ASSERT_EQUAL_INT16(800, dirty.rects[0].w);
    TEST_ASSERT_EQUAL_INT16(480, dirty.rects[0].h);
    TEST_ASSERT_NOT_EQUAL_UINT16(0, pixels[100 * 800 + 100]);

    heap_caps_free(pixels);
}
