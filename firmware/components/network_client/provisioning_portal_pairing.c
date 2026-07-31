#include "provisioning_portal_internal.h"

#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include "esp_log.h"
#include "esp_random.h"

static const char *TAG = "provisioning";

bool provisioning_bearer_token_matches(
    const char *authorization, const char *token)
{
    if (!authorization || !token || strlen(token) != NETCFG_TOKEN_HEX_LENGTH)
        return false;

    static const char prefix[] = "Bearer ";
    size_t token_length = strlen(token);
    size_t expected_length = sizeof(prefix) - 1 + token_length;
    if (strlen(authorization) != expected_length ||
        memcmp(authorization, prefix, sizeof(prefix) - 1) != 0) {
        return false;
    }

    unsigned char difference = 0;
    const char *submitted = authorization + sizeof(prefix) - 1;
    for (size_t index = 0; index < token_length; ++index)
        difference |= (unsigned char)(submitted[index] ^ token[index]);
    return difference == 0;
}

static void generate_pairing_code(char output[PAIRING_CODE_LENGTH + 1])
{
    snprintf(output, PAIRING_CODE_LENGTH + 1, "%06lu",
             (unsigned long)(esp_random() % 1000000U));
}

static void refresh_pairing_code_locked(
    provisioning_portal_t *portal, uint32_t now_ms)
{
    if (!portal->pairing_active) return;
    if (portal->pairing_code[0] &&
        (uint32_t)(now_ms - portal->pairing_code_started_ms) <
            PAIRING_CODE_ROTATION_MS) {
        return;
    }
    if (portal->pairing_code[0]) {
        snprintf(portal->previous_pairing_code,
                 sizeof(portal->previous_pairing_code), "%s",
                 portal->pairing_code);
        portal->previous_pairing_code_valid_until_ms =
            now_ms + PAIRING_CODE_GRACE_MS;
    }
    generate_pairing_code(portal->pairing_code);
    portal->pairing_code_started_ms = now_ms;
}

esp_err_t provisioning_pairing_token_apply(const char *body,
                                            const network_config_t *existing,
                                            network_config_t *output)
{
    if (!body || !existing || !output) return ESP_ERR_INVALID_ARG;
    network_config_t candidate = *existing;
    if (!provisioning_portal_form_value(body, "token", candidate.token, sizeof(candidate.token)))
        return ESP_ERR_INVALID_ARG;
    network_config_normalize_token(&candidate);
    if (network_config_validate(&candidate) != ESP_OK) return ESP_ERR_INVALID_ARG;
    *output = candidate;
    return ESP_OK;
}

static bool fixed_text_equal(const char *left, const char *right, size_t length)
{
    if (!left || !right || strlen(left) != length || strlen(right) != length)
        return false;
    unsigned difference = 0;
    for (size_t index = 0; index < length; ++index)
        difference |= (unsigned)((unsigned char)left[index] ^ (unsigned char)right[index]);
    return difference == 0;
}

bool provisioning_pairing_code_matches(
    const char *submitted, const char *current, const char *previous,
    uint32_t previous_valid_until_ms, uint32_t now_ms)
{
    if (fixed_text_equal(submitted, current, PAIRING_CODE_LENGTH))
        return true;
    return previous && previous[0] &&
           (int32_t)(previous_valid_until_ms - now_ms) > 0 &&
           fixed_text_equal(submitted, previous, PAIRING_CODE_LENGTH);
}

esp_err_t provisioning_pairing_request_apply(
    const char *body, const network_config_t *existing,
    const char *current_code, const char *previous_code,
    uint32_t previous_valid_until_ms, uint32_t now_ms,
    network_config_t *output)
{
    if (!body || !existing || !current_code || !output)
        return ESP_ERR_INVALID_ARG;

    char submitted_code[PAIRING_CODE_LENGTH + 1];
    network_config_t candidate = *existing;
    if (!provisioning_portal_form_value(body, "code", submitted_code, sizeof(submitted_code)) ||
        !provisioning_pairing_code_matches(
            submitted_code, current_code, previous_code,
            previous_valid_until_ms, now_ms) ||
        !provisioning_portal_form_value(body, "host", candidate.host, sizeof(candidate.host)) ||
        !provisioning_portal_form_value(body, "token", candidate.token, sizeof(candidate.token))) {
        return ESP_ERR_INVALID_ARG;
    }

    candidate.port = NETCFG_DEFAULT_PORT;
    network_config_normalize_token(&candidate);
    if (network_config_validate(&candidate) != ESP_OK)
        return ESP_ERR_INVALID_ARG;
    *output = candidate;
    return ESP_OK;
}

