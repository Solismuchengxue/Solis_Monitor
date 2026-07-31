#pragma once

#include <stddef.h>

#include "network_client.h"

typedef enum {
    NETWORK_TASK_WAIT,
    NETWORK_TASK_RELOAD,
    NETWORK_TASK_RETRY,
    NETWORK_TASK_POLL,
} network_task_action_t;

typedef enum {
    NETWORK_INIT_DEFAULT_EVENT_LOOP = 1U << 0,
    NETWORK_INIT_DEFAULT_STA = 1U << 1,
    NETWORK_INIT_DEFAULT_AP = 1U << 2,
    NETWORK_INIT_WIFI = 1U << 3,
    NETWORK_INIT_WIFI_STARTED = 1U << 4,
    NETWORK_INIT_WIFI_HANDLER = 1U << 5,
    NETWORK_INIT_IP_HANDLER = 1U << 6,
} network_init_stage_t;

typedef enum {
    NETWORK_CLEANUP_DONE,
    NETWORK_CLEANUP_IP_HANDLER,
    NETWORK_CLEANUP_WIFI_HANDLER,
    NETWORK_CLEANUP_WIFI_STOP,
    NETWORK_CLEANUP_DEFAULT_STA,
    NETWORK_CLEANUP_DEFAULT_AP,
    NETWORK_CLEANUP_WIFI_DEINIT,
    NETWORK_CLEANUP_DEFAULT_EVENT_LOOP,
} network_cleanup_action_t;

network_task_action_t network_task_next_action(bool configured, bool got_ip,
                                               bool reconnect_requested);
uint32_t network_freshness_wait_ms(uint32_t requested_ms, uint32_t now_ms,
                                   uint32_t last_valid_ms, bool ever_valid,
                                   bool reported_expired);
bool network_apply_source_freshness(dashboard_store_t *store, uint32_t now_ms,
                                    uint32_t last_valid_ms, bool ever_valid,
                                    bool *reported_expired);
bool network_pc_inactive_elapsed(uint32_t now_ms, uint32_t last_valid_ms,
                                 bool ever_valid, uint32_t timeout_ms);
network_cleanup_action_t network_init_cleanup_next(uint32_t *completed_stages);
bool network_format_station_hostname(const uint8_t mac[6], char *hostname,
                                     size_t hostname_size);
