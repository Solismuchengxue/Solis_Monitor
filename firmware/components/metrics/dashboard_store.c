#include "dashboard_store.h"

#include <stdio.h>
#include <string.h>

#include "freertos/FreeRTOS.h"

esp_err_t dashboard_store_init(dashboard_store_t *store)
{
    if (!store) return ESP_ERR_INVALID_ARG;

    memset(store, 0, sizeof(*store));
    store->mutex = xSemaphoreCreateMutex();
    return store->mutex ? ESP_OK : ESP_ERR_NO_MEM;
}

void dashboard_store_deinit(dashboard_store_t *store)
{
    if (!store || !store->mutex) return;

    vSemaphoreDelete(store->mutex);
    store->mutex = NULL;
}

bool dashboard_store_snapshot(dashboard_store_t *store, dashboard_state_t *state,
                              uint64_t *sequence, int64_t *generated_at)
{
    if (!store || !state || !sequence || !generated_at || !store->mutex) return false;
    if (xSemaphoreTake(store->mutex, portMAX_DELAY) != pdTRUE) return false;

    *state = store->state;
    *sequence = store->sequence;
    *generated_at = store->generated_at;
    xSemaphoreGive(store->mutex);
    return true;
}

bool dashboard_store_replace(dashboard_store_t *store, const dashboard_state_t *state,
                             uint64_t sequence, int64_t generated_at)
{
    if (!store || !state || !store->mutex) return false;
    if (xSemaphoreTake(store->mutex, portMAX_DELAY) != pdTRUE) return false;

    bool preserve_local_environment = store->local_environment_owned;
    float indoor_temp_c = store->state.environment.indoor_temp_c;
    float humidity = store->state.environment.humidity;
    bool preserve_local_wifi = store->local_wifi_owned;
    bool wifi_connected = store->state.wifi_connected;
    char wifi_ssid[sizeof(store->state.wifi_ssid)];
    memcpy(wifi_ssid, store->state.wifi_ssid, sizeof(wifi_ssid));
    bool preserve_local_provisioning = store->local_provisioning_owned;
    bool provisioning_active = store->state.provisioning_active;
    char provisioning_ssid[sizeof(store->state.provisioning_ssid)];
    memcpy(provisioning_ssid, store->state.provisioning_ssid, sizeof(provisioning_ssid));
    unsigned provisioning_remaining_seconds = store->state.provisioning_remaining_seconds;
    bool preserve_local_pairing = store->local_pairing_owned;
    bool discovery_active = store->state.discovery_active;
    bool pairing_completed = store->state.pairing_completed;
    char pairing_code[sizeof(store->state.pairing_code)];
    memcpy(pairing_code, store->state.pairing_code, sizeof(pairing_code));
    unsigned pairing_code_remaining_seconds =
        store->state.pairing_code_remaining_seconds;
    store->state = *state;
    if (preserve_local_environment) {
        store->state.environment.indoor_temp_c = indoor_temp_c;
        store->state.environment.humidity = humidity;
    }
    if (preserve_local_wifi) {
        store->state.wifi_connected = wifi_connected;
        memcpy(store->state.wifi_ssid, wifi_ssid, sizeof(wifi_ssid));
    }
    if (preserve_local_provisioning) {
        store->state.provisioning_active = provisioning_active;
        memcpy(store->state.provisioning_ssid, provisioning_ssid,
               sizeof(provisioning_ssid));
        store->state.provisioning_remaining_seconds = provisioning_remaining_seconds;
    }
    if (preserve_local_pairing) {
        store->state.discovery_active = discovery_active;
        store->state.pairing_completed = pairing_completed;
        memcpy(store->state.pairing_code, pairing_code, sizeof(pairing_code));
        store->state.pairing_code_remaining_seconds =
            pairing_code_remaining_seconds;
    }
    store->sequence = sequence;
    store->generated_at = generated_at;
    xSemaphoreGive(store->mutex);
    return true;
}

bool dashboard_store_set_provisioning(dashboard_store_t *store, bool active, const char *ssid,
                                      unsigned remaining_seconds)
{
    if (!store || !store->mutex) return false;
    if (xSemaphoreTake(store->mutex, portMAX_DELAY) != pdTRUE) return false;

    store->state.provisioning_active = active;
    snprintf(store->state.provisioning_ssid, sizeof(store->state.provisioning_ssid), "%s",
             active && ssid ? ssid : "");
    store->state.provisioning_remaining_seconds = active ? remaining_seconds : 0;
    store->local_provisioning_owned = true;
    xSemaphoreGive(store->mutex);
    return true;
}

bool dashboard_store_set_pairing(dashboard_store_t *store, bool discovery_active,
                                 const char *pairing_code,
                                 unsigned pairing_code_remaining_seconds,
                                 bool pairing_completed)
{
    if (!store || !store->mutex) return false;
    if (xSemaphoreTake(store->mutex, portMAX_DELAY) != pdTRUE) return false;

    store->state.discovery_active = discovery_active;
    store->state.pairing_completed = pairing_completed;
    snprintf(store->state.pairing_code, sizeof(store->state.pairing_code), "%s",
             discovery_active && pairing_code ? pairing_code : "");
    store->state.pairing_code_remaining_seconds =
        discovery_active ? pairing_code_remaining_seconds : 0;
    store->local_pairing_owned = true;
    xSemaphoreGive(store->mutex);
    return true;
}

bool dashboard_store_set_wifi_state(dashboard_store_t *store, bool connected, const char *ssid)
{
    if (!store || !store->mutex) return false;
    if (xSemaphoreTake(store->mutex, portMAX_DELAY) != pdTRUE) return false;

    store->state.wifi_connected = connected;
    snprintf(store->state.wifi_ssid, sizeof(store->state.wifi_ssid), "%s",
             connected && ssid ? ssid : "");
    store->local_wifi_owned = true;
    xSemaphoreGive(store->mutex);
    return true;
}

bool dashboard_store_set_local_environment(dashboard_store_t *store, float indoor_temp_c,
                                           float humidity)
{
    if (!store || !store->mutex) return false;
    if (xSemaphoreTake(store->mutex, portMAX_DELAY) != pdTRUE) return false;

    store->state.environment.indoor_temp_c = indoor_temp_c;
    store->state.environment.humidity = humidity;
    store->local_environment_owned = true;
    xSemaphoreGive(store->mutex);
    return true;
}

bool dashboard_store_set_source_online(dashboard_store_t *store, bool online)
{
    if (!store || !store->mutex) return false;
    if (xSemaphoreTake(store->mutex, portMAX_DELAY) != pdTRUE) return false;

    store->state.source_online = online;
    xSemaphoreGive(store->mutex);
    return true;
}
