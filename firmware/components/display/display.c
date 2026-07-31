#include "display.h"

#include <stdlib.h>
#include <string.h>

#include "board_config.h"
#include "esp_heap_caps.h"
#include "esp_lcd_io_i80.h"
#include "esp_lcd_nt35510.h"
#include "esp_lcd_panel_io.h"
#include "esp_lcd_panel_ops.h"
#include "esp_log.h"
#include "freertos/FreeRTOS.h"
#include "freertos/semphr.h"

#define DISPLAY_WIDTH 800
#define DISPLAY_HEIGHT 480
#define DISPLAY_DMA_ROWS 32
#define DISPLAY_DMA_BYTES (DISPLAY_WIDTH * DISPLAY_DMA_ROWS * sizeof(uint16_t))

static const char *TAG = "display";

struct display_t {
    esp_lcd_i80_bus_handle_t bus;
    esp_lcd_panel_io_handle_t io;
    esp_lcd_panel_handle_t panel;
    SemaphoreHandle_t transfer_done;
    uint16_t *dma_buffer;
    size_t dma_pixels;
};

static bool display_transfer_done(esp_lcd_panel_io_handle_t io,
                                  esp_lcd_panel_io_event_data_t *event, void *ctx)
{
    (void)io;
    (void)event;
    BaseType_t high_task_woken = pdFALSE;
    xSemaphoreGiveFromISR(((display_t *)ctx)->transfer_done, &high_task_woken);
    return high_task_woken == pdTRUE;
}

static void display_log_error(const char *stage, esp_err_t err)
{
    ESP_LOGE(TAG, "%s: %s", stage, esp_err_to_name(err));
}

void display_deinit(display_t *display)
{
    if (!display) return;

    if (display->panel) {
        esp_err_t err = esp_lcd_panel_disp_on_off(display->panel, false);
        if (err != ESP_OK) display_log_error("disable panel", err);
        err = esp_lcd_panel_del(display->panel);
        if (err != ESP_OK) display_log_error("delete panel", err);
    }
    if (display->dma_buffer) heap_caps_free(display->dma_buffer);
    if (display->io) {
        esp_err_t err = esp_lcd_panel_io_del(display->io);
        if (err != ESP_OK) display_log_error("delete panel IO", err);
    }
    if (display->bus) {
        esp_err_t err = esp_lcd_del_i80_bus(display->bus);
        if (err != ESP_OK) display_log_error("delete I80 bus", err);
    }
    if (display->transfer_done) vSemaphoreDelete(display->transfer_done);
    free(display);
}

