#include "ui.h"

#include <math.h>
#include <stdio.h>
#include <string.h>

#include "ui_assets.h"

#define UI_WIDTH 800
#define UI_HEIGHT 480

static const renderer_rect_t PC_DYNAMIC[] = {
    {24, 16, 752, 40}, {40, 82, 332, 156}, {416, 82, 344, 156},
    {40, 276, 214, 164}, {298, 276, 462, 164},
};

static const renderer_rect_t CODEX_DYNAMIC[] = {
    {24, 16, 752, 40}, {40, 84, 364, 220}, {448, 84, 312, 248},
    {40, 348, 720, 108},
};

static int valid_surface(const renderer_surface_t *surface)
{
    return surface && surface->pixels && surface->width == UI_WIDTH &&
           surface->height == UI_HEIGHT && surface->stride >= UI_WIDTH;
}

static int valid_page(ui_page_t page)
{
    return page == UI_PAGE_PC || page == UI_PAGE_CODEX;
}

static const uint16_t *page_background(ui_page_t page)
{
    return ui_assets_page(page == UI_PAGE_CODEX ? UI_ASSET_PAGE_CODEX : UI_ASSET_PAGE_PC);
}

static void restore_rect(renderer_surface_t *surface, const uint16_t *background,
                         renderer_rect_t rect)
{
    for (int y = rect.y; y < rect.y + rect.h; ++y) {
        memcpy(surface->pixels + y * surface->stride + rect.x,
               background + y * UI_WIDTH + rect.x, (size_t)rect.w * sizeof(uint16_t));
    }
}

static const char *text_or(const char *text, const char *fallback)
{
    return text && text[0] ? text : fallback;
}

static void format_metric(char *destination, size_t destination_size,
                          float value, const char *format)
{
    if (isfinite(value)) snprintf(destination, destination_size, format, value);
    else snprintf(destination, destination_size, "--");
}

static void copy_short_text(char *destination, size_t destination_size,
                            const char *source, size_t max_bytes)
{
    size_t source_length = strlen(source);
    size_t length = source_length < max_bytes ? source_length : max_bytes;
    if (length >= destination_size) length = destination_size - 1;
    if (length < source_length) {
        while (length > 0 && ((unsigned char)source[length] & 0xC0) == 0x80) length--;
    }
    memcpy(destination, source, length);
    destination[length] = '\0';
}

