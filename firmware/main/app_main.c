#include <stdlib.h>
#include <time.h>

#include "board.h"
#include "config_store.h"
#include "dashboard_store.h"
#include "device_control.h"
#include "display.h"
#include "environment_sensor.h"
#include "esp_heap_caps.h"
#include "esp_idf_version.h"
#include "esp_log.h"
#include "esp_netif_sntp.h"
#include "esp_system.h"
#include "esp_timer.h"
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include "network_client.h"
#include "nvs_config_backend.h"
#include "nvs_flash.h"
#include "ota_update.h"
#include "renderer.h"
#include "serial_setup.h"
#include "ui.h"

#define DISPLAY_WIDTH 800
#define DISPLAY_HEIGHT 480
#define FRAMEBUFFER_BYTES (DISPLAY_WIDTH * DISPLAY_HEIGHT * sizeof(uint16_t))
#define PC_INACTIVE_BACKLIGHT_MS 300000U
#define BACKLIGHT_WAKE_MS 30000U
#define NIGHT_SETTINGS_CONFIRMATION_MS 5000U
#define VALID_UNIX_TIME 1700000000

static const char *TAG = "solis_monitor";

static uint32_t now_ms(void)
{
    return (uint32_t)(esp_timer_get_time() / 1000);
}

static void request_network_reconnect(void *context)
{
    network_client_request_reconnect(context);
}

static void verify_psram_or_abort(void)
{
    size_t psram_size = heap_caps_get_total_size(MALLOC_CAP_SPIRAM);

    ESP_LOGI(TAG, "ESP-IDF=%s flash=%s", esp_get_idf_version(),
             CONFIG_ESPTOOLPY_FLASHSIZE);
    ESP_LOGI(TAG, "PSRAM detected=%u bytes internal_free=%u bytes psram_free=%u bytes",
             (unsigned)psram_size,
             (unsigned)heap_caps_get_free_size(MALLOC_CAP_INTERNAL),
             (unsigned)heap_caps_get_free_size(MALLOC_CAP_SPIRAM));
    if (psram_size == 0) {
        ESP_LOGE(TAG, "PSRAM is not initialized");
        abort();
    }
}

static void log_memory_watermarks(void)
{
    ESP_LOGI(TAG, "heap free internal=%u psram=%u; minimum internal=%u psram=%u",
             (unsigned)heap_caps_get_free_size(MALLOC_CAP_INTERNAL),
             (unsigned)heap_caps_get_free_size(MALLOC_CAP_SPIRAM),
             (unsigned)heap_caps_get_minimum_free_size(MALLOC_CAP_INTERNAL),
             (unsigned)heap_caps_get_minimum_free_size(MALLOC_CAP_SPIRAM));
}

static bool local_minute_now(int16_t utc_offset_minutes,
                             uint16_t *local_minute)
{
    if (!local_minute) return false;
    time_t utc = time(NULL);
    if (utc < VALID_UNIX_TIME) return false;
    time_t adjusted = utc + (time_t)utc_offset_minutes * 60;
    struct tm local;
    if (!gmtime_r(&adjusted, &local)) return false;
    *local_minute = (uint16_t)(local.tm_hour * 60 + local.tm_min);
    return true;
}

static esp_err_t flush_dirty(display_t *display, const uint16_t *framebuffer,
                             const ui_dirty_list_t *dirty)
{
    for (size_t index = 0; index < dirty->count; ++index) {
        esp_err_t err = display_flush_rect(display, framebuffer, DISPLAY_WIDTH,
                                           dirty->rects[index]);
        if (err != ESP_OK) return err;
    }
    return ESP_OK;
}

