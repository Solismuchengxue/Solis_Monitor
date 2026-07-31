#include "unity.h"
#include "button_debounce.h"

TEST_CASE("button emits short press after the double press window", "[button]")
{
    button_debounce_t b;
    button_debounce_init(&b, false, 0);
    TEST_ASSERT_EQUAL(BUTTON_EVENT_NONE, button_debounce_update(&b, true, 10));
    TEST_ASSERT_EQUAL(BUTTON_EVENT_NONE, button_debounce_update(&b, false, 15));
    TEST_ASSERT_EQUAL(BUTTON_EVENT_NONE, button_debounce_update(&b, true, 20));
    TEST_ASSERT_EQUAL(BUTTON_EVENT_NONE, button_debounce_update(&b, true, 50));
    TEST_ASSERT_EQUAL(BUTTON_EVENT_NONE, button_debounce_update(&b, false, 100));
    TEST_ASSERT_EQUAL(BUTTON_EVENT_NONE, button_debounce_update(&b, false, 130));
    TEST_ASSERT_EQUAL(BUTTON_EVENT_NONE, button_debounce_update(&b, false, 629));
    TEST_ASSERT_EQUAL(BUTTON_EVENT_SHORT_PRESS, button_debounce_update(&b, false, 630));
}

TEST_CASE("button emits double press on the second debounced press", "[button]")
{
    button_debounce_t b;
    button_debounce_init(&b, false, 0);
    TEST_ASSERT_EQUAL(BUTTON_EVENT_NONE, button_debounce_update(&b, true, 10));
    TEST_ASSERT_EQUAL(BUTTON_EVENT_NONE, button_debounce_update(&b, true, 40));
    TEST_ASSERT_EQUAL(BUTTON_EVENT_NONE, button_debounce_update(&b, false, 100));
    TEST_ASSERT_EQUAL(BUTTON_EVENT_NONE, button_debounce_update(&b, false, 130));
    TEST_ASSERT_EQUAL(BUTTON_EVENT_NONE, button_debounce_update(&b, true, 250));
    TEST_ASSERT_EQUAL(BUTTON_EVENT_DOUBLE_PRESS, button_debounce_update(&b, true, 280));
    TEST_ASSERT_EQUAL(BUTTON_EVENT_NONE, button_debounce_update(&b, true, 5500));
    TEST_ASSERT_EQUAL(BUTTON_EVENT_NONE, button_debounce_update(&b, false, 5510));
    TEST_ASSERT_EQUAL(BUTTON_EVENT_NONE, button_debounce_update(&b, false, 5540));
}

TEST_CASE("button emits one long press without a following short press", "[button]")
{
    button_debounce_t b;
    button_debounce_init(&b, false, 0);
    TEST_ASSERT_EQUAL(BUTTON_EVENT_NONE, button_debounce_update(&b, true, 10));
    TEST_ASSERT_EQUAL(BUTTON_EVENT_NONE, button_debounce_update(&b, true, 40));
    TEST_ASSERT_EQUAL(BUTTON_EVENT_NONE, button_debounce_update(&b, true, 5039));
    TEST_ASSERT_EQUAL(BUTTON_EVENT_LONG_PRESS, button_debounce_update(&b, true, 5040));
    TEST_ASSERT_EQUAL(BUTTON_EVENT_NONE, button_debounce_update(&b, true, 6000));
    TEST_ASSERT_EQUAL(BUTTON_EVENT_NONE, button_debounce_update(&b, false, 6010));
    TEST_ASSERT_EQUAL(BUTTON_EVENT_NONE, button_debounce_update(&b, false, 6040));
}
