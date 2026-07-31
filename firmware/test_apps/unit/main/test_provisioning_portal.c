#include <string.h>

#include "provisioning_portal.h"
#include "unity.h"

static const char *TOKEN =
    "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

TEST_CASE("provisioning form decodes and validates a new configuration", "[provisioning]")
{
    network_config_t output;

    TEST_ASSERT_EQUAL(ESP_OK,
        provisioning_form_parse(
            "ssid=Solis+WiFi&password=p%40ssword",
            NULL, false, &output));
    TEST_ASSERT_EQUAL_STRING("Solis WiFi", output.ssid);
    TEST_ASSERT_EQUAL_STRING("p@ssword", output.password);
    TEST_ASSERT_EQUAL_STRING("", output.host);
    TEST_ASSERT_EQUAL_UINT16(18472, output.port);
    TEST_ASSERT_EQUAL_STRING("", output.token);
}

TEST_CASE("blank secrets preserve an existing provisioning configuration", "[provisioning]")
{
    network_config_t existing = {
        .ssid = "Old", .password = "secret", .host = "192.168.0.27", .port = 18472,
    };
    snprintf(existing.token, sizeof(existing.token), "%s", TOKEN);
    network_config_t output;

    TEST_ASSERT_EQUAL(ESP_OK,
        provisioning_form_parse("ssid=New&password=",
                                &existing, true, &output));
    TEST_ASSERT_EQUAL_STRING("New", output.ssid);
    TEST_ASSERT_EQUAL_STRING("secret", output.password);
    TEST_ASSERT_EQUAL_STRING("192.168.0.27", output.host);
    TEST_ASSERT_EQUAL_STRING(TOKEN, output.token);
}

TEST_CASE("provisioning form requires only Wi-Fi fields", "[provisioning]")
{
    network_config_t output;
    TEST_ASSERT_EQUAL(ESP_ERR_INVALID_ARG,
        provisioning_form_parse("ssid=WiFi", NULL, false, &output));
    TEST_ASSERT_EQUAL(ESP_ERR_INVALID_ARG,
        provisioning_form_parse("password=x", NULL, false, &output));
}

TEST_CASE("restore defaults requires an explicit confirmation", "[provisioning]")
{
    TEST_ASSERT_TRUE(provisioning_reset_confirmed("confirm=restore"));
    TEST_ASSERT_FALSE(provisioning_reset_confirmed(""));
    TEST_ASSERT_FALSE(provisioning_reset_confirmed("confirm=yes"));
    TEST_ASSERT_FALSE(provisioning_reset_confirmed("other=restore"));
}

TEST_CASE("device display settings require complete bounded values", "[provisioning]")
{
    device_control_settings_t settings;

    TEST_ASSERT_EQUAL(
        ESP_OK,
        provisioning_device_control_parse(
            "brightness=75&night_enabled=1&night_start=1410&"
            "night_end=450&utc_offset=480",
            &settings));
    TEST_ASSERT_EQUAL_UINT8(75, settings.brightness_percent);
    TEST_ASSERT_TRUE(settings.night_enabled);
    TEST_ASSERT_EQUAL_UINT16(1410, settings.night_start_minute);
    TEST_ASSERT_EQUAL_UINT16(450, settings.night_end_minute);
    TEST_ASSERT_EQUAL_INT16(480, settings.utc_offset_minutes);

    TEST_ASSERT_EQUAL(
        ESP_ERR_INVALID_ARG,
        provisioning_device_control_parse(
            "brightness=0&night_enabled=1&night_start=1410&"
            "night_end=450&utc_offset=480",
            &settings));
    TEST_ASSERT_EQUAL(
        ESP_ERR_INVALID_ARG,
        provisioning_device_control_parse(
            "brightness=75&night_enabled=1&night_start=1410&"
            "night_end=1410&utc_offset=480",
            &settings));
}

TEST_CASE("OTA bearer authorization requires the exact paired token", "[provisioning]")
{
    char authorization[80];
    snprintf(authorization, sizeof(authorization), "Bearer %s", TOKEN);

    TEST_ASSERT_TRUE(
        provisioning_bearer_token_matches(authorization, TOKEN));
    TEST_ASSERT_FALSE(
        provisioning_bearer_token_matches("Bearer wrong", TOKEN));
    TEST_ASSERT_FALSE(
        provisioning_bearer_token_matches(TOKEN, TOKEN));
    TEST_ASSERT_FALSE(
        provisioning_bearer_token_matches(authorization, ""));
}

