#pragma once

#include <stdbool.h>

#define DASHBOARD_MAX_STORAGE_DEVICES 4

typedef struct {
    char name[32];
    float usage, temp_c;
} storage_device_metrics_t;

typedef struct {
    char time[6];
    char cpu_name[48];
    float cpu_usage, cpu_temp_c, cpu_ghz, cpu_w;
    char gpu_name[48];
    float gpu_usage, gpu_temp_c, gpu_ghz, gpu_w;
    float gpu_memory_usage, gpu_memory_used_mb, gpu_memory_total_mb, gpu_memory_temp_c;
    float memory_usage, memory_temp_c, memory_used_gb, memory_total_gb;
    float fps, nvme_temp_c, download_mbps, upload_mbps;
    char network_name[32];
    unsigned storage_count;
    storage_device_metrics_t storage[DASHBOARD_MAX_STORAGE_DEVICES];
} system_metrics_t;

typedef struct {
    bool online;
    char project[32];
    char task[48];
    char model[32];
    char reasoning_effort[16];
    float context_used, context_used_k, context_window_k;
    float total_tokens, weekly_used_tokens;
    float main_weekly_remaining, spark_weekly_remaining;
    char main_quota_name[32];
    char spark_quota_name[32];
    char main_quota_reset_at[24];
    char spark_quota_reset_at[24];
} codex_metrics_t;

typedef struct {
    char location[64];
    char weather[24];
    char wind_direction[24];
    char wind_scale[12];
    float weather_icon;
    float outdoor_low_c, outdoor_high_c, indoor_temp_c, humidity;
} environment_metrics_t;

typedef struct {
    bool source_online;
    bool wifi_connected;
    char wifi_ssid[33];
    bool provisioning_active;
    char provisioning_ssid[33];
    unsigned provisioning_remaining_seconds;
    bool discovery_active;
    bool pairing_completed;
    char pairing_code[7];
    unsigned pairing_code_remaining_seconds;
    system_metrics_t system;
    codex_metrics_t codex;
    environment_metrics_t environment;
} dashboard_state_t;

void dashboard_state_init_empty(dashboard_state_t *state);
void dashboard_state_sanitize(dashboard_state_t *state);