static void draw_pc(renderer_surface_t *surface, const dashboard_state_t *state)
{
    const bitmap_font_t *font20 = ui_assets_font(20);
    const bitmap_font_t *font24 = ui_assets_font(24);
    const bitmap_font_t *font56 = ui_assets_font(56);
    const system_metrics_t *m = &state->system;
    uint16_t white = 0xFFFF;
    uint16_t cyan = renderer_rgb565(76, 220, 255);
    uint16_t purple = renderer_rgb565(136, 119, 255);
    uint16_t green = renderer_rgb565(68, 214, 165);
    uint16_t label = renderer_rgb565(190, 204, 219);
    char value[128];
    char details[128];
    char name[48];
    char first[24];
    char second[24];
    char third[24];

    format_metric(first, sizeof(first), m->download_mbps, "%.1f Mbps");
    renderer_draw_text(surface, 56, 40, first, font20, cyan);
    format_metric(second, sizeof(second), m->upload_mbps, "%.1f Mbps");
    renderer_draw_text(surface, 272, 40, second, font20, cyan);
    renderer_draw_rgba565(surface, 570, 18, 28, 28,
                          ui_assets_wifi_icon(state->wifi_connected));
    copy_short_text(name, sizeof(name),
                    state->wifi_connected ? text_or(state->wifi_ssid, "--") : "无连接", 14);
    renderer_draw_text(surface, 610, 40, name, font20,
                       state->wifi_connected ? green : purple);

    copy_short_text(name, sizeof(name), text_or(m->cpu_name, "CPU"), 28);
    renderer_draw_text(surface, 84, 108, name, font20, white);
    format_metric(value, sizeof(value), m->cpu_usage, "%.0f%%");
    renderer_draw_text(surface, 40, 174, value, font56, cyan);
    format_metric(first, sizeof(first), m->cpu_ghz, "%.1fGHz");
    format_metric(second, sizeof(second), m->cpu_w, "%.0fW");
    format_metric(third, sizeof(third), m->cpu_temp_c, "%.0f°C");
    snprintf(details, sizeof(details), "%s  %s  %s", first, second, third);
    renderer_draw_text(surface, 190, 198, details, font20, label);

    copy_short_text(name, sizeof(name), text_or(m->gpu_name, "GPU"), 28);
    renderer_draw_text(surface, 460, 108, name, font20, white);
    format_metric(value, sizeof(value), m->gpu_usage, "%.0f%%");
    renderer_draw_text(surface, 416, 174, value, font56, purple);
    format_metric(first, sizeof(first), m->gpu_ghz, "%.1fGHz");
    format_metric(second, sizeof(second), m->gpu_w, "%.0fW");
    format_metric(third, sizeof(third), m->gpu_temp_c, "%.0f°C");
    snprintf(details, sizeof(details), "%s  %s  %s", first, second, third);
    renderer_draw_text(surface, 550, 172, details, font20, label);
    format_metric(first, sizeof(first), m->gpu_memory_used_mb / 1024.0f, "%.1fG");
    format_metric(second, sizeof(second), m->gpu_memory_total_mb / 1024.0f, "%.1fG");
    format_metric(third, sizeof(third), m->gpu_memory_temp_c, "%.0f°C");
    snprintf(details, sizeof(details), "%s/%s  %s", first, second, third);
    renderer_draw_text(surface, 550, 212, details, font20, white);

    renderer_draw_text(surface, 84, 302, "内存", font20, label);
    format_metric(first, sizeof(first), m->memory_used_gb, "%.1f");
    format_metric(second, sizeof(second), m->memory_total_gb, "%.1f");
    snprintf(value, sizeof(value), "%s / %s GB", first, second);
    renderer_draw_text(surface, 40, 360, value, font24, white);
    format_metric(first, sizeof(first), m->memory_usage, "%.0f%%");
    format_metric(second, sizeof(second), m->memory_temp_c, "%.0f°C");
    snprintf(value, sizeof(value), "%s   %s", first, second);
    renderer_draw_text(surface, 40, 414, value, font20, cyan);

    renderer_draw_text(surface, 342, 302, "物理硬盘", font20, label);
    if (m->storage_count == 0) {
        renderer_draw_text(surface, 304, 350, "--", font24, white);
    }
    for (unsigned index = 0; index < m->storage_count; ++index) {
        copy_short_text(name, sizeof(name), text_or(m->storage[index].name, "DISK"), 28);
        renderer_draw_text(surface, 304, 342 + (int)index * 28, name, font20, white);
        format_metric(first, sizeof(first), m->storage[index].usage, "%.0f%%");
        format_metric(second, sizeof(second), m->storage[index].temp_c, "%.0f°C");
        snprintf(value, sizeof(value), "%s  %s", first, second);
        renderer_draw_text(surface, 650, 342 + (int)index * 28, value, font20, cyan);
    }
}

