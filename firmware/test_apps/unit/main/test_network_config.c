#include <string.h>

#include "network_config.h"
#include "unity.h"

static void fill_chars(char *destination, size_t count, char character)
{
    memset(destination, character, count);
    destination[count] = '\0';
}

static network_config_t valid_config(void)
{
    network_config_t config = {0};

    strcpy(config.ssid, "unit-network");
    strcpy(config.host, "192.168.1.2");
    config.port = NETCFG_DEFAULT_PORT;
    fill_chars(config.token, 64, 'a');
    return config;
}

TEST_CASE("network config accepts documented field boundaries", "[network_config]")
{
    network_config_t config = valid_config();

    fill_chars(config.ssid, 32, 's');
    fill_chars(config.password, 64, 'p');
    config.port = 1;
    TEST_ASSERT_EQUAL(ESP_OK, network_config_validate(&config));

    config.port = UINT16_MAX;
    TEST_ASSERT_EQUAL(ESP_OK, network_config_validate(&config));
}

TEST_CASE("network config rejects unterminated or required invalid fields", "[network_config]")
{
    network_config_t config = valid_config();

    memset(config.ssid, 's', sizeof(config.ssid));
    TEST_ASSERT_EQUAL(ESP_ERR_INVALID_ARG, network_config_validate(&config));

    config = valid_config();
    memset(config.password, 'p', sizeof(config.password));
    TEST_ASSERT_EQUAL(ESP_ERR_INVALID_ARG, network_config_validate(&config));

    config = valid_config();
    config.ssid[0] = '\0';
    TEST_ASSERT_EQUAL(ESP_ERR_INVALID_ARG, network_config_validate(&config));

    config = valid_config();
    config.port = 0;
    TEST_ASSERT_EQUAL(ESP_ERR_INVALID_ARG, network_config_validate(&config));
}

TEST_CASE("network config accepts only IPv4 hosts", "[network_config]")
{
    network_config_t config = valid_config();

    TEST_ASSERT_EQUAL(ESP_OK, network_config_validate(&config));

    strcpy(config.host, "server.local");
    TEST_ASSERT_EQUAL(ESP_ERR_INVALID_ARG, network_config_validate(&config));

    strcpy(config.host, "::1");
    TEST_ASSERT_EQUAL(ESP_ERR_INVALID_ARG, network_config_validate(&config));
}

TEST_CASE("network config validates and lowercases a 64 character hex token", "[network_config]")
{
    network_config_t config = valid_config();

    for (size_t index = 0; index < 64; ++index) config.token[index] = index % 2 ? 'B' : 'A';
    TEST_ASSERT_EQUAL(ESP_OK, network_config_validate(&config));
    network_config_normalize_token(&config);
    for (size_t index = 0; index < 64; ++index) TEST_ASSERT_TRUE(config.token[index] == 'a' || config.token[index] == 'b');

    config = valid_config();
    config.token[0] = 'g';
    TEST_ASSERT_EQUAL(ESP_ERR_INVALID_ARG, network_config_validate(&config));

    config = valid_config();
    config.token[63] = '\0';
    TEST_ASSERT_EQUAL(ESP_ERR_INVALID_ARG, network_config_validate(&config));

    config = valid_config();
    memset(config.token, 'a', sizeof(config.token));
    TEST_ASSERT_EQUAL(ESP_ERR_INVALID_ARG, network_config_validate(&config));
}

TEST_CASE("network config accepts an empty token while waiting for physical pairing",
          "[network_config]")
{
    network_config_t config = valid_config();

    config.token[0] = '\0';
    TEST_ASSERT_EQUAL(ESP_OK, network_config_validate(&config));
}

TEST_CASE("network config accepts Wi-Fi before PC pairing", "[network_config]")
{
    network_config_t config = valid_config();

    config.host[0] = '\0';
    config.token[0] = '\0';
    TEST_ASSERT_EQUAL(ESP_OK, network_config_validate(&config));

    strcpy(config.token,
           "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef");
    TEST_ASSERT_EQUAL(ESP_ERR_INVALID_ARG, network_config_validate(&config));
}
