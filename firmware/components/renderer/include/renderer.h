#pragma once

#include <stddef.h>
#include <stdint.h>

typedef struct { int16_t x, y, w, h; } renderer_rect_t;
typedef struct { uint16_t *pixels; uint16_t width, height, stride; } renderer_surface_t;
typedef struct {
    uint32_t codepoint;
    uint32_t offset;
    uint16_t width, height;
    int16_t x_offset, y_offset, advance;
} bitmap_glyph_t;
typedef struct {
    const uint8_t *bitmap;
    const bitmap_glyph_t *glyphs;
    size_t glyph_count;
    uint16_t pixel_size;
} bitmap_font_t;

uint16_t renderer_rgb565(uint8_t r, uint8_t g, uint8_t b);
renderer_rect_t renderer_clip(renderer_rect_t rect, uint16_t width, uint16_t height);
void renderer_fill_rect(renderer_surface_t *surface, renderer_rect_t rect, uint16_t color);
void renderer_draw_progress(renderer_surface_t *surface, renderer_rect_t rect,
                            unsigned percent, uint16_t track, uint16_t fill);
void renderer_draw_rgba565(renderer_surface_t *surface, int x, int y,
                           uint16_t width, uint16_t height, const uint8_t *pixels);
int renderer_draw_text(renderer_surface_t *surface, int x, int baseline,
                       const char *utf8, const bitmap_font_t *font, uint16_t color);
