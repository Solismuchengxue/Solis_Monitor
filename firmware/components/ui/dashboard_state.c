#include <math.h>

#include "dashboard_state.h"

void dashboard_state_init_empty(dashboard_state_t *state)
{
    if (!state) return;
    *state = (dashboard_state_t){0};
    state->system.cpu_usage = NAN;
    state->system.cpu_temp_c = NAN;
    state->system.cpu_ghz = NAN;
    state->system.cpu_w = NAN;
    state->system.gpu_usage = NAN;
    state->system.gpu_temp_c = NAN;
    state->system.gpu_ghz = NAN;
    state->system.gpu_w = NAN;
    state->system.gpu_memory_usage = NAN;
    state->system.gpu_memory_used_mb = NAN;
    state->system.gpu_memory_total_mb = NAN;
    state->system.gpu_memory_temp_c = NAN;
    state->system.memory_usage = NAN;
    state->system.memory_temp_c = NAN;
    state->system.memory_used_gb = NAN;
    state->system.memory_total_gb = NAN;
    state->system.fps = NAN;
    state->system.nvme_temp_c = NAN;
    state->system.download_mbps = NAN;
    state->system.upload_mbps = NAN;
    state->codex.context_used = NAN;
    state->codex.context_used_k = NAN;
    state->codex.context_window_k = NAN;
    state->codex.total_tokens = NAN;
    state->codex.weekly_used_tokens = NAN;
    state->codex.main_weekly_remaining = NAN;
    state->codex.spark_weekly_remaining = NAN;
    state->environment.outdoor_low_c = NAN;
    state->environment.outdoor_high_c = NAN;
    state->environment.weather_icon = NAN;
    state->environment.indoor_temp_c = NAN;
    state->environment.humidity = NAN;
}

static float percent(float value)
{
    if (value < 0) return 0;
    if (value > 100) return 100;
    return value;
}

void dashboard_state_sanitize(dashboard_state_t *state)
{
    if (!state) return;
    state->system.cpu_usage = percent(state->system.cpu_usage);
    state->system.gpu_usage = percent(state->system.gpu_usage);
    state->system.gpu_memory_usage = percent(state->system.gpu_memory_usage);
    state->system.memory_usage = percent(state->system.memory_usage);
    for (unsigned index = 0; index < state->system.storage_count; ++index)
        state->system.storage[index].usage = percent(state->system.storage[index].usage);
    state->codex.context_used = percent(state->codex.context_used);
    state->codex.main_weekly_remaining = percent(state->codex.main_weekly_remaining);
    state->codex.spark_weekly_remaining = percent(state->codex.spark_weekly_remaining);
    state->environment.humidity = percent(state->environment.humidity);
}
