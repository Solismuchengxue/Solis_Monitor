#include "network_config.h"

#include <stdbool.h>
#include <stddef.h>
#include <string.h>

#include "lwip/inet.h"
#include "lwip/sockets.h"

static bool is_hex(char character)
{
    return (character >= '0' && character <= '9') ||
           (character >= 'a' && character <= 'f') ||
           (character >= 'A' && character <= 'F');
}

esp_err_t network_config_validate(const network_config_t *config)
{
    struct in_addr address;
    size_t host_length;
    size_t token_length;

    if (!config) return ESP_ERR_INVALID_ARG;
    if (strnlen(config->ssid, sizeof(config->ssid)) == 0 ||
        strnlen(config->ssid, sizeof(config->ssid)) == sizeof(config->ssid) ||
        strnlen(config->password, sizeof(config->password)) == sizeof(config->password) ||
        strnlen(config->host, sizeof(config->host)) == sizeof(config->host) ||
        config->port == 0) {
        return ESP_ERR_INVALID_ARG;
    }

    host_length = strnlen(config->host, sizeof(config->host));
    token_length = strnlen(config->token, sizeof(config->token));
    if (token_length != 0 && token_length != sizeof(config->token) - 1)
        return ESP_ERR_INVALID_ARG;
    for (size_t index = 0; index < token_length; ++index) {
        if (!is_hex(config->token[index])) return ESP_ERR_INVALID_ARG;
    }
    if (host_length == 0)
        return token_length == 0 ? ESP_OK : ESP_ERR_INVALID_ARG;
    if (inet_pton(AF_INET, config->host, &address) != 1)
        return ESP_ERR_INVALID_ARG;
    return ESP_OK;
}

void network_config_normalize_token(network_config_t *config)
{
    if (!config) return;
    for (size_t index = 0; index < sizeof(config->token) - 1; ++index) {
        if (config->token[index] >= 'A' && config->token[index] <= 'F') {
            config->token[index] = (char)(config->token[index] - 'A' + 'a');
        }
    }
}
