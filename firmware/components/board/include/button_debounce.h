#pragma once

#include <stdbool.h>
#include <stdint.h>

typedef struct {
    bool stable_pressed;
    bool sample_pressed;
    bool long_press_emitted;
    bool short_press_pending;
    uint32_t sample_since_ms;
    uint32_t press_since_ms;
    uint32_t short_press_since_ms;
} button_debounce_t;

typedef enum {
    BUTTON_EVENT_NONE,
    BUTTON_EVENT_SHORT_PRESS,
    BUTTON_EVENT_DOUBLE_PRESS,
    BUTTON_EVENT_LONG_PRESS,
} button_event_t;

void button_debounce_init(button_debounce_t *button, bool pressed, uint32_t now_ms);
button_event_t button_debounce_update(button_debounce_t *button, bool pressed, uint32_t now_ms);
