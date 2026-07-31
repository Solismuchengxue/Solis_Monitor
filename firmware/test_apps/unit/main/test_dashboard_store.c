#include <math.h>
#include <stdio.h>

#include "dashboard_store.h"
#include "unity.h"

TEST_CASE("dashboard store preserves state when source status changes", "[dashboard_store]")
{
    dashboard_store_t store = {0};
    dashboard_state_t input = {
        .source_online = true,
        .system = {.cpu_usage = 42, .time = "12:34"},
        .codex = {.online = true, .project = "Solis_Monitor"},
        .environment = {.humidity = 61, .location = "大连"},
    };
    dashboard_state_t before;
    dashboard_state_t after;
    uint64_t sequence;
    int64_t generated_at;

    TEST_ASSERT_EQUAL(ESP_OK, dashboard_store_init(&store));
    TEST_ASSERT_TRUE(dashboard_store_replace(&store, &input, 12, 34));
    TEST_ASSERT_TRUE(dashboard_store_snapshot(&store, &before, &sequence, &generated_at));
    TEST_ASSERT_EQUAL_UINT64(12, sequence);
    TEST_ASSERT_EQUAL_INT64(34, generated_at);

    TEST_ASSERT_TRUE(dashboard_store_set_source_online(&store, false));
    TEST_ASSERT_TRUE(dashboard_store_snapshot(&store, &after, &sequence, &generated_at));
    before.source_online = false;
    TEST_ASSERT_EQUAL_MEMORY(&before, &after, sizeof(after));
    TEST_ASSERT_EQUAL_UINT64(12, sequence);
    TEST_ASSERT_EQUAL_INT64(34, generated_at);
    dashboard_store_deinit(&store);
}

TEST_CASE("dashboard store keeps local DHT11 readings across PC snapshots", "[dashboard_store]")
{
    dashboard_store_t store = {0};
    dashboard_state_t pc_state;
    dashboard_state_t snapshot;
    uint64_t sequence;
    int64_t generated_at;

    dashboard_state_init_empty(&pc_state);
    pc_state.source_online = true;
    pc_state.environment.indoor_temp_c = 99;
    pc_state.environment.humidity = 99;

    TEST_ASSERT_EQUAL(ESP_OK, dashboard_store_init(&store));
    TEST_ASSERT_TRUE(dashboard_store_set_local_environment(&store, 25, 56));
    TEST_ASSERT_TRUE(dashboard_store_replace(&store, &pc_state, 1, 2));
    TEST_ASSERT_TRUE(dashboard_store_snapshot(&store, &snapshot, &sequence, &generated_at));
    TEST_ASSERT_EQUAL_FLOAT(25, snapshot.environment.indoor_temp_c);
    TEST_ASSERT_EQUAL_FLOAT(56, snapshot.environment.humidity);

    TEST_ASSERT_TRUE(dashboard_store_set_local_environment(&store, NAN, NAN));
    TEST_ASSERT_TRUE(dashboard_store_snapshot(&store, &snapshot, &sequence, &generated_at));
    TEST_ASSERT_TRUE(isnan(snapshot.environment.indoor_temp_c));
    TEST_ASSERT_TRUE(isnan(snapshot.environment.humidity));

    TEST_ASSERT_TRUE(dashboard_store_set_local_environment(&store, 26, 57));
    TEST_ASSERT_TRUE(dashboard_store_snapshot(&store, &snapshot, &sequence, &generated_at));
    TEST_ASSERT_EQUAL_FLOAT(26, snapshot.environment.indoor_temp_c);
    TEST_ASSERT_EQUAL_FLOAT(57, snapshot.environment.humidity);
    dashboard_store_deinit(&store);
}