static void draw_codex(renderer_surface_t *surface, const dashboard_state_t *state)
{
    const bitmap_font_t *font20 = ui_assets_font(20);
    const bitmap_font_t *font24 = ui_assets_font(24);
    const codex_metrics_t *codex = &state->codex;
    const environment_metrics_t *environment = &state->environment;
    uint16_t white = 0xFFFF;
    uint16_t cyan = renderer_rgb565(76, 220, 255);
    uint16_t purple = renderer_rgb565(136, 119, 255);
    uint16_t green = renderer_rgb565(68, 214, 165);
    uint16_t label = renderer_rgb565(190, 204, 219);
    uint16_t track = renderer_rgb565(41, 52, 66);
    const int weekly_token_x = 410;
    const int account_token_x = 590;
    char value[128];
    char details[96];
    char name[48];
    char location[64];
    char percent_text[16];
    char first[24];
    char second[24];

    format_metric(first, sizeof(first), environment->indoor_temp_c, "%.1f°C");
    renderer_draw_text(surface, 54, 40, first, font20, cyan);
    format_metric(first, sizeof(first), environment->humidity, "%.0f%%");
    renderer_draw_text(surface, 250, 40, first, font20, green);
    renderer_draw_text(surface, 620, 40, codex->online ? "CODEX 活跃" : "CODEX 不活跃", font20,
                       codex->online ? green : purple);

    renderer_draw_text(surface, 40, 106, "上下文", font20, label);
    copy_short_text(name, sizeof(name), text_or(codex->project, "--"), 24);
    snprintf(details, sizeof(details), "项目  %s", name);
    renderer_draw_text(surface, 40, 142, details, font20, white);
    copy_short_text(name, sizeof(name), text_or(codex->model, "--"), 18);
    snprintf(details, sizeof(details), "模型 %s   推理 %s",
             name, text_or(codex->reasoning_effort, "--"));
    renderer_draw_text(surface, 40, 176, details, font20, label);
    if (isfinite(codex->context_used) &&
        isfinite(codex->context_used_k) &&
        isfinite(codex->context_window_k))
    {
        snprintf(value, sizeof(value), "%.1fK / %.1fK", codex->context_used_k, codex->context_window_k);
        snprintf(percent_text, sizeof(percent_text), "%.0f%%", codex->context_used);
    }
    else
    {
        snprintf(value, sizeof(value), "--");
        snprintf(percent_text, sizeof(percent_text), "--");
    }

    renderer_draw_text(surface, 40, 218, value, font24, white);
    renderer_draw_text(surface, 276, 218, percent_text, font24, cyan);
    renderer_draw_progress(surface, (renderer_rect_t){40, 240, 364, 12},
                           isfinite(codex->context_used) ? (unsigned)codex->context_used : 0,
                           track, cyan);

    renderer_draw_text(surface, 448, 106, "主周额度", font20, label);
    if (isfinite(codex->main_weekly_remaining))
    {
        snprintf(value, sizeof(value), "%.0f%%", codex->main_weekly_remaining);
    }
    else
    {
        snprintf(value, sizeof(value), "待更新");
    }
    renderer_draw_text(surface, 700, 106, value, font24, white);
    if (isfinite(codex->main_weekly_remaining))
    {
        snprintf(details, sizeof(details), "重置 %s", text_or(codex->main_quota_reset_at, "--"));
        renderer_draw_progress(surface, (renderer_rect_t){448, 128, 312, 8},
                                (unsigned)codex->main_weekly_remaining, track, purple);
    }
    else
    {
        snprintf(details, sizeof(details), "重置 --");
    }
    renderer_draw_text(surface, 448, 160, details, font20, label);
    renderer_fill_rect(surface, (renderer_rect_t){448, 188, 312, 2}, track);

    renderer_draw_text(surface, 448, 218, "GPT-5.3-Codex-Spark", font20, label);
    if (isfinite(codex->spark_weekly_remaining))
    {
        snprintf(value, sizeof(value), "%.0f%%", codex->spark_weekly_remaining);
    }
    else
    {
        snprintf(value, sizeof(value), "待更新");
    }
    renderer_draw_text(surface, 700, 218, value, font24, white);
    if (isfinite(codex->spark_weekly_remaining))
        renderer_draw_progress(surface, (renderer_rect_t){448, 240, 312, 8},
                               (unsigned)codex->spark_weekly_remaining, track, purple);
    snprintf(details, sizeof(details), "重置 %s", strlen(codex->spark_quota_reset_at) > 0 ? codex->spark_quota_reset_at : "--");
    renderer_draw_text(surface, 448, 272, details, font20, label);

    if (isfinite(environment->weather_icon) && environment->weather_icon >= 0 &&
        environment->weather_icon < UI_ASSETS_WEATHER_ICON_COUNT) {
        const uint8_t *icon = ui_assets_weather_icon((unsigned)environment->weather_icon);
        if (icon) renderer_draw_rgba565(surface, 40, 378, 48, 48, icon);
    }
    copy_short_text(location, sizeof(location), text_or(environment->location, "--"), 36);
    renderer_draw_text(surface, 80, 370, location, font20, label);
    format_metric(first, sizeof(first), environment->outdoor_low_c, "%.0f°C");
    format_metric(second, sizeof(second), environment->outdoor_high_c, "%.0f°C");
    snprintf(value, sizeof(value), "%s  %s/%s", text_or(environment->weather, "--"),
             first, second);
    renderer_draw_text(surface, 100, 402, value, font24, white);
    snprintf(value, sizeof(value), "%s  %s级",
             text_or(environment->wind_direction, "--"),
             text_or(environment->wind_scale, "--"));
    renderer_draw_text(surface, 100, 436, value, font20, label);

    renderer_draw_text(surface, weekly_token_x, 374, "周使用 TOKEN", font20, label);
    if (!isfinite(codex->weekly_used_tokens))
        snprintf(value, sizeof(value), "--");
    else
        snprintf(value, sizeof(value), "%.2f亿", codex->weekly_used_tokens / 100000000.0f);
    renderer_draw_text(surface, weekly_token_x, 424, value, font24, green);

    renderer_draw_text(surface, account_token_x, 374, "账户累计 TOKEN", font20, label);
    if (!isfinite(codex->total_tokens))
        snprintf(value, sizeof(value), "--");
    else
        snprintf(value, sizeof(value), "%.2f亿", codex->total_tokens / 100000000.0f);
    renderer_draw_text(surface, account_token_x, 424, value, font24, cyan);
}

