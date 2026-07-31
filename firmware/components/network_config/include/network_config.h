#pragma once

#include <stdint.h>

#include "esp_err.h"

#define NETCFG_DEFAULT_PORT 18472
#define NETCFG_TOKEN_HEX_LENGTH 64U

typedef struct {
    char ssid[33];
    char password[65];
    char host[16];
    uint16_t port;
    char token[65];
} network_config_t;

esp_err_t network_config_validate(const network_config_t *config);
void network_config_normalize_token(network_config_t *config);