TEST_CASE("dashboard store keeps local Wi-Fi status across PC snapshots", "[dashboard_store]")
{
    dashboard_store_t store = {0};
    dashboard_state_t pc_state;
    dashboard_state_t snapshot;
    uint64_t sequence;
    int64_t generated_at;

    dashboard_state_init_empty(&pc_state);
    pc_state.wifi_connected = false;
    snprintf(pc_state.wifi_ssid, sizeof(pc_state.wifi_ssid), "wrong");

    TEST_ASSERT_EQUAL(ESP_OK, dashboard_store_init(&store));
    TEST_ASSERT_TRUE(dashboard_store_set_wifi_state(&store, true, "Solis-WiFi"));
    TEST_ASSERT_TRUE(dashboard_store_replace(&store, &pc_state, 3, 4));
    TEST_ASSERT_TRUE(dashboard_store_snapshot(&store, &snapshot, &sequence, &generated_at));
    TEST_ASSERT_TRUE(snapshot.wifi_connected);
    TEST_ASSERT_EQUAL_STRING("Solis-WiFi", snapshot.wifi_ssid);

    TEST_ASSERT_TRUE(dashboard_store_set_wifi_state(&store, false, NULL));
    TEST_ASSERT_TRUE(dashboard_store_snapshot(&store, &snapshot, &sequence, &generated_at));
    TEST_ASSERT_FALSE(snapshot.wifi_connected);
    TEST_ASSERT_EQUAL_STRING("", snapshot.wifi_ssid);
    dashboard_store_deinit(&store);
}

TEST_CASE("dashboard store keeps provisioning status across PC snapshots", "[dashboard_store]")
{
    dashboard_store_t store = {0};
    dashboard_state_t pc_state;
    dashboard_state_t snapshot;
    uint64_t sequence;
    int64_t generated_at;

    dashboard_state_init_empty(&pc_state);
    TEST_ASSERT_EQUAL(ESP_OK, dashboard_store_init(&store));
    TEST_ASSERT_TRUE(dashboard_store_set_provisioning(&store, true, "Solis-Monitor-1234", 599));
    TEST_ASSERT_TRUE(dashboard_store_replace(&store, &pc_state, 5, 6));
    TEST_ASSERT_TRUE(dashboard_store_snapshot(&store, &snapshot, &sequence, &generated_at));
    TEST_ASSERT_TRUE(snapshot.provisioning_active);
    TEST_ASSERT_EQUAL_STRING("Solis-Monitor-1234", snapshot.provisioning_ssid);
    TEST_ASSERT_EQUAL_UINT32(599, snapshot.provisioning_remaining_seconds);
    dashboard_store_deinit(&store);
}

TEST_CASE("dashboard store keeps discovery state across PC snapshots",
          "[dashboard_store]")
{
    dashboard_store_t store = {0};
    dashboard_state_t pc_state;
    dashboard_state_t snapshot;
    uint64_t sequence;
    int64_t generated_at;

    dashboard_state_init_empty(&pc_state);
    TEST_ASSERT_EQUAL(ESP_OK, dashboard_store_init(&store));
    TEST_ASSERT_TRUE(
        dashboard_store_set_pairing(&store, true, "123456", 42, false));
    TEST_ASSERT_TRUE(dashboard_store_replace(&store, &pc_state, 7, 8));
    TEST_ASSERT_TRUE(dashboard_store_snapshot(&store, &snapshot, &sequence, &generated_at));
    TEST_ASSERT_TRUE(snapshot.discovery_active);
    TEST_ASSERT_FALSE(snapshot.pairing_completed);
    TEST_ASSERT_EQUAL_STRING("123456", snapshot.pairing_code);
    TEST_ASSERT_EQUAL_UINT32(42, snapshot.pairing_code_remaining_seconds);

    TEST_ASSERT_TRUE(
        dashboard_store_set_pairing(&store, false, NULL, 0, true));
    TEST_ASSERT_TRUE(dashboard_store_snapshot(&store, &snapshot, &sequence, &generated_at));
    TEST_ASSERT_FALSE(snapshot.discovery_active);
    TEST_ASSERT_TRUE(snapshot.pairing_completed);
    TEST_ASSERT_EQUAL_STRING("", snapshot.pairing_code);
    TEST_ASSERT_EQUAL_UINT32(0, snapshot.pairing_code_remaining_seconds);
    dashboard_store_deinit(&store);
}