esp_err_t provisioning_portal_pairing_post(httpd_req_t *request)
{
    provisioning_portal_touch(g_provisioning_portal);
    if (!provisioning_portal_pairing_active(g_provisioning_portal, provisioning_portal_now_ms()))
        return provisioning_portal_send_json(request, "403 Forbidden", "{\"error\":\"请先通过物理按键授权配对\"}");
    if (request->content_len <= 0 || request->content_len > FORM_BODY_MAX)
        return provisioning_portal_send_json(request, "400 Bad Request", "{\"error\":\"配对内容无效\"}");

    char *body = calloc(1, (size_t)request->content_len + 1);
    if (!body)
        return provisioning_portal_send_json(request, "500 Internal Server Error", "{\"error\":\"内存不足\"}");
    int offset = 0;
    while (offset < request->content_len) {
        int received = httpd_req_recv(request, body + offset, request->content_len - offset);
        if (received <= 0) {
            free(body);
            return ESP_FAIL;
        }
        offset += received;
    }

    char current_code[PAIRING_CODE_LENGTH + 1] = {0};
    char previous_code[PAIRING_CODE_LENGTH + 1] = {0};
    uint32_t previous_valid_until_ms = 0;
    uint32_t now_ms = provisioning_portal_now_ms();
    if (xSemaphoreTake(g_provisioning_portal->lock, portMAX_DELAY) == pdTRUE) {
        refresh_pairing_code_locked(g_provisioning_portal, now_ms);
        snprintf(current_code, sizeof(current_code), "%s", g_provisioning_portal->pairing_code);
        snprintf(previous_code, sizeof(previous_code), "%s",
                 g_provisioning_portal->previous_pairing_code);
        previous_valid_until_ms =
            g_provisioning_portal->previous_pairing_code_valid_until_ms;
        xSemaphoreGive(g_provisioning_portal->lock);
    }

    network_config_t existing = {0};
    network_config_t candidate = {0};
    bool present = false;
    esp_err_t result = config_store_load(g_provisioning_portal->store, &existing, &present);
    if (result == ESP_OK && !present) result = ESP_ERR_NOT_FOUND;
    if (result == ESP_OK)
        result = provisioning_pairing_request_apply(
            body, &existing, current_code, previous_code,
            previous_valid_until_ms, now_ms, &candidate);
    free(body);
    if (result != ESP_OK)
        return provisioning_portal_send_json(request, "400 Bad Request",
                         "{\"error\":\"配对码、PC 地址或设备令牌无效\"}");

    result = config_store_save(g_provisioning_portal->store, &candidate);
    if (result != ESP_OK)
        return provisioning_portal_send_json(request, "500 Internal Server Error", "{\"error\":\"保存配对失败\"}");
    if (xSemaphoreTake(g_provisioning_portal->lock, portMAX_DELAY) == pdTRUE) {
        g_provisioning_portal->saved_config = candidate;
        g_provisioning_portal->pairing_saved = true;
        g_provisioning_portal->pairing_active = false;
        g_provisioning_portal->pairing_code[0] = '\0';
        g_provisioning_portal->previous_pairing_code[0] = '\0';
        xSemaphoreGive(g_provisioning_portal->lock);
    }
    ESP_LOGI(TAG, "device pairing completed");
    return provisioning_portal_send_json(request, "200 OK", "{\"ok\":true}");
}

bool provisioning_portal_take_pairing_saved(provisioning_portal_t *portal,
                                            network_config_t *config)
{
    if (!portal || !config || xSemaphoreTake(portal->lock, portMAX_DELAY) != pdTRUE)
        return false;
    bool saved = portal->pairing_saved;
    if (saved) {
        *config = portal->saved_config;
        portal->pairing_saved = false;
    }
    xSemaphoreGive(portal->lock);
    return saved;
}

esp_err_t provisioning_portal_begin_pairing(provisioning_portal_t *portal,
                                            uint32_t now_ms)
{
    if (!portal || xSemaphoreTake(portal->lock, portMAX_DELAY) != pdTRUE)
        return ESP_ERR_INVALID_ARG;
    if (portal->pairing_active) {
        xSemaphoreGive(portal->lock);
        return ESP_ERR_INVALID_STATE;
    }
    portal->pairing_active = true;
    portal->pairing_code[0] = '\0';
    portal->previous_pairing_code[0] = '\0';
    portal->previous_pairing_code_valid_until_ms = 0;
    refresh_pairing_code_locked(portal, now_ms);
    xSemaphoreGive(portal->lock);
    return ESP_OK;
}

void provisioning_portal_end_pairing(provisioning_portal_t *portal)
{
    if (!portal || xSemaphoreTake(portal->lock, portMAX_DELAY) != pdTRUE) return;
    portal->pairing_active = false;
    portal->pairing_code[0] = '\0';
    portal->previous_pairing_code[0] = '\0';
    xSemaphoreGive(portal->lock);
}

bool provisioning_portal_pairing_active(provisioning_portal_t *portal,
                                        uint32_t now_ms)
{
    (void)now_ms;
    if (!portal || xSemaphoreTake(portal->lock, portMAX_DELAY) != pdTRUE)
        return false;
    bool active = portal->pairing_active;
    xSemaphoreGive(portal->lock);
    return active;
}

uint32_t provisioning_portal_pairing_remaining_seconds(
    provisioning_portal_t *portal, uint32_t now_ms)
{
    if (!portal || xSemaphoreTake(portal->lock, portMAX_DELAY) != pdTRUE)
        return 0;
    refresh_pairing_code_locked(portal, now_ms);
    uint32_t remaining = 0;
    if (portal->pairing_active && portal->pairing_code[0]) {
        uint32_t elapsed = (uint32_t)(now_ms - portal->pairing_code_started_ms);
        uint32_t remaining_ms = PAIRING_CODE_ROTATION_MS - elapsed;
        remaining = (remaining_ms + 999U) / 1000U;
    }
    xSemaphoreGive(portal->lock);
    return remaining;
}

bool provisioning_portal_pairing_code(
    provisioning_portal_t *portal, uint32_t now_ms,
    char output[PAIRING_CODE_LENGTH + 1])
{
    if (!portal || !output ||
        xSemaphoreTake(portal->lock, portMAX_DELAY) != pdTRUE) {
        return false;
    }
    refresh_pairing_code_locked(portal, now_ms);
    bool active = portal->pairing_active && portal->pairing_code[0];
    snprintf(output, PAIRING_CODE_LENGTH + 1, "%s",
             active ? portal->pairing_code : "");
    xSemaphoreGive(portal->lock);
    return active;
}
