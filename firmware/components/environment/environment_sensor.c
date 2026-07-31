#include "environment_sensor.h"

#include <math.h>
#include <stdbool.h>

#include "board_config.h"
#include "dht.h"
#include "environment_calibration.h"
#include "esp_log.h"
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"

#define DHT11_STARTUP_DELAY_MS 2000
#define DHT11_SAMPLE_INTERVAL_MS 2000
#define DHT11_TASK_STACK_SIZE 3072
#define DHT11_TASK_PRIORITY 4

static const char *TAG = "environment";

static bool reading_is_valid(float temperature, float humidity)
{
    return isfinite(temperature) && isfinite(humidity) &&
           temperature >= -40.0f && temperature <= 80.0f &&
           humidity >= 0.0f && humidity <= 100.0f;
}

static void dht11_task(void *context)
{
    dashboard_store_t *store = context;
    bool available = false;
    bool failure_logged = false;

    vTaskDelay(pdMS_TO_TICKS(DHT11_STARTUP_DELAY_MS));
    for (;;) {
        float humidity = NAN;
        float temperature = NAN;
        esp_err_t err = dht_read_float_data(DHT_TYPE_DHT11, (gpio_num_t)BOARD_DHT11,
                                            &humidity, &temperature);

        if (err == ESP_OK && reading_is_valid(temperature, humidity)) {
            environment_calibrate_dht11(temperature, humidity, &temperature, &humidity);
            dashboard_store_set_local_environment(store, temperature, humidity);
            if (!available) {
                ESP_LOGI(TAG, "DHT11 available: temperature=%.1f C humidity=%.1f%%",
                         temperature, humidity);
            }
            available = true;
            failure_logged = false;
        } else {
            dashboard_store_set_local_environment(store, NAN, NAN);
            if (!failure_logged) {
                if (err == ESP_OK) {
                    ESP_LOGW(TAG, "DHT11 returned an invalid reading");
                } else {
                    ESP_LOGW(TAG, "DHT11 read failed: %s", esp_err_to_name(err));
                }
            }
            available = false;
            failure_logged = true;
        }

        vTaskDelay(pdMS_TO_TICKS(DHT11_SAMPLE_INTERVAL_MS));
    }
}

esp_err_t environment_sensor_start(dashboard_store_t *dashboard_store)
{
    if (!dashboard_store) return ESP_ERR_INVALID_ARG;

    if (!dashboard_store_set_local_environment(dashboard_store, NAN, NAN)) {
        return ESP_FAIL;
    }
    BaseType_t result = xTaskCreate(dht11_task, "dht11", DHT11_TASK_STACK_SIZE,
                                    dashboard_store, DHT11_TASK_PRIORITY, NULL);
    return result == pdPASS ? ESP_OK : ESP_ERR_NO_MEM;
}