void app_main(void)
{
    display_t *display = NULL;
    dashboard_store_t dashboard_store;
    dashboard_state_t state;
    config_store_backend_t nvs_backend;
    config_store_t config_store;
    device_control_t *device_control = NULL;
    network_client_t *network_client;
    ui_dirty_list_t dirty;

    ESP_ERROR_CHECK(board_init());
    verify_psram_or_abort();
    ESP_ERROR_CHECK(display_init(&display));

    uint16_t *framebuffer = heap_caps_malloc(FRAMEBUFFER_BYTES,
                                             MALLOC_CAP_SPIRAM | MALLOC_CAP_8BIT);
    if (!framebuffer) {
        ESP_LOGE(TAG, "framebuffer allocation failed: requested=%u available=%u bytes",
                 (unsigned)FRAMEBUFFER_BYTES,
                 (unsigned)heap_caps_get_free_size(MALLOC_CAP_SPIRAM));
        board_backlight_set(false);
        abort();
    }
    ESP_LOGI(TAG, "framebuffer=%p", framebuffer);

    renderer_surface_t surface = {
        .pixels = framebuffer,
        .width = DISPLAY_WIDTH,
        .height = DISPLAY_HEIGHT,
        .stride = DISPLAY_WIDTH,
    };
    ui_page_t page = UI_PAGE_PC;

    ESP_ERROR_CHECK(dashboard_store_init(&dashboard_store));
    dashboard_state_init_empty(&state);
    ESP_ERROR_CHECK(dashboard_store_replace(&dashboard_store, &state, 0, 0) ? ESP_OK : ESP_FAIL);
    ui_render_full(&surface, page, &state, &dirty);
    ESP_ERROR_CHECK(flush_dirty(display, framebuffer, &dirty));
    ESP_ERROR_CHECK(display_set_enabled(display, true));
    board_backlight_set(true);

    esp_err_t nvs_err = nvs_flash_init();
    if (nvs_err == ESP_ERR_NVS_NO_FREE_PAGES || nvs_err == ESP_ERR_NVS_NEW_VERSION_FOUND) {
        ESP_ERROR_CHECK(nvs_flash_erase());
        ESP_ERROR_CHECK(nvs_flash_init());
    } else {
        ESP_ERROR_CHECK(nvs_err);
    }
    ESP_ERROR_CHECK(nvs_config_backend_create(&nvs_backend));
    ESP_ERROR_CHECK(config_store_init(&config_store, nvs_backend));
    ESP_ERROR_CHECK(device_control_init(&device_control));
    device_control_settings_t display_settings;
    ESP_ERROR_CHECK(device_control_get(device_control, &display_settings));
    ESP_ERROR_CHECK(
        board_backlight_set_percent(display_settings.brightness_percent));
    ESP_ERROR_CHECK(network_client_start(
        &network_client, &config_store, &dashboard_store, device_control));
    esp_sntp_config_t sntp_config =
        ESP_NETIF_SNTP_DEFAULT_CONFIG("pool.ntp.org");
    esp_err_t sntp_result = esp_netif_sntp_init(&sntp_config);
    if (sntp_result != ESP_OK)
        ESP_LOGW(TAG, "SNTP initialization failed: %s",
                 esp_err_to_name(sntp_result));
    ESP_ERROR_CHECK(serial_setup_start(&config_store, request_network_reconnect, network_client));
    ESP_ERROR_CHECK(environment_sensor_start(&dashboard_store));

    uint32_t last_metrics_ms = now_ms();
    uint32_t last_memory_log_ms = last_metrics_ms;
    uint32_t backlight_wake_until_ms = 0;
    uint32_t night_confirmation_until_ms = 0;
    uint8_t applied_brightness = display_settings.brightness_percent;
    bool was_provisioning = false;
    bool was_discovery = false;
    bool was_pairing_completed = false;
    bool was_night_confirmation = false;
    bool ota_pending_verify = ota_update_running_pending_verify();
    if (ota_pending_verify)
        ESP_LOGW(TAG, "OTA image pending health confirmation");
#ifdef SOLIS_OTA_ROLLBACK_TEST
    if (ota_pending_verify) {
        ESP_LOGE(TAG, "controlled rollback test: restarting before health confirmation");
        esp_restart();
    }
#endif
    uint64_t sequence;
    int64_t generated_at;
    for (;;) {
        uint32_t now = now_ms();
        button_event_t button_event = board_button_event(now);
        bool night_confirmation_active = device_control_wake_active(
            now, night_confirmation_until_ms);
        bool interaction_active =
            state.provisioning_active || state.discovery_active ||
            state.pairing_completed || night_confirmation_active;
        uint16_t local_minute = 0;
        bool time_valid = local_minute_now(
            display_settings.utc_offset_minutes, &local_minute);
        bool night_active =
            display_settings.night_enabled && time_valid &&
            device_control_night_active(
                local_minute, display_settings.night_start_minute,
                display_settings.night_end_minute);
        bool pc_inactive = network_client_pc_inactive(
            network_client, now, PC_INACTIVE_BACKLIGHT_MS);
        bool sleep_requested = device_control_should_sleep(
            interaction_active, night_active, pc_inactive);
        bool wake_active = device_control_wake_active(
            now, backlight_wake_until_ms);
        if (sleep_requested && !wake_active &&
            applied_brightness == 0 &&
            button_event != BUTTON_EVENT_NONE) {
            backlight_wake_until_ms = now + BACKLIGHT_WAKE_MS;
            wake_active = true;
            button_event = BUTTON_EVENT_NONE;
            ESP_LOGI(TAG, "backlight wake requested by button");
        }
        uint8_t desired_brightness =
            !sleep_requested || wake_active
                ? display_settings.brightness_percent
                : 0U;
        if (desired_brightness != applied_brightness) {
            ESP_ERROR_CHECK(
                board_backlight_set_percent(desired_brightness));
            applied_brightness = desired_brightness;
        }
        if (button_event == BUTTON_EVENT_SHORT_PRESS) {
            if (interaction_active) {
                ESP_LOGI(TAG, "interaction mode exit requested by short press");
                network_client_request_mode_exit(network_client);
            } else {
                page = page == UI_PAGE_PC ? UI_PAGE_CODEX : UI_PAGE_PC;
                ESP_ERROR_CHECK(dashboard_store_snapshot(&dashboard_store, &state, &sequence,
                                                         &generated_at) ? ESP_OK : ESP_FAIL);
                ui_render_full(&surface, page, &state, &dirty);
                ESP_ERROR_CHECK(flush_dirty(display, framebuffer, &dirty));
            }
        }
        if (!interaction_active && button_event == BUTTON_EVENT_DOUBLE_PRESS) {
            ESP_LOGI(TAG, "physical pairing requested by double press");
            network_client_request_physical_action(network_client);
        }
        if (!interaction_active && button_event == BUTTON_EVENT_LONG_PRESS) {
            ESP_LOGI(TAG, "AP provisioning requested by long press");
            network_client_request_provisioning(network_client);
        }
        if ((uint32_t)(now - last_metrics_ms) >= 1000U) {
            last_metrics_ms += 1000U;
            ESP_ERROR_CHECK(
                device_control_get(device_control, &display_settings));
            device_control_settings_t confirmed_settings;
            bool night_settings_updated =
                device_control_take_night_settings_updated(
                    device_control, &confirmed_settings);
            ESP_ERROR_CHECK(dashboard_store_snapshot(&dashboard_store, &state, &sequence,
                                                     &generated_at) ? ESP_OK : ESP_FAIL);
            if (ota_pending_verify &&
                ota_update_health_ready(
                    now, state.wifi_connected || state.provisioning_active)) {
                esp_err_t confirm_result = ota_update_confirm_running();
                if (confirm_result == ESP_OK) {
                    ota_pending_verify = false;
                    ESP_LOGI(TAG, "OTA image confirmed after health check");
                } else {
                    ESP_LOGE(TAG, "OTA confirmation failed: %s",
                             esp_err_to_name(confirm_result));
                }
            }
            if (night_settings_updated) {
                night_confirmation_until_ms =
                    now + NIGHT_SETTINGS_CONFIRMATION_MS;
                backlight_wake_until_ms = night_confirmation_until_ms;
                ui_render_night_settings_confirmation(
                    &surface, confirmed_settings.night_enabled,
                    confirmed_settings.night_start_minute,
                    confirmed_settings.night_end_minute, &dirty);
                was_night_confirmation = true;
            } else if (night_confirmation_active) {
                dirty.count = 0;
            } else if (was_night_confirmation) {
                ui_render_full(&surface, page, &state, &dirty);
                was_night_confirmation = false;
            } else if (state.provisioning_active != was_provisioning ||
                state.discovery_active != was_discovery ||
                state.pairing_completed != was_pairing_completed) {
                ui_render_full(&surface, page, &state, &dirty);
                was_provisioning = state.provisioning_active;
                was_discovery = state.discovery_active;
                was_pairing_completed = state.pairing_completed;
            } else {
                ui_render_update(&surface, page, &state, &dirty);
            }
            ESP_ERROR_CHECK(flush_dirty(display, framebuffer, &dirty));
        }
        if ((uint32_t)(now - last_memory_log_ms) >= 60000U) {
            last_memory_log_ms = now;
            log_memory_watermarks();
        }
        if (device_control_take_restart_requested(device_control)) {
            ESP_LOGW(TAG, "remote device restart requested");
            vTaskDelay(pdMS_TO_TICKS(250));
            esp_restart();
        }
        vTaskDelay(pdMS_TO_TICKS(10));
    }
}