TEST_CASE("device identity JSON exposes discovery fields without secrets", "[provisioning]")
{
    char json[256];

    TEST_ASSERT_TRUE(provisioning_device_info_format(
        json, sizeof(json), "Solis_Monitor_A1B2", "1.0.0", "192.168.0.42",
        -57, true, true, true));
    TEST_ASSERT_EQUAL_STRING(
        "{\"product\":\"Solis Monitor\",\"hostname\":\"Solis_Monitor_A1B2\","
        "\"firmware\":\"1.0.0\",\"ip\":\"192.168.0.42\",\"rssi\":-57,"
        "\"paired\":true,\"pairing\":true}",
        json);
    TEST_ASSERT_NULL(strstr(json, "token"));
    TEST_ASSERT_FALSE(provisioning_device_info_format(
        json, 8, "Solis_Monitor_A1B2", "1.0.0", "192.168.0.42",
        -57, true, true, true));
}

TEST_CASE("physical pairing only replaces the device token", "[provisioning]")
{
    static const char *NEW_TOKEN =
        "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789";
    network_config_t existing = {
        .ssid = "Solis WiFi",
        .password = "secret",
        .host = "192.168.0.27",
        .port = 18472,
    };
    snprintf(existing.token, sizeof(existing.token), "%s", TOKEN);
    char body[96];
    snprintf(body, sizeof(body), "token=%s", NEW_TOKEN);
    network_config_t output = {0};

    TEST_ASSERT_EQUAL(
        ESP_OK,
        provisioning_pairing_token_apply(body, &existing, &output));
    TEST_ASSERT_EQUAL_STRING(existing.ssid, output.ssid);
    TEST_ASSERT_EQUAL_STRING(existing.password, output.password);
    TEST_ASSERT_EQUAL_STRING(existing.host, output.host);
    TEST_ASSERT_EQUAL_UINT16(existing.port, output.port);
    TEST_ASSERT_EQUAL_STRING(NEW_TOKEN, output.token);
    TEST_ASSERT_EQUAL(
        ESP_ERR_INVALID_ARG,
        provisioning_pairing_token_apply("token=bad", &existing, &output));
}

TEST_CASE("pairing request accepts current and briefly previous code", "[provisioning]")
{
    static const char *NEW_TOKEN =
        "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789";
    network_config_t existing = {
        .ssid = "Solis WiFi",
        .password = "secret",
        .host = "192.168.0.27",
        .port = 18472,
    };
    snprintf(existing.token, sizeof(existing.token), "%s", TOKEN);
    char body[160];
    network_config_t output = {0};

    snprintf(body, sizeof(body), "code=123456&host=192.168.0.88&token=%s", NEW_TOKEN);
    TEST_ASSERT_EQUAL(
        ESP_OK,
        provisioning_pairing_request_apply(
            body, &existing, "123456", "654321", 70000, 65000, &output));
    TEST_ASSERT_EQUAL_STRING("Solis WiFi", output.ssid);
    TEST_ASSERT_EQUAL_STRING("secret", output.password);
    TEST_ASSERT_EQUAL_STRING("192.168.0.88", output.host);
    TEST_ASSERT_EQUAL_UINT16(18472, output.port);
    TEST_ASSERT_EQUAL_STRING(NEW_TOKEN, output.token);

    snprintf(body, sizeof(body), "code=654321&host=192.168.0.89&token=%s", NEW_TOKEN);
    TEST_ASSERT_EQUAL(
        ESP_OK,
        provisioning_pairing_request_apply(
            body, &existing, "123456", "654321", 70000, 69999, &output));
    TEST_ASSERT_EQUAL_STRING("192.168.0.89", output.host);
}

TEST_CASE("pairing request rejects expired or incorrect code", "[provisioning]")
{
    static const char *NEW_TOKEN =
        "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789";
    network_config_t existing = {
        .ssid = "Solis WiFi",
        .password = "secret",
        .host = "192.168.0.27",
        .port = 18472,
    };
    snprintf(existing.token, sizeof(existing.token), "%s", TOKEN);
    char body[160];
    network_config_t output = {0};

    snprintf(body, sizeof(body), "code=654321&host=192.168.0.88&token=%s", NEW_TOKEN);
    TEST_ASSERT_EQUAL(
        ESP_ERR_INVALID_ARG,
        provisioning_pairing_request_apply(
            body, &existing, "123456", "654321", 70000, 70000, &output));

    snprintf(body, sizeof(body), "code=111111&host=192.168.0.88&token=%s", NEW_TOKEN);
    TEST_ASSERT_EQUAL(
        ESP_ERR_INVALID_ARG,
        provisioning_pairing_request_apply(
            body, &existing, "123456", "654321", 70000, 65000, &output));
}
