#pragma once

#include <stdbool.h>
#include <stdint.h>

#include "config_store.h"
#include "dashboard_store.h"
#include "device_control.h"
#include "esp_err.h"

typedef struct network_client network_client_t;

bool network_source_expired(uint32_t now_ms, uint32_t last_valid_ms, bool ever_valid);
uint32_t network_retry_delay_ms(unsigned failure_count);

esp_err_t network_client_start(network_client_t **client, config_store_t *config_store,
                               dashboard_store_t *dashboard_store,
                               device_control_t *device_control);
bool network_client_pc_inactive(network_client_t *client, uint32_t now_ms,
                                uint32_t timeout_ms);
void network_client_request_reconnect(network_client_t *client);
void network_client_request_provisioning(network_client_t *client);
void network_client_request_physical_action(network_client_t *client);
void network_client_request_mode_exit(network_client_t *client);
