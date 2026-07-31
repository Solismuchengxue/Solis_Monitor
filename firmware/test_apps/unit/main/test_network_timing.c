#include <stddef.h>
#include <stdint.h>

#include "network_client_internal.h"
#include "unity.h"

TEST_CASE("network source expires at five seconds including timer wrap", "[network_client]")
{
    TEST_ASSERT_FALSE(network_source_expired(4999, 0, true));
    TEST_ASSERT_TRUE(network_source_expired(5000, 0, true));
    TEST_ASSERT_FALSE(network_source_expired(3, UINT32_MAX - 4995U, true));
    TEST_ASSERT_TRUE(network_source_expired(4, UINT32_MAX - 4995U, true));
}

TEST_CASE("network source is not expired before the first valid response", "[network_client]")
{
    TEST_ASSERT_FALSE(network_source_expired(UINT32_MAX, 0, false));
}

TEST_CASE("PC inactivity starts at five minutes and handles timer wrap",
          "[network_client]")
{
    const uint32_t timeout_ms = 5U * 60U * 1000U;

    TEST_ASSERT_FALSE(network_pc_inactive_elapsed(
        timeout_ms - 1U, 0, false, timeout_ms));
    TEST_ASSERT_TRUE(network_pc_inactive_elapsed(
        timeout_ms, 0, false, timeout_ms));
    TEST_ASSERT_FALSE(network_pc_inactive_elapsed(
        timeout_ms + 4999U, 5000U, true, timeout_ms));
    TEST_ASSERT_TRUE(network_pc_inactive_elapsed(
        timeout_ms + 5000U, 5000U, true, timeout_ms));
    TEST_ASSERT_FALSE(network_pc_inactive_elapsed(
        3U, UINT32_MAX - timeout_ms + 5U, true, timeout_ms));
    TEST_ASSERT_TRUE(network_pc_inactive_elapsed(
        4U, UINT32_MAX - timeout_ms + 5U, true, timeout_ms));
    TEST_ASSERT_FALSE(network_pc_inactive_elapsed(
        UINT32_MAX, 0, true, 0));
}

TEST_CASE("network retry delay doubles then caps at thirty seconds", "[network_client]")
{
    const uint32_t expected[] = {1000, 2000, 4000, 8000, 16000, 30000, 30000};

    for (unsigned failure_count = 1;
         failure_count <= sizeof(expected) / sizeof(expected[0]); ++failure_count) {
        TEST_ASSERT_EQUAL_UINT32(expected[failure_count - 1],
                                 network_retry_delay_ms(failure_count));
    }
}

TEST_CASE("station hostname uses final four STA MAC digits", "[network_client]")
{
    const uint8_t mac[6] = {0x34, 0x85, 0x18, 0x6A, 0xAF, 0x28};
    char hostname[33];

    TEST_ASSERT_TRUE(network_format_station_hostname(mac, hostname, sizeof(hostname)));
    TEST_ASSERT_EQUAL_STRING("Solis_Monitor_AF28", hostname);
    TEST_ASSERT_FALSE(network_format_station_hostname(mac, hostname, 18));
    TEST_ASSERT_FALSE(network_format_station_hostname(NULL, hostname, sizeof(hostname)));
}

TEST_CASE("unconfigured client reloads when reconnect command wakes it", "[network_client]")
{
    TEST_ASSERT_EQUAL(NETWORK_TASK_WAIT,
                      network_task_next_action(false, false, false));
    TEST_ASSERT_EQUAL(NETWORK_TASK_RELOAD,
                      network_task_next_action(false, false, true));
    TEST_ASSERT_EQUAL(NETWORK_TASK_RETRY,
                      network_task_next_action(true, false, false));
    TEST_ASSERT_EQUAL(NETWORK_TASK_POLL,
                      network_task_next_action(true, true, false));
}

