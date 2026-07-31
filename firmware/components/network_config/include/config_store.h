#pragma once

#include <stdbool.h>
#include <stdint.h>

#include "esp_err.h"
#include "network_config.h"

typedef struct {
    void *context;
    esp_err_t (*read_slot)(void *context, uint8_t slot, network_config_t *value, bool *present);
    esp_err_t (*write_slot)(void *context, uint8_t slot, const network_config_t *value);
    esp_err_t (*read_active)(void *context, uint8_t *slot, bool *present);
    esp_err_t (*write_active)(void *context, uint8_t slot);
    esp_err_t (*clear)(void *context);
} config_store_backend_t;

typedef struct {
    config_store_backend_t backend;
} config_store_t;

esp_err_t config_store_init(config_store_t *store, config_store_backend_t backend);
esp_err_t config_store_load(config_store_t *store, network_config_t *value, bool *present);
esp_err_t config_store_save(config_store_t *store, const network_config_t *value);
esp_err_t config_store_clear(config_store_t *store);
