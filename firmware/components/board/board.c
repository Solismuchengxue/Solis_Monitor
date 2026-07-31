#include "board.h"

#include "board_config.h"
#include "button_debounce.h"
#include "driver/gpio.h"
#include "driver/ledc.h"
#include "esp_check.h"

#define BACKLIGHT_PWM_FREQUENCY_HZ 20000
#define BACKLIGHT_PWM_MAX_DUTY 1023U

static const char *TAG = "board";
static button_debounce_t button;

esp_err_t board_init(void)
{
    ESP_RETURN_ON_ERROR(gpio_set_level(BOARD_BACKLIGHT, 0), TAG, "backlight low");
    ESP_RETURN_ON_ERROR(gpio_set_direction(BOARD_BACKLIGHT, GPIO_MODE_OUTPUT),
                        TAG, "backlight output");
    const ledc_timer_config_t backlight_timer = {
        .speed_mode = LEDC_LOW_SPEED_MODE,
        .duty_resolution = LEDC_TIMER_10_BIT,
        .timer_num = LEDC_TIMER_0,
        .freq_hz = BACKLIGHT_PWM_FREQUENCY_HZ,
        .clk_cfg = LEDC_AUTO_CLK,
    };
    ESP_RETURN_ON_ERROR(ledc_timer_config(&backlight_timer), TAG,
                        "backlight PWM timer");
    const ledc_channel_config_t backlight_channel = {
        .gpio_num = BOARD_BACKLIGHT,
        .speed_mode = LEDC_LOW_SPEED_MODE,
        .channel = LEDC_CHANNEL_0,
        .intr_type = LEDC_INTR_DISABLE,
        .timer_sel = LEDC_TIMER_0,
        .duty = 0,
        .hpoint = 0,
    };
    ESP_RETURN_ON_ERROR(ledc_channel_config(&backlight_channel), TAG,
                        "backlight PWM channel");
    ESP_RETURN_ON_ERROR(gpio_set_level(BOARD_LCD_RD, 1), TAG, "RD high");
    ESP_RETURN_ON_ERROR(gpio_set_direction(BOARD_LCD_RD, GPIO_MODE_OUTPUT),
                        TAG, "RD output");

    const gpio_config_t button_cfg = {
        .pin_bit_mask = 1ULL << BOARD_BUTTON,
        .mode = GPIO_MODE_INPUT,
        .pull_up_en = GPIO_PULLUP_ENABLE,
        .pull_down_en = GPIO_PULLDOWN_DISABLE,
        .intr_type = GPIO_INTR_DISABLE,
    };
    ESP_RETURN_ON_ERROR(gpio_config(&button_cfg), TAG, "button input");
    button_debounce_init(&button, gpio_get_level(BOARD_BUTTON) == 0, 0);
    return ESP_OK;
}

void board_backlight_set(bool on)
{
    (void)board_backlight_set_percent(on ? 100U : 0U);
}

esp_err_t board_backlight_set_percent(uint8_t percent)
{
    if (percent > 100U) return ESP_ERR_INVALID_ARG;
    uint32_t duty =
        (BACKLIGHT_PWM_MAX_DUTY * (uint32_t)percent + 50U) / 100U;
    esp_err_t result = ledc_set_duty(
        LEDC_LOW_SPEED_MODE, LEDC_CHANNEL_0, duty);
    if (result != ESP_OK) return result;
    return ledc_update_duty(LEDC_LOW_SPEED_MODE, LEDC_CHANNEL_0);
}

button_event_t board_button_event(uint32_t now_ms)
{
    return button_debounce_update(&button, gpio_get_level(BOARD_BUTTON) == 0, now_ms);
}
