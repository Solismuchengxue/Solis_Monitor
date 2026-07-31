#pragma once

#include <stddef.h>

#include "dashboard_state.h"
#include "renderer.h"

typedef enum { UI_PAGE_PC = 0, UI_PAGE_CODEX = 1 } ui_page_t;

#define UI_MAX_DIRTY_RECTS 8

typedef struct {
    renderer_rect_t rects[UI_MAX_DIRTY_RECTS];
    size_t count;
} ui_dirty_list_t;

void ui_render_full(renderer_surface_t *surface, ui_page_t page,
                    const dashboard_state_t *state, ui_dirty_list_t *dirty);
void ui_render_update(renderer_surface_t *surface, ui_page_t page,
                      const dashboard_state_t *state, ui_dirty_list_t *dirty);
void ui_render_night_settings_confirmation(
    renderer_surface_t *surface, bool enabled, unsigned start_minute,
    unsigned end_minute, ui_dirty_list_t *dirty);
