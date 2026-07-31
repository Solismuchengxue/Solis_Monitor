#include "nvs_config_backend.h"

#include <stddef.h>

#include "nvs.h"

#define NETCFG_NAMESPACE "netcfg"

static const char *const slot_keys[2][5] = {
    {"a_ssid", "a_pass", "a_host", "a_port", "a_token"},
    {"b_ssid", "b_pass", "b_host", "b_port", "b_token"},
};

static esp_err_t nvs_read_slot(void *context, uint8_t slot, network_config_t *value, bool *present)
{
    nvs_handle_t handle;
    network_config_t candidate = {0};
    size_t ssid_size = sizeof(candidate.ssid);
    size_t password_size = sizeof(candidate.password);
    size_t host_size = sizeof(candidate.host);
    size_t token_size = sizeof(candidate.token);
    esp_err_t result;

    (void)context;
    if (slot > 1 || !value || !present) return ESP_ERR_INVALID_ARG;
    result = nvs_open(NETCFG_NAMESPACE, NVS_READONLY, &handle);
    if (result == ESP_ERR_NVS_NOT_FOUND) {
        *present = false;
        return ESP_OK;
    }
    if (result != ESP_OK) return result;

    result = nvs_get_str(handle, slot_keys[slot][0], candidate.ssid, &ssid_size);
    if (result == ESP_OK) result = nvs_get_str(handle, slot_keys[slot][1], candidate.password, &password_size);
    if (result == ESP_OK) result = nvs_get_str(handle, slot_keys[slot][2], candidate.host, &host_size);
    if (result == ESP_OK) result = nvs_get_u16(handle, slot_keys[slot][3], &candidate.port);
    if (result == ESP_OK) result = nvs_get_str(handle, slot_keys[slot][4], candidate.token, &token_size);
    nvs_close(handle);

    if (result == ESP_ERR_NVS_NOT_FOUND) {
        *present = false;
        return ESP_OK;
    }
    if (result != ESP_OK) return result;
    *value = candidate;
    *present = true;
    return ESP_OK;
}

static esp_err_t nvs_write_slot(void *context, uint8_t slot, const network_config_t *value)
{
    nvs_handle_t handle;
    esp_err_t result;

    (void)context;
    if (slot > 1 || !value) return ESP_ERR_INVALID_ARG;
    result = nvs_open(NETCFG_NAMESPACE, NVS_READWRITE, &handle);
    if (result != ESP_OK) return result;

    result = nvs_set_str(handle, slot_keys[slot][0], value->ssid);
    if (result == ESP_OK) result = nvs_set_str(handle, slot_keys[slot][1], value->password);
    if (result == ESP_OK) result = nvs_set_str(handle, slot_keys[slot][2], value->host);
    if (result == ESP_OK) result = nvs_set_u16(handle, slot_keys[slot][3], value->port);
    if (result == ESP_OK) result = nvs_set_str(handle, slot_keys[slot][4], value->token);
    if (result == ESP_OK) result = nvs_commit(handle);
    nvs_close(handle);
    return result;
}

static esp_err_t nvs_read_active(void *context, uint8_t *slot, bool *present)
{
    nvs_handle_t handle;
    esp_err_t result;

    (void)context;
    if (!slot || !present) return ESP_ERR_INVALID_ARG;
    result = nvs_open(NETCFG_NAMESPACE, NVS_READONLY, &handle);
    if (result == ESP_ERR_NVS_NOT_FOUND) {
        *present = false;
        return ESP_OK;
    }
    if (result != ESP_OK) return result;
    result = nvs_get_u8(handle, "active", slot);
    nvs_close(handle);
    if (result == ESP_ERR_NVS_NOT_FOUND) {
        *present = false;
        return ESP_OK;
    }
    if (result != ESP_OK) return result;
    *present = true;
    return ESP_OK;
}

static esp_err_t nvs_write_active(void *context, uint8_t slot)
{
    nvs_handle_t handle;
    esp_err_t result;

    (void)context;
    if (slot > 1) return ESP_ERR_INVALID_ARG;
    result = nvs_open(NETCFG_NAMESPACE, NVS_READWRITE, &handle);
    if (result != ESP_OK) return result;
    result = nvs_set_u8(handle, "active", slot);
    if (result == ESP_OK) result = nvs_commit(handle);
    nvs_close(handle);
    return result;
}

static esp_err_t nvs_clear(void *context)
{
    nvs_handle_t handle;
    esp_err_t result;

    (void)context;
    result = nvs_open(NETCFG_NAMESPACE, NVS_READWRITE, &handle);
    if (result != ESP_OK) return result;
    result = nvs_erase_all(handle);
    if (result == ESP_OK) result = nvs_commit(handle);
    nvs_close(handle);
    return result;
}

esp_err_t nvs_config_backend_create(config_store_backend_t *backend)
{
    if (!backend) return ESP_ERR_INVALID_ARG;
    *backend = (config_store_backend_t){
        .context = NULL,
        .read_slot = nvs_read_slot,
        .write_slot = nvs_write_slot,
        .read_active = nvs_read_active,
        .write_active = nvs_write_active,
        .clear = nvs_clear,
    };
    return ESP_OK;
}
