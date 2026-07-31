#pragma once

void environment_calibrate_dht11(float raw_temperature_c, float raw_humidity,
                                 float *temperature_c, float *humidity);
