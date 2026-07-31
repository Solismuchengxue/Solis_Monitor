#pragma once

#include <stdbool.h>
#include <stdint.h>

#include "esp_err.h"
#include "renderer.h"

typedef struct display_t display_t;

esp_err_t display_init(display_t **out_display);
esp_err_t display_flush_rect(display_t *display, const uint16_t *framebuffer,
                             uint16_t stride, renderer_rect_t rect);
esp_err_t display_set_enabled(display_t *display, bool enabled);
void display_deinit(display_t *display);