static void draw_values(renderer_surface_t *surface, ui_page_t page,
                        const dashboard_state_t *state)
{
    if (page == UI_PAGE_PC) draw_pc(surface, state);
    else draw_codex(surface, state);
}

static void draw_provisioning(renderer_surface_t *surface, const dashboard_state_t *state)
{
    const bitmap_font_t *font20 = ui_assets_font(20);
    const bitmap_font_t *font24 = ui_assets_font(24);
    const bitmap_font_t *font56 = ui_assets_font(56);
    uint16_t background = renderer_rgb565(3, 13, 20);
    uint16_t card = renderer_rgb565(13, 24, 42);
    uint16_t cyan = renderer_rgb565(76, 220, 255);
    uint16_t white = 0xFFFF;
    uint16_t label = renderer_rgb565(190, 204, 219);
    char remaining[16];

    renderer_fill_rect(surface, (renderer_rect_t){0, 0, UI_WIDTH, UI_HEIGHT}, background);
    renderer_fill_rect(surface, (renderer_rect_t){80, 64, 640, 352}, card);
    renderer_draw_text(surface, 292, 124, "Solis Monitor 配网", font24, cyan);
    renderer_draw_text(surface, 132, 190, "热点", font20, label);
    renderer_draw_text(surface, 260, 190, text_or(state->provisioning_ssid, "--"), font24, white);
    renderer_draw_text(surface, 132, 252, "地址", font20, label);
    renderer_draw_text(surface, 260, 252, "192.168.0.1", font24, white);
    renderer_draw_text(surface, 132, 314, "剩余时间", font20, label);
    snprintf(remaining, sizeof(remaining), "%02u:%02u",
             state->provisioning_remaining_seconds / 60,
             state->provisioning_remaining_seconds % 60);
    renderer_draw_text(surface, 410, 330, remaining, font56, cyan);
    renderer_draw_text(surface, 340, 382, "单击退出", font20, label);
}

static void draw_pairing(
    renderer_surface_t *surface, const dashboard_state_t *state)
{
    const bitmap_font_t *font20 = ui_assets_font(20);
    const bitmap_font_t *font24 = ui_assets_font(24);
    const bitmap_font_t *font56 = ui_assets_font(56);
    uint16_t background = renderer_rgb565(3, 13, 20);
    uint16_t card = renderer_rgb565(13, 24, 42);
    uint16_t green = renderer_rgb565(68, 214, 165);
    uint16_t cyan = renderer_rgb565(76, 220, 255);
    uint16_t white = 0xFFFF;
    uint16_t label = renderer_rgb565(190, 204, 219);
    char remaining[32];

    renderer_fill_rect(surface, (renderer_rect_t){0, 0, UI_WIDTH, UI_HEIGHT}, background);
    renderer_fill_rect(surface, (renderer_rect_t){80, 64, 640, 352}, card);
    if (state->pairing_completed) {
        renderer_draw_text(surface, 292, 124, "Solis Monitor 配对", font24, green);
        renderer_draw_text(surface, 310, 220, "已成功配对", font24, white);
        renderer_draw_text(surface, 264, 286, "PC 已获得设备访问权限", font20, label);
        renderer_draw_text(surface, 340, 382, "单击退出", font20, label);
        return;
    }
    renderer_draw_text(surface, 352, 112, "开启发现", font24, cyan);
    renderer_draw_text(surface, 253, 154,
                       "请在 PC 端设备向导中选择此设备", font20, label);
    renderer_draw_text(surface, 370, 210, "配对码", font20, label);
    renderer_draw_text(surface, 304, 270,
                       state->pairing_code[0] ? state->pairing_code : "------",
                       font56, white);
    renderer_fill_rect(surface, (renderer_rect_t){240, 294, 320, 1}, label);
    unsigned remaining_seconds = state->pairing_code_remaining_seconds;
    snprintf(remaining, sizeof(remaining), "%u 秒后刷新", remaining_seconds);
    int remaining_width = (remaining_seconds >= 10 ? 2 : 1) * 14 + 6 + 4 * 24;
    int remaining_x = (UI_WIDTH - remaining_width) / 2;
    renderer_draw_text(surface, remaining_x, 338, remaining, font24, cyan);
    renderer_fill_rect(surface, (renderer_rect_t){160, 360, 480, 1}, label);
    renderer_draw_text(surface, 360, 398, "单击退出", font20, label);
}

