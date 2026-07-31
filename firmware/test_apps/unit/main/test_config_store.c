#include <string.h>

#include "config_store.h"
#include "unity.h"

typedef struct {
    network_config_t slots[2];
    bool slot_present[2];
    uint8_t active;
    bool active_present;
    esp_err_t read_slot_result;
    esp_err_t write_slot_result;
    esp_err_t read_active_result;
    esp_err_t write_active_result;
    esp_err_t clear_result;
    unsigned operations;
    uint8_t written_slots[2];
    uint8_t written_active[2];
    unsigned slot_writes;
    unsigned active_writes;
    char write_order[4];
    unsigned write_order_count;
} fake_backend_t;

static network_config_t valid_config(char suffix)
{
    network_config_t config = {0};

    strcpy(config.ssid, "unit-network");
    strcpy(config.host, "192.168.1.2");
    config.port = NETCFG_DEFAULT_PORT;
    memset(config.token, 'a', 64);
    config.token[63] = suffix;
    return config;
}

static esp_err_t fake_read_slot(void *context, uint8_t slot, network_config_t *value, bool *present)
{
    fake_backend_t *fake = context;
    fake->operations++;
    if (fake->read_slot_result != ESP_OK) return fake->read_slot_result;
    *present = fake->slot_present[slot];
    if (*present) *value = fake->slots[slot];
    return ESP_OK;
}

static esp_err_t fake_write_slot(void *context, uint8_t slot, const network_config_t *value)
{
    fake_backend_t *fake = context;
    fake->operations++;
    fake->write_order[fake->write_order_count++] = 'S';
    fake->written_slots[fake->slot_writes++] = slot;
    if (fake->write_slot_result != ESP_OK) return fake->write_slot_result;
    fake->slots[slot] = *value;
    fake->slot_present[slot] = true;
    return ESP_OK;
}

static esp_err_t fake_read_active(void *context, uint8_t *slot, bool *present)
{
    fake_backend_t *fake = context;
    fake->operations++;
    if (fake->read_active_result != ESP_OK) return fake->read_active_result;
    *slot = fake->active;
    *present = fake->active_present;
    return ESP_OK;
}

static esp_err_t fake_write_active(void *context, uint8_t slot)
{
    fake_backend_t *fake = context;
    fake->operations++;
    fake->write_order[fake->write_order_count++] = 'A';
    fake->written_active[fake->active_writes++] = slot;
    if (fake->write_active_result != ESP_OK) return fake->write_active_result;
    fake->active = slot;
    fake->active_present = true;
    return ESP_OK;
}

static esp_err_t fake_clear(void *context)
{
    fake_backend_t *fake = context;
    fake->operations++;
    if (fake->clear_result != ESP_OK) return fake->clear_result;
    fake->active_present = false;
    fake->slot_present[0] = false;
    fake->slot_present[1] = false;
    return ESP_OK;
}

static config_store_t make_store(fake_backend_t *fake)
{
    config_store_t store;
    config_store_backend_t backend = {
        .context = fake,
        .read_slot = fake_read_slot,
        .write_slot = fake_write_slot,
        .read_active = fake_read_active,
        .write_active = fake_write_active,
        .clear = fake_clear,
    };

    TEST_ASSERT_EQUAL(ESP_OK, config_store_init(&store, backend));
    return store;
}

static void assert_loaded(config_store_t *store, const network_config_t *expected)
{
    network_config_t loaded = {0};
    bool present = false;

    TEST_ASSERT_EQUAL(ESP_OK, config_store_load(store, &loaded, &present));
    TEST_ASSERT_TRUE(present);
    TEST_ASSERT_EQUAL_MEMORY(expected, &loaded, sizeof(loaded));
}

TEST_CASE("config store writes inactive slots before their active markers", "[config_store]")
{
    fake_backend_t fake = {0};
    config_store_t store = make_store(&fake);
    network_config_t first = valid_config('1');
    network_config_t second = valid_config('2');

    TEST_ASSERT_EQUAL(ESP_OK, config_store_save(&store, &first));
    TEST_ASSERT_EQUAL_UINT8(0, fake.written_slots[0]);
    TEST_ASSERT_EQUAL_UINT8(0, fake.written_active[0]);
    TEST_ASSERT_EQUAL_CHAR('S', fake.write_order[0]);
    TEST_ASSERT_EQUAL_CHAR('A', fake.write_order[1]);
    assert_loaded(&store, &first);

    TEST_ASSERT_EQUAL(ESP_OK, config_store_save(&store, &second));
    TEST_ASSERT_EQUAL_UINT8(1, fake.written_slots[1]);
    TEST_ASSERT_EQUAL_UINT8(1, fake.written_active[1]);
    TEST_ASSERT_EQUAL_CHAR('S', fake.write_order[2]);
    TEST_ASSERT_EQUAL_CHAR('A', fake.write_order[3]);
    assert_loaded(&store, &second);
}

TEST_CASE("config store preserves old active value after write failures", "[config_store]")
{
    fake_backend_t fake = {0};
    config_store_t store = make_store(&fake);
    network_config_t old_value = valid_config('1');
    network_config_t new_value = valid_config('2');

    TEST_ASSERT_EQUAL(ESP_OK, config_store_save(&store, &old_value));
    fake.write_slot_result = ESP_FAIL;
    TEST_ASSERT_EQUAL(ESP_FAIL, config_store_save(&store, &new_value));
    assert_loaded(&store, &old_value);

    fake.write_slot_result = ESP_OK;
    fake.write_active_result = ESP_FAIL;
    TEST_ASSERT_EQUAL(ESP_FAIL, config_store_save(&store, &new_value));
    assert_loaded(&store, &old_value);
}

TEST_CASE("config store rejects invalid input without backend operations", "[config_store]")
{
    fake_backend_t fake = {0};
    config_store_t store = make_store(&fake);
    network_config_t invalid = valid_config('1');
    unsigned operations = fake.operations;

    invalid.port = 0;
    TEST_ASSERT_EQUAL(ESP_ERR_INVALID_ARG, config_store_save(&store, &invalid));
    TEST_ASSERT_EQUAL_UINT(operations, fake.operations);
}
