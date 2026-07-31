#include "environment_calibration.h"

#include <math.h>

#define DHT11_RAW_REFERENCE_TEMP_C 29.0f
#define DHT11_RAW_REFERENCE_HUMIDITY 61.0f
#define DHT11_ACTUAL_REFERENCE_TEMP_C 26.7f
#define DHT11_ACTUAL_REFERENCE_HUMIDITY 79.0f

static float saturation_vapor_pressure_ratio(float temperature_c)
{
    return expf((17.62f * temperature_c) / (243.12f + temperature_c));
}

void environment_calibrate_dht11(float raw_temperature_c, float raw_humidity,
                                 float *temperature_c, float *humidity)
{
    if (!temperature_c || !humidity) return;

    float corrected_temperature = raw_temperature_c +
        (DHT11_ACTUAL_REFERENCE_TEMP_C - DHT11_RAW_REFERENCE_TEMP_C);
    float vapor_gain =
        (DHT11_ACTUAL_REFERENCE_HUMIDITY *
         saturation_vapor_pressure_ratio(DHT11_ACTUAL_REFERENCE_TEMP_C)) /
        (DHT11_RAW_REFERENCE_HUMIDITY *
         saturation_vapor_pressure_ratio(DHT11_RAW_REFERENCE_TEMP_C));
    float corrected_humidity = raw_humidity *
        saturation_vapor_pressure_ratio(raw_temperature_c) * vapor_gain /
        saturation_vapor_pressure_ratio(corrected_temperature);

    *temperature_c = corrected_temperature;
    *humidity = fminf(100.0f, fmaxf(0.0f, corrected_humidity));
}
