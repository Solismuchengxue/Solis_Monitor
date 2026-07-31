#include "button_debounce.h"

#define DEBOUNCE_MS 30U
#define DOUBLE_PRESS_MS 500U
#define LONG_PRESS_MS 5000U

void button_debounce_init(button_debounce_t *button, bool pressed, uint32_t now_ms)
{
    *button = (button_debounce_t){
        .stable_pressed = pressed,
        .sample_pressed = pressed,
        .sample_since_ms = now_ms,
        .press_since_ms = now_ms,
    };
}

button_event_t button_debounce_update(button_debounce_t *button, bool pressed, uint32_t now_ms)
{
    if (pressed != button->sample_pressed) {
        button->sample_pressed = pressed;
        button->sample_since_ms = now_ms;
        return BUTTON_EVENT_NONE;
    }

    if (pressed != button->stable_pressed &&
        (uint32_t)(now_ms - button->sample_since_ms) >= DEBOUNCE_MS) {
        button->stable_pressed = pressed;
        if (pressed) {
            button->press_since_ms = now_ms;
            button->long_press_emitted = false;
            if (button->short_press_pending) {
                uint32_t elapsed =
                    (uint32_t)(now_ms - button->short_press_since_ms);
                button->short_press_pending = false;
                if (elapsed <= DOUBLE_PRESS_MS) {
                    button->long_press_emitted = true;
                    return BUTTON_EVENT_DOUBLE_PRESS;
                }
                return BUTTON_EVENT_SHORT_PRESS;
            }
            return BUTTON_EVENT_NONE;
        }
        if (button->long_press_emitted) return BUTTON_EVENT_NONE;
        button->short_press_pending = true;
        button->short_press_since_ms = now_ms;
        return BUTTON_EVENT_NONE;
    }

    if (button->stable_pressed && !button->long_press_emitted &&
        (uint32_t)(now_ms - button->press_since_ms) >= LONG_PRESS_MS) {
        button->long_press_emitted = true;
        return BUTTON_EVENT_LONG_PRESS;
    }

    if (!button->stable_pressed && !button->sample_pressed &&
        button->short_press_pending &&
        (uint32_t)(now_ms - button->short_press_since_ms) >= DOUBLE_PRESS_MS) {
        button->short_press_pending = false;
        return BUTTON_EVENT_SHORT_PRESS;
    }

    return BUTTON_EVENT_NONE;
}