esp_err_t display_init(display_t **out_display)
{
    if (!out_display) return ESP_ERR_INVALID_ARG;
    *out_display = NULL;

    esp_err_t err = ESP_OK;
    display_t *display = calloc(1, sizeof(*display));
    if (!display) {
        err = ESP_ERR_NO_MEM;
        display_log_error("allocate display state", err);
        return err;
    }

    display->transfer_done = xSemaphoreCreateBinary();
    if (!display->transfer_done) {
        err = ESP_ERR_NO_MEM;
        display_log_error("create transfer semaphore", err);
        goto fail;
    }

    const esp_lcd_i80_bus_config_t bus_cfg = {
        .clk_src = LCD_CLK_SRC_DEFAULT,
        .dc_gpio_num = BOARD_LCD_DC,
        .wr_gpio_num = BOARD_LCD_WR,
        .data_gpio_nums = {0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15},
        .bus_width = 16,
        .max_transfer_bytes = DISPLAY_DMA_BYTES,
        .dma_burst_size = 64,
    };
    err = esp_lcd_new_i80_bus(&bus_cfg, &display->bus);
    if (err != ESP_OK) {
        display_log_error("create I80 bus", err);
        goto fail;
    }

    const esp_lcd_panel_io_i80_config_t io_cfg = {
        .cs_gpio_num = BOARD_LCD_CS,
        .pclk_hz = BOARD_LCD_PCLK_HZ,
        .trans_queue_depth = 1,
        .on_color_trans_done = display_transfer_done,
        .user_ctx = display,
        .dc_levels = {.dc_idle_level = 0, .dc_cmd_level = 0,
                      .dc_dummy_level = 0, .dc_data_level = 1},
        .lcd_cmd_bits = 16,
        .lcd_param_bits = 16,
        .flags = {.swap_color_bytes = BOARD_LCD_SWAP_COLOR_BYTES},
    };
    err = esp_lcd_new_panel_io_i80(display->bus, &io_cfg, &display->io);
    if (err != ESP_OK) {
        display_log_error("create I80 panel IO", err);
        goto fail;
    }

    display->dma_buffer = esp_lcd_i80_alloc_draw_buffer(
        display->io, DISPLAY_DMA_BYTES, MALLOC_CAP_INTERNAL | MALLOC_CAP_DMA);
    if (!display->dma_buffer) {
        err = ESP_ERR_NO_MEM;
        display_log_error("allocate I80 DMA buffer", err);
        goto fail;
    }
    display->dma_pixels = DISPLAY_DMA_BYTES / sizeof(*display->dma_buffer);

    const esp_lcd_panel_dev_config_t panel_cfg = {
        .reset_gpio_num = BOARD_LCD_RST,
        .rgb_ele_order = BOARD_LCD_BGR ? LCD_RGB_ELEMENT_ORDER_BGR : LCD_RGB_ELEMENT_ORDER_RGB,
        .bits_per_pixel = 16,
    };
    err = esp_lcd_new_panel_nt35510(display->io, &panel_cfg, &display->panel);
    if (err != ESP_OK) {
        display_log_error("create NT35510 panel", err);
        goto fail;
    }
    err = esp_lcd_panel_reset(display->panel);
    if (err != ESP_OK) {
        display_log_error("reset NT35510 panel", err);
        goto fail;
    }
    err = esp_lcd_panel_init(display->panel);
    if (err != ESP_OK) {
        display_log_error("initialize NT35510 panel", err);
        goto fail;
    }
    err = esp_lcd_panel_swap_xy(display->panel, BOARD_LCD_SWAP_XY);
    if (err != ESP_OK) {
        display_log_error("set NT35510 axis swap", err);
        goto fail;
    }
    err = esp_lcd_panel_mirror(display->panel, BOARD_LCD_MIRROR_X, BOARD_LCD_MIRROR_Y);
    if (err != ESP_OK) {
        display_log_error("set NT35510 mirror", err);
        goto fail;
    }
    err = esp_lcd_panel_disp_on_off(display->panel, false);
    if (err != ESP_OK) {
        display_log_error("disable NT35510 panel", err);
        goto fail;
    }

    ESP_LOGI(TAG, "I80 %u MHz, 16-bit data bus", BOARD_LCD_PCLK_HZ / 1000000U);
    *out_display = display;
    return ESP_OK;

fail:
    display_deinit(display);
    return err;
}

esp_err_t display_flush_rect(display_t *display, const uint16_t *framebuffer,
                             uint16_t stride, renderer_rect_t rect)
{
    if (!display || !framebuffer || stride < DISPLAY_WIDTH) return ESP_ERR_INVALID_ARG;

    rect = renderer_clip(rect, DISPLAY_WIDTH, DISPLAY_HEIGHT);
    if (rect.w == 0 || rect.h == 0) return ESP_OK;

    renderer_rect_t segments[2] = {rect};
    size_t segment_count = 1;
    // This panel needs a separate edge-column overwrite after a window ending at x=799.
    if (rect.x + rect.w == DISPLAY_WIDTH && rect.w > 1) {
        segments[1] = (renderer_rect_t){DISPLAY_WIDTH - 1, rect.y, 1, rect.h};
        segment_count = 2;
    }

    for (size_t segment = 0; segment < segment_count; ++segment) {
        renderer_rect_t part = segments[segment];
        size_t rows_per_chunk = display->dma_pixels / (size_t)part.w;
        if (rows_per_chunk == 0) return ESP_ERR_INVALID_SIZE;

        for (int row = 0; row < part.h; row += (int)rows_per_chunk) {
            int rows = part.h - row;
            if (rows > (int)rows_per_chunk) rows = (int)rows_per_chunk;
            for (int y = 0; y < rows; ++y) {
                const uint16_t *src = framebuffer +
                    (part.y + row + y) * stride + part.x;
                memcpy(display->dma_buffer + y * part.w, src, part.w * sizeof(uint16_t));
            }
            esp_err_t err = esp_lcd_panel_draw_bitmap(
                display->panel, part.x, part.y + row,
                part.x + part.w, part.y + row + rows, display->dma_buffer);
            if (err != ESP_OK) {
                display_log_error("queue rectangle flush", err);
                return err;
            }
            if (xSemaphoreTake(display->transfer_done, pdMS_TO_TICKS(1000)) != pdTRUE) {
                display_log_error("wait for rectangle flush", ESP_ERR_TIMEOUT);
                return ESP_ERR_TIMEOUT;
            }
        }
    }
    return ESP_OK;
}

esp_err_t display_set_enabled(display_t *display, bool enabled)
{
    if (!display) return ESP_ERR_INVALID_ARG;
    esp_err_t err = esp_lcd_panel_disp_on_off(display->panel, enabled);
    if (err != ESP_OK) display_log_error(enabled ? "enable panel" : "disable panel", err);
    return err;
}
