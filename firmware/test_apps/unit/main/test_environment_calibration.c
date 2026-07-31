#include "environment_calibration.h"
#include "unity.h"

TEST_CASE("DHT11 calibration matches the measured reference point", "[environment]")
{
    float temperature;
    float humidity;

    environment_calibrate_dht11(29.0f, 61.0f, &temperature, &humidity);

    TEST_ASSERT_FLOAT_WITHIN(0.05f, 26.7f, temperature);
    TEST_ASSERT_FLOAT_WITHIN(0.1f, 79.0f, humidity);
}

TEST_CASE("DHT11 calibrated humidity remains in physical range", "[environment]")
{
    float temperature;
    float humidity;

    environment_calibrate_dht11(35.0f, 100.0f, &temperature, &humidity);

    TEST_ASSERT_TRUE(humidity >= 0.0f);
    TEST_ASSERT_TRUE(humidity <= 100.0f);
}