void ui_render_night_settings_confirmation(
    renderer_surface_t *surface, bool enabled, unsigned start_minute,
    unsigned end_minute, ui_dirty_list_t *dirty)
{
    if (dirty) dirty->count = 0;
    if (!dirty || !valid_surface(surface) ||
        start_minute >= 24U * 60U || end_minute >= 24U * 60U) {
        return;
    }

    const bitmap_font_t *font20 = ui_assets_font(20);
    const bitmap_font_t *font24 = ui_assets_font(24);
    const bitmap_font_t *font56 = ui_assets_font(56);
    uint16_t background = renderer_rgb565(3, 13, 20);
    uint16_t card = renderer_rgb565(13, 24, 42);
    uint16_t green = renderer_rgb565(68, 214, 165);
    uint16_t white = 0xFFFF;
    uint16_t label = renderer_rgb565(190, 204, 219);
    char period[32];

    renderer_fill_rect(
        surface, (renderer_rect_t){0, 0, UI_WIDTH, UI_HEIGHT}, background);
    renderer_fill_rect(
        surface, (renderer_rect_t){80, 64, 640, 352}, card);
    renderer_draw_text(
        surface, 292, 134, "夜间背光设置成功", font24, green);
    renderer_draw_text(
        surface, enabled ? 352 : 340, 210,
        enabled ? "已启用" : "已关闭", font24, white);
    snprintf(
        period, sizeof(period), "%02u:%02u - %02u:%02u",
        start_minute / 60, start_minute % 60,
        end_minute / 60, end_minute % 60);
    renderer_draw_text(surface, 206, 298, period, font56, white);
    renderer_draw_text(surface, 332, 374, "5 秒后返回", font20, label);

    dirty->rects[0] = (renderer_rect_t){0, 0, UI_WIDTH, UI_HEIGHT};
    dirty->count = 1;
}

void ui_render_full(renderer_surface_t *surface, ui_page_t page,
                    const dashboard_state_t *state, ui_dirty_list_t *dirty)
{
    if (dirty) dirty->count = 0;
    if (!dirty || !state || !valid_surface(surface) || !valid_page(page)) return;

    if (state->provisioning_active) {
        draw_provisioning(surface, state);
        dirty->rects[0] = (renderer_rect_t){0, 0, UI_WIDTH, UI_HEIGHT};
        dirty->count = 1;
        return;
    }
    if (state->discovery_active || state->pairing_completed) {
        draw_pairing(surface, state);
        dirty->rects[0] = (renderer_rect_t){0, 0, UI_WIDTH, UI_HEIGHT};
        dirty->count = 1;
        return;
    }
    const uint16_t *background = page_background(page);
    for (int y = 0; y < UI_HEIGHT; ++y) {
        memcpy(surface->pixels + y * surface->stride, background + y * UI_WIDTH,
               UI_WIDTH * sizeof(uint16_t));
    }
    draw_values(surface, page, state);
    dirty->rects[0] = (renderer_rect_t){0, 0, UI_WIDTH, UI_HEIGHT};
    dirty->count = 1;
}

void ui_render_update(renderer_surface_t *surface, ui_page_t page,
                      const dashboard_state_t *state, ui_dirty_list_t *dirty)
{
    if (dirty) dirty->count = 0;
    if (!dirty || !state || !valid_surface(surface) || !valid_page(page)) return;

    if (state->provisioning_active) {
        draw_provisioning(surface, state);
        dirty->rects[0] = (renderer_rect_t){0, 0, UI_WIDTH, UI_HEIGHT};
        dirty->count = 1;
        return;
    }
    if (state->discovery_active || state->pairing_completed) {
        draw_pairing(surface, state);
        dirty->rects[0] = (renderer_rect_t){0, 0, UI_WIDTH, UI_HEIGHT};
        dirty->count = 1;
        return;
    }
    const renderer_rect_t *regions = page == UI_PAGE_PC ? PC_DYNAMIC : CODEX_DYNAMIC;
    size_t count = page == UI_PAGE_PC ? sizeof(PC_DYNAMIC) / sizeof(PC_DYNAMIC[0]) :
                                        sizeof(CODEX_DYNAMIC) / sizeof(CODEX_DYNAMIC[0]);
    const uint16_t *background = page_background(page);
    for (size_t i = 0; i < count; ++i) {
        restore_rect(surface, background, regions[i]);
        dirty->rects[i] = regions[i];
    }
    draw_values(surface, page, state);
    dirty->count = count;
}
