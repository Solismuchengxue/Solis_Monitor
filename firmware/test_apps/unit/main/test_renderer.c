#include "unity.h"
#include "renderer.h"
#include "ui_assets.h"

TEST_CASE("renderer clips negative and oversized rectangles", "[renderer]")
{
    uint16_t pixels[16] = {0};
    renderer_surface_t s = {.pixels = pixels, .width = 4, .height = 4, .stride = 4};
    renderer_fill_rect(&s, (renderer_rect_t){-2, 1, 8, 2}, 0x1234);
    for (int y = 0; y < 4; ++y) {
        for (int x = 0; x < 4; ++x) {
            TEST_ASSERT_EQUAL_HEX16((y == 1 || y == 2) ? 0x1234 : 0, pixels[y * 4 + x]);
        }
    }
}

TEST_CASE("progress clamps percentage", "[renderer]")
{
    uint16_t pixels[10] = {0};
    renderer_surface_t s = {.pixels = pixels, .width = 10, .height = 1, .stride = 10};
    renderer_draw_progress(&s, (renderer_rect_t){0, 0, 10, 1}, 150, 1, 2);
    for (int i = 0; i < 10; ++i) TEST_ASSERT_EQUAL_HEX16(2, pixels[i]);
}

TEST_CASE("rgb565 packs primary colors", "[renderer]")
{
    TEST_ASSERT_EQUAL_HEX16(0xF800, renderer_rgb565(255, 0, 0));
    TEST_ASSERT_EQUAL_HEX16(0x07E0, renderer_rgb565(0, 255, 0));
    TEST_ASSERT_EQUAL_HEX16(0x001F, renderer_rgb565(0, 0, 255));
}

TEST_CASE("text blends an alpha glyph and advances", "[renderer]")
{
    uint16_t pixels[4] = {0};
    const uint8_t alpha[2] = {255, 0};
    const bitmap_glyph_t glyph = {.codepoint = 'A', .offset = 0, .width = 2, .height = 1,
                                  .x_offset = 0, .y_offset = -1, .advance = 3};
    const bitmap_font_t font = {.bitmap = alpha, .glyphs = &glyph, .glyph_count = 1, .pixel_size = 1};
    renderer_surface_t s = {.pixels = pixels, .width = 4, .height = 1, .stride = 4};
    TEST_ASSERT_EQUAL(3, renderer_draw_text(&s, 0, 1, "A", &font, 0xFFFF));
    TEST_ASSERT_EQUAL_HEX16(0xFFFF, pixels[0]);
    TEST_ASSERT_EQUAL_HEX16(0x0000, pixels[1]);
}

TEST_CASE("generated fonts are exposed by the asset component", "[ui_assets]")
{
    const bitmap_font_t *font20 = ui_assets_font(20);
    const bitmap_font_t *font24 = ui_assets_font(24);
    const bitmap_font_t *font56 = ui_assets_font(56);
    TEST_ASSERT_NOT_NULL(font20);
    TEST_ASSERT_NOT_NULL(font24);
    TEST_ASSERT_NOT_NULL(font56);
    TEST_ASSERT_EQUAL(20, font20->pixel_size);
    TEST_ASSERT_EQUAL(24, font24->pixel_size);
    TEST_ASSERT_EQUAL(56, font56->pixel_size);
    TEST_ASSERT_GREATER_THAN(0, font20->glyph_count);
    TEST_ASSERT_NULL(ui_assets_font(25));
}
