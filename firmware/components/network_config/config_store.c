#include "config_store.h"

#include <string.h>

static bool config_equal(const network_config_t *left, const network_config_t *right)
{
    return left->port == right->port && strcmp(left->ssid, right->ssid) == 0 &&
           strcmp(left->password, right->password) == 0 && strcmp(left->host, right->host) == 0 &&
           strcmp(left->token, right->token) == 0;
}

static bool backend_is_complete(config_store_backend_t backend)
{
    return backend.read_slot && backend.write_slot && backend.read_active && backend.write_active &&
           backend.clear;
}

esp_err_t config_store_init(config_store_t *store, config_store_backend_t backend)
{
    if (!store || !backend_is_complete(backend)) return ESP_ERR_INVALID_ARG;
    store->backend = backend;
    return ESP_OK;
}

esp_err_t config_store_load(config_store_t *store, network_config_t *value, bool *present)
{
    uint8_t slot;
    bool active_present;
    bool slot_present;
    esp_err_t result;

    if (!store || !value || !present || !backend_is_complete(store->backend)) return ESP_ERR_INVALID_ARG;
    result = store->backend.read_active(store->backend.context, &slot, &active_present);
    if (result != ESP_OK) return result;
    if (!active_present) {
        *present = false;
        return ESP_OK;
    }
    if (slot > 1) return ESP_ERR_INVALID_STATE;

    result = store->backend.read_slot(store->backend.context, slot, value, &slot_present);
    if (result != ESP_OK) return result;
    if (!slot_present) {
        *present = false;
        return ESP_OK;
    }
    result = network_config_validate(value);
    if (result != ESP_OK) return result;
    *present = true;
    return ESP_OK;
}

esp_err_t config_store_save(config_store_t *store, const network_config_t *value)
{
    network_config_t candidate;
    network_config_t read_back;
    bool active_present;
    bool read_back_present;
    uint8_t active_slot;
    uint8_t target_slot;
    esp_err_t result;

    if (!store || !value || !backend_is_complete(store->backend)) return ESP_ERR_INVALID_ARG;
    result = network_config_validate(value);
    if (result != ESP_OK) return result;

    candidate = *value;
    network_config_normalize_token(&candidate);

    result = store->backend.read_active(store->backend.context, &active_slot, &active_present);
    if (result != ESP_OK) return result;
    if (active_present && active_slot > 1) return ESP_ERR_INVALID_STATE;
    target_slot = active_present ? (uint8_t)(1 - active_slot) : 0;

    result = store->backend.write_slot(store->backend.context, target_slot, &candidate);
    if (result != ESP_OK) return result;
    result = store->backend.read_slot(store->backend.context, target_slot, &read_back, &read_back_present);
    if (result != ESP_OK) return result;
    if (!read_back_present || network_config_validate(&read_back) != ESP_OK ||
        !config_equal(&candidate, &read_back)) {
        return ESP_ERR_INVALID_STATE;
    }
    return store->backend.write_active(store->backend.context, target_slot);
}

esp_err_t config_store_clear(config_store_t *store)
{
    if (!store || !backend_is_complete(store->backend)) return ESP_ERR_INVALID_ARG;
    return store->backend.clear(store->backend.context);
}
