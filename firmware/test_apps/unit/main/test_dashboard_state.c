#include <math.h>

#include "unity.h"
#include "dashboard_state.h"

TEST_CASE("dashboard percentages are clamped", "[dashboard]")
{
    dashboard_state_t state = {0};
    state.system.cpu_usage = -5;
    state.system.gpu_usage = 130;
    state.system.memory_usage = 101;
    state.codex.context_used = 120;
    state.codex.main_weekly_remaining = -1;
    state.environment.humidity = 101;
    dashboard_state_sanitize(&state);
    TEST_ASSERT_EQUAL_FLOAT(0, state.system.cpu_usage);
    TEST_ASSERT_EQUAL_FLOAT(100, state.system.gpu_usage);
    TEST_ASSERT_EQUAL_FLOAT(100, state.system.memory_usage);
    TEST_ASSERT_EQUAL_FLOAT(100, state.codex.context_used);
    TEST_ASSERT_EQUAL_FLOAT(0, state.codex.main_weekly_remaining);
    TEST_ASSERT_EQUAL_FLOAT(100, state.environment.humidity);
}

TEST_CASE("dashboard empty state clears all fields", "[dashboard]")
{
    dashboard_state_t state = {
        .source_online = true,
        .system = {.cpu_usage = 42},
        .codex = {.online = true, .context_used = 51},
        .environment = {.humidity = 60},
    };

    dashboard_state_init_empty(&state);

    TEST_ASSERT_FALSE(state.source_online);
    TEST_ASSERT_FALSE(state.codex.online);
    TEST_ASSERT_TRUE(isnan(state.system.cpu_usage));
    TEST_ASSERT_TRUE(isnan(state.system.gpu_memory_temp_c));
    TEST_ASSERT_TRUE(isnan(state.system.memory_temp_c));
    TEST_ASSERT_TRUE(isnan(state.codex.context_used));
    TEST_ASSERT_TRUE(isnan(state.codex.main_weekly_remaining));
    TEST_ASSERT_TRUE(isnan(state.environment.indoor_temp_c));
    TEST_ASSERT_TRUE(isnan(state.environment.humidity));
}