TEST_CASE("freshness bounds unconfigured and reconnect waits", "[network_client]")
{
    TEST_ASSERT_EQUAL(NETWORK_TASK_WAIT,
                      network_task_next_action(false, false, false));
    TEST_ASSERT_EQUAL_UINT32(5000,
                             network_freshness_wait_ms(UINT32_MAX, 0, 0, true, false));
    TEST_ASSERT_EQUAL(NETWORK_TASK_RETRY,
                      network_task_next_action(true, false, false));
    TEST_ASSERT_EQUAL_UINT32(30000, network_retry_delay_ms(6));
    TEST_ASSERT_EQUAL_UINT32(5000,
                             network_freshness_wait_ms(network_retry_delay_ms(6),
                                                       0, 0, true, false));
    TEST_ASSERT_EQUAL_UINT32(500,
                             network_freshness_wait_ms(30000, 4500, 0, true, false));
    TEST_ASSERT_EQUAL_UINT32(30000,
                             network_freshness_wait_ms(30000, 5000, 0, true, true));
    TEST_ASSERT_EQUAL_UINT32(UINT32_MAX,
                             network_freshness_wait_ms(UINT32_MAX, 0, 0, false, false));
}

TEST_CASE("freshness transition changes only transport online state", "[network_client]")
{
    dashboard_store_t store = {0};
    dashboard_state_t input = {.source_online = true, .codex = {.online = true}};
    dashboard_state_t output = {0};
    uint64_t sequence = 0;
    int64_t generated_at = 0;
    bool reported_expired = false;

    TEST_ASSERT_EQUAL(ESP_OK, dashboard_store_init(&store));
    TEST_ASSERT_TRUE(dashboard_store_replace(&store, &input, 1, 2));
    TEST_ASSERT_FALSE(network_apply_source_freshness(&store, 4999, 0, true,
                                                     &reported_expired));
    TEST_ASSERT_TRUE(network_apply_source_freshness(&store, 5000, 0, true,
                                                    &reported_expired));
    TEST_ASSERT_TRUE(reported_expired);
    TEST_ASSERT_FALSE(network_apply_source_freshness(&store, 30000, 0, true,
                                                     &reported_expired));
    TEST_ASSERT_TRUE(dashboard_store_snapshot(&store, &output, &sequence, &generated_at));
    input.source_online = false;
    TEST_ASSERT_EQUAL_MEMORY(&input, &output, sizeof(output));
    TEST_ASSERT_EQUAL_UINT64(1, sequence);
    TEST_ASSERT_EQUAL_INT64(2, generated_at);
    dashboard_store_deinit(&store);
}

TEST_CASE("initialization cleanup unwinds only completed stages in reverse order",
          "[network_client]")
{
    uint32_t stages = NETWORK_INIT_DEFAULT_EVENT_LOOP | NETWORK_INIT_DEFAULT_STA |
                      NETWORK_INIT_WIFI | NETWORK_INIT_WIFI_STARTED |
                      NETWORK_INIT_WIFI_HANDLER | NETWORK_INIT_IP_HANDLER;
    const network_cleanup_action_t expected[] = {
        NETWORK_CLEANUP_IP_HANDLER,
        NETWORK_CLEANUP_WIFI_HANDLER,
        NETWORK_CLEANUP_WIFI_STOP,
        NETWORK_CLEANUP_DEFAULT_STA,
        NETWORK_CLEANUP_WIFI_DEINIT,
        NETWORK_CLEANUP_DEFAULT_EVENT_LOOP,
        NETWORK_CLEANUP_DONE,
    };

    for (size_t index = 0; index < sizeof(expected) / sizeof(expected[0]); ++index) {
        TEST_ASSERT_EQUAL(expected[index], network_init_cleanup_next(&stages));
    }
    TEST_ASSERT_EQUAL_UINT32(0, stages);

    stages = NETWORK_INIT_DEFAULT_STA | NETWORK_INIT_WIFI;
    TEST_ASSERT_EQUAL(NETWORK_CLEANUP_DEFAULT_STA, network_init_cleanup_next(&stages));
    TEST_ASSERT_EQUAL(NETWORK_CLEANUP_WIFI_DEINIT, network_init_cleanup_next(&stages));
    TEST_ASSERT_EQUAL(NETWORK_CLEANUP_DONE, network_init_cleanup_next(&stages));
}
