#include <string.h>

#include "serial_setup.h"
#include "unity.h"

static network_config_t summary_config(void)
{
    network_config_t config = {0};

    strcpy(config.ssid, "Solis network");
    strcpy(config.password, "password must remain hidden");
    strcpy(config.host, "192.168.1.2");
    config.port = 18472;
    strcpy(config.token, "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef");
    return config;
}

TEST_CASE("serial setup trims LF and CRLF without changing credential spaces", "[serial_setup]")
{
    char lf[] = "  password with spaces  \n";
    char crlf[] = "  token with spaces  \r\n";

    TEST_ASSERT_EQUAL_UINT(strlen("  password with spaces  "), serial_setup_trim_line(lf));
    TEST_ASSERT_EQUAL_STRING("  password with spaces  ", lf);
    TEST_ASSERT_EQUAL_UINT(strlen("  token with spaces  "), serial_setup_trim_line(crlf));
    TEST_ASSERT_EQUAL_STRING("  token with spaces  ", crlf);
}

TEST_CASE("serial setup formats absent configuration without secrets", "[serial_setup]")
{
    char output[128] = {0};

    TEST_ASSERT_EQUAL(ESP_OK, serial_setup_format_summary(NULL, false, output, sizeof(output)));
    TEST_ASSERT_EQUAL_STRING("not configured", output);
}

TEST_CASE("serial setup summary redacts password and token prefix", "[serial_setup]")
{
    network_config_t config = summary_config();
    char output[256] = {0};

    TEST_ASSERT_EQUAL(ESP_OK, serial_setup_format_summary(&config, true, output, sizeof(output)));
    TEST_ASSERT_NOT_NULL(strstr(output, "Solis network"));
    TEST_ASSERT_NOT_NULL(strstr(output, "192.168.1.2"));
    TEST_ASSERT_NOT_NULL(strstr(output, "18472"));
    TEST_ASSERT_NOT_NULL(strstr(output, "token=****cdef"));
    TEST_ASSERT_NULL(strstr(output, config.password));
    TEST_ASSERT_NULL(strstr(output, config.token));
    TEST_ASSERT_NULL(strstr(output, "0123456789abcdef0123456789abcdef0123456789abcdef0123456789"));
}
