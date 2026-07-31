#pragma once

#include <stdbool.h>
#include <stdint.h>

#include "dashboard_state.h"
#include "esp_err.h"
#include "freertos/FreeRTOS.h"
#include "freertos/semphr.h"

typedef struct {
    SemaphoreHandle_t mutex;
    dashboard_state_t state;
    uint64_t sequence;
    int64_t generated_at;
    bool local_environment_owned;
    bool local_wifi_owned;
    bool local_provisioning_owned;
    bool local_pairing_owned;
} dashboard_store_t;

esp_err_t dashboard_store_init(dashboard_store_t *store);
void dashboard_store_deinit(dashboard_store_t *store);
bool dashboard_store_snapshot(dashboard_store_t *store, dashboard_state_t *state,
                              uint64_t *sequence, int64_t *generated_at);
bool dashboard_store_replace(dashboard_store_t *store, const dashboard_state_t *state,
                             uint64_t sequence, int64_t generated_at);
bool dashboard_store_set_source_online(dashboard_store_t *store, bool online);
bool dashboard_store_set_local_environment(dashboard_store_t *store, float indoor_temp_c,
                                           float humidity);
bool dashboard_store_set_wifi_state(dashboard_store_t *store, bool connected, const char *ssid);
bool dashboard_store_set_provisioning(dashboard_store_t *store, bool active, const char *ssid,
                                      unsigned remaining_seconds);
bool dashboard_store_set_pairing(dashboard_store_t *store, bool discovery_active,
                                 const char *pairing_code,
                                 unsigned pairing_code_remaining_seconds,
                                 bool pairing_completed);
