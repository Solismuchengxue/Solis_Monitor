#include "renderer.h"

#include <limits.h>

uint16_t renderer_rgb565(uint8_t r, uint8_t g, uint8_t b)
{
    return (uint16_t)(((r & 0xF8U) << 8) | ((g & 0xFCU) << 3) | (b >> 3));
}

renderer_rect_t renderer_clip(renderer_rect_t r, uint16_t width, uint16_t height)
{
    int x0 = r.x < 0 ? 0 : r.x;
    int y0 = r.y < 0 ? 0 : r.y;
    int x1 = r.x + r.w > width ? width : r.x + r.w;
    int y1 = r.y + r.h > height ? height : r.y + r.h;
    if (x1 <= x0 || y1 <= y0) return (renderer_rect_t){0, 0, 0, 0};
    return (renderer_rect_t){x0, y0, x1 - x0, y1 - y0};
}

void renderer_fill_rect(renderer_surface_t *s, renderer_rect_t rect, uint16_t color)
{
    if (!s || !s->pixels) return;
    rect = renderer_clip(rect, s->width, s->height);
    for (int y = rect.y; y < rect.y + rect.h; ++y) {
        uint16_t *row = s->pixels + y * s->stride + rect.x;
        for (int x = 0; x < rect.w; ++x) row[x] = color;
    }
}

void renderer_draw_progress(renderer_surface_t *s, renderer_rect_t rect,
                            unsigned percent, uint16_t track, uint16_t fill)
{
    if (percent > 100) percent = 100;
    renderer_fill_rect(s, rect, track);
    renderer_rect_t value = rect;
    value.w = (int)((unsigned)rect.w * percent / 100U);
    renderer_fill_rect(s, value, fill);
}

static uint32_t decode_utf8(const unsigned char **cursor)
{
    const unsigned char *s = *cursor;
    uint32_t cp;
    if (s[0] < 0x80) { *cursor = s + 1; return s[0]; }
    if (s[0] >= 0xC2 && s[0] <= 0xDF && s[1] != 0 && (s[1] & 0xC0) == 0x80) {
        cp = ((uint32_t)(s[0] & 0x1F) << 6) | (s[1] & 0x3F);
        *cursor = s + 2;
        return cp;
    }
    if (s[0] >= 0xE0 && s[0] <= 0xEF && s[1] != 0 && s[2] != 0 &&
        (s[1] & 0xC0) == 0x80 && (s[2] & 0xC0) == 0x80) {
        cp = ((uint32_t)(s[0] & 0x0F) << 12) |
             ((uint32_t)(s[1] & 0x3F) << 6) | (s[2] & 0x3F);
        if (cp >= 0x800 && !(cp >= 0xD800 && cp <= 0xDFFF)) {
            *cursor = s + 3;
            return cp;
        }
    }
    if (s[0] >= 0xF0 && s[0] <= 0xF4 && s[1] != 0 && s[2] != 0 && s[3] != 0 &&
        (s[1] & 0xC0) == 0x80 && (s[2] & 0xC0) == 0x80 && (s[3] & 0xC0) == 0x80) {
        cp = ((uint32_t)(s[0] & 0x07) << 18) | ((uint32_t)(s[1] & 0x3F) << 12) |
             ((uint32_t)(s[2] & 0x3F) << 6) | (s[3] & 0x3F);
        if (cp >= 0x10000 && cp <= 0x10FFFF) {
            *cursor = s + 4;
            return cp;
        }
    }
    *cursor = s + 1;
    return UINT32_MAX;
}

static const bitmap_glyph_t *find_glyph(const bitmap_font_t *font, uint32_t cp)
{
    size_t lo = 0, hi = font->glyph_count;
    while (lo < hi) {
        size_t mid = lo + (hi - lo) / 2;
        if (font->glyphs[mid].codepoint < cp) lo = mid + 1;
        else hi = mid;
    }
    return lo < font->glyph_count && font->glyphs[lo].codepoint == cp ? &font->glyphs[lo] : NULL;
}

static uint16_t blend565(uint16_t dst, uint16_t src, uint8_t alpha)
{
    unsigned sr = (src >> 11) & 31U, sg = (src >> 5) & 63U, sb = src & 31U;
    unsigned dr = (dst >> 11) & 31U, dg = (dst >> 5) & 63U, db = dst & 31U;
    unsigned inv = 255U - alpha;
    unsigned r = (sr * alpha + dr * inv + 127U) / 255U;
    unsigned g = (sg * alpha + dg * inv + 127U) / 255U;
    unsigned b = (sb * alpha + db * inv + 127U) / 255U;
    return (uint16_t)((r << 11) | (g << 5) | b);
}

void renderer_draw_rgba565(renderer_surface_t *s, int x, int y,
                           uint16_t width, uint16_t height, const uint8_t *pixels)
{
    if (!s || !s->pixels || !pixels) return;
    for (uint16_t iy = 0; iy < height; ++iy) {
        int dy = y + iy;
        if (dy < 0 || dy >= s->height) continue;
        for (uint16_t ix = 0; ix < width; ++ix) {
            int dx = x + ix;
            if (dx < 0 || dx >= s->width) continue;
            size_t offset = ((size_t)iy * width + ix) * 3U;
            uint16_t color = (uint16_t)(pixels[offset] | ((uint16_t)pixels[offset + 1] << 8));
            uint8_t alpha = pixels[offset + 2];
            if (alpha == 0) continue;
            uint16_t *destination = &s->pixels[dy * s->stride + dx];
            *destination = blend565(*destination, color, alpha);
        }
    }
}

int renderer_draw_text(renderer_surface_t *s, int x, int baseline,
                       const char *utf8, const bitmap_font_t *font, uint16_t color)
{
    if (!s || !s->pixels || !utf8 || !font) return x;
    const unsigned char *cursor = (const unsigned char *)utf8;
    int pen = x;
    while (*cursor) {
        uint32_t cp = decode_utf8(&cursor);
        if (cp == UINT32_MAX) continue;
        const bitmap_glyph_t *g = find_glyph(font, cp);
        if (!g) { pen += font->pixel_size / 2; continue; }
        for (int gy = 0; gy < g->height; ++gy) {
            int dy = baseline + g->y_offset + gy;
            if (dy < 0 || dy >= s->height) continue;
            for (int gx = 0; gx < g->width; ++gx) {
                int dx = pen + g->x_offset + gx;
                if (dx < 0 || dx >= s->width) continue;
                uint8_t a = font->bitmap[g->offset + gy * g->width + gx];
                uint16_t *dst = &s->pixels[dy * s->stride + dx];
                *dst = blend565(*dst, color, a);
            }
        }
        pen += g->advance;
    }
    return pen;
}
