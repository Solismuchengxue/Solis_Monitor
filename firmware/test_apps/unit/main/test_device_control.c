#include "device_control.h"

#include "unity.h"

TEST_CASE("device control defaults keep the backlight fully on", "[device_control]")
{
    device_control_settings_t settings;
    device_control_settings_default(&settings);

    TEST_ASSERT_EQUAL_UINT8(100, settings.brightness_percent);
    TEST_ASSERT_FALSE(settings.night_enabled);
    TEST_ASSERT_EQUAL_UINT16(23 * 60 + 30, settings.night_start_minute);
    TEST_ASSERT_EQUAL_UINT16(7 * 60 + 30, settings.night_end_minute);
}

TEST_CASE("overnight schedule crosses midnight", "[device_control]")
{
    TEST_ASSERT_TRUE(device_control_night_active(23 * 60 + 30, 23 * 60 + 30,
                                                  7 * 60 + 30));
    TEST_ASSERT_TRUE(device_control_night_active(6 * 60, 23 * 60 + 30,
                                                  7 * 60 + 30));
    TEST_ASSERT_FALSE(device_control_night_active(12 * 60, 23 * 60 + 30,
                                                   7 * 60 + 30));
    TEST_ASSERT_FALSE(device_control_night_active(7 * 60 + 30, 23 * 60 + 30,
                                                   7 * 60 + 30));
}

TEST_CASE("daytime schedule uses a simple bounded interval", "[device_control]")
{
    TEST_ASSERT_FALSE(device_control_night_active(8 * 60 + 59, 9 * 60,
                                                   18 * 60));
    TEST_ASSERT_TRUE(device_control_night_active(9 * 60, 9 * 60,
                                                  18 * 60));
    TEST_ASSERT_FALSE(device_control_night_active(18 * 60, 9 * 60,
                                                   18 * 60));
}

TEST_CASE("interaction mode keeps the screen visible", "[device_control]")
{
    TEST_ASSERT_FALSE(device_control_should_sleep(true, true, true));
    TEST_ASSERT_TRUE(device_control_should_sleep(false, true, false));
    TEST_ASSERT_TRUE(device_control_should_sleep(false, false, true));
    TEST_ASSERT_FALSE(device_control_should_sleep(false, false, false));
}

TEST_CASE("wake deadline remains valid across tick wrap", "[device_control]")
{
    TEST_ASSERT_TRUE(device_control_wake_active(0xfffffff0U, 0x00000020U));
    TEST_ASSERT_TRUE(device_control_wake_active(0x00000010U, 0x00000020U));
    TEST_ASSERT_FALSE(device_control_wake_active(0x00000020U, 0x00000020U));
}
