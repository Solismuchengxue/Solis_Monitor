#include "provisioning_portal_internal.h"

#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include "esp_log.h"
#include "esp_mac.h"
#include "esp_wifi.h"

#define SCAN_RESULT_MAX 20

extern const uint8_t portal_html_gz_start[] asm("_binary_portal_html_gz_start");
extern const uint8_t portal_html_gz_end[] asm("_binary_portal_html_gz_end");

static const char *TAG = "provisioning";
provisioning_portal_t *g_provisioning_portal;

uint32_t provisioning_portal_now_ms(void)
{
    return (uint32_t)(xTaskGetTickCount() * portTICK_PERIOD_MS);
}

void provisioning_portal_touch(provisioning_portal_t *portal)
{
    if (portal) portal->last_activity_ms = provisioning_portal_now_ms();
}

static int hex_value(char value)
{
    if (value >= '0' && value <= '9') return value - '0';
    if (value >= 'a' && value <= 'f') return value - 'a' + 10;
    if (value >= 'A' && value <= 'F') return value - 'A' + 10;
    return -1;
}

static bool decode_value(const char *begin, const char *end, char *output, size_t output_size)
{
    size_t written = 0;
    while (begin < end) {
        unsigned char value = (unsigned char)*begin++;
        if (value == '+') value = ' ';
        else if (value == '%') {
            if (end - begin < 2) return false;
            int high = hex_value(begin[0]);
            int low = hex_value(begin[1]);
            if (high < 0 || low < 0) return false;
            value = (unsigned char)((high << 4) | low);
            begin += 2;
        }
        if (value == 0 || written + 1 >= output_size) return false;
        output[written++] = (char)value;
    }
    output[written] = '\0';
    return true;
}

bool provisioning_portal_form_value(const char *body, const char *key, char *output, size_t output_size)
{
    size_t key_length = strlen(key);
    const char *cursor = body;
    while (*cursor) {
        const char *pair_end = strchr(cursor, '&');
        if (!pair_end) pair_end = cursor + strlen(cursor);
        const char *equals = memchr(cursor, '=', (size_t)(pair_end - cursor));
        if (equals && (size_t)(equals - cursor) == key_length &&
            memcmp(cursor, key, key_length) == 0)
            return decode_value(equals + 1, pair_end, output, output_size);
        cursor = *pair_end ? pair_end + 1 : pair_end;
    }
    return false;
}

esp_err_t provisioning_form_parse(const char *body, const network_config_t *existing,
                                  bool has_existing, network_config_t *output)
{
    if (!body || !output || (has_existing && !existing)) return ESP_ERR_INVALID_ARG;
    network_config_t result = has_existing ? *existing : (network_config_t){.port = NETCFG_DEFAULT_PORT};
    char password[sizeof(result.password)];
    if (!provisioning_portal_form_value(body, "ssid", result.ssid, sizeof(result.ssid)) ||
        !provisioning_portal_form_value(body, "password", password, sizeof(password)))
        return ESP_ERR_INVALID_ARG;

    if (password[0] || !has_existing)
        snprintf(result.password, sizeof(result.password), "%s", password);
    network_config_normalize_token(&result);
    if (network_config_validate(&result) != ESP_OK) return ESP_ERR_INVALID_ARG;
    *output = result;
    return ESP_OK;
}

bool provisioning_reset_confirmed(const char *body)
{
    char confirmation[16];
    return body && provisioning_portal_form_value(body, "confirm", confirmation, sizeof(confirmation)) &&
           strcmp(confirmation, "restore") == 0;
}

esp_err_t provisioning_portal_send_json(httpd_req_t *request, const char *status, const char *body)
{
    httpd_resp_set_status(request, status);
    httpd_resp_set_type(request, "application/json; charset=utf-8");
    httpd_resp_set_hdr(request, "Cache-Control", "no-store");
    return httpd_resp_sendstr(request, body);
}

static esp_err_t root_get(httpd_req_t *request)
{
    provisioning_portal_touch(g_provisioning_portal);
    ESP_LOGI(TAG, "serving portal root");
    httpd_resp_set_type(request, "text/html; charset=utf-8");
    httpd_resp_set_hdr(request, "Content-Encoding", "gzip");
    httpd_resp_set_hdr(request, "Cache-Control", "no-store");
    return httpd_resp_send(request, (const char *)portal_html_gz_start,
                           portal_html_gz_end - portal_html_gz_start);
}

static void provisioning_portal_send_json_escaped(httpd_req_t *request, const char *text);

static esp_err_t config_get(httpd_req_t *request)
{
    provisioning_portal_touch(g_provisioning_portal);
    network_config_t config = {0};
    bool present = false;
    if (config_store_load(g_provisioning_portal->store, &config, &present) != ESP_OK)
        return provisioning_portal_send_json(request, "500 Internal Server Error", "{\"error\":\"读取配置失败\"}");
    httpd_resp_set_type(request, "application/json; charset=utf-8");
    httpd_resp_set_hdr(request, "Cache-Control", "no-store");
    httpd_resp_sendstr_chunk(request, "{\"ssid\":\"");
    provisioning_portal_send_json_escaped(request, present ? config.ssid : "");
    char suffix[64];
    snprintf(suffix, sizeof(suffix),
             "\",\"has_password\":%s}",
             present && config.password[0] ? "true" : "false");
    httpd_resp_sendstr_chunk(request, suffix);
    return httpd_resp_sendstr_chunk(request, NULL);
}

static void provisioning_portal_send_json_escaped(httpd_req_t *request, const char *text)
{
    char part[8];
    for (const unsigned char *cursor = (const unsigned char *)text; *cursor; ++cursor) {
        if (*cursor == '"' || *cursor == '\\') {
            part[0] = '\\'; part[1] = (char)*cursor; part[2] = 0;
        } else if (*cursor < 0x20) {
            snprintf(part, sizeof(part), "\\u%04x", *cursor);
        } else {
            part[0] = (char)*cursor; part[1] = 0;
        }
        httpd_resp_sendstr_chunk(request, part);
    }
}

static esp_err_t scan_get(httpd_req_t *request)
{
    provisioning_portal_touch(g_provisioning_portal);
    wifi_scan_config_t scan = {.show_hidden = false};
    esp_err_t result = esp_wifi_scan_start(&scan, true);
    if (result != ESP_OK)
        return provisioning_portal_send_json(request, "503 Service Unavailable", "{\"error\":\"扫描失败\"}");
    uint16_t count = SCAN_RESULT_MAX;
    wifi_ap_record_t records[SCAN_RESULT_MAX] = {0};
    result = esp_wifi_scan_get_ap_records(&count, records);
    if (result != ESP_OK)
        return provisioning_portal_send_json(request, "503 Service Unavailable", "{\"error\":\"扫描失败\"}");

    httpd_resp_set_type(request, "application/json; charset=utf-8");
    httpd_resp_set_hdr(request, "Cache-Control", "no-store");
    httpd_resp_sendstr_chunk(request, "{\"networks\":[");
    for (uint16_t index = 0; index < count; ++index) {
        if (index) httpd_resp_sendstr_chunk(request, ",");
        httpd_resp_sendstr_chunk(request, "{\"ssid\":\"");
        provisioning_portal_send_json_escaped(request, (const char *)records[index].ssid);
        char suffix[40];
        snprintf(suffix, sizeof(suffix), "\",\"rssi\":%d,\"secure\":%s}", records[index].rssi,
                 records[index].authmode == WIFI_AUTH_OPEN ? "false" : "true");
        httpd_resp_sendstr_chunk(request, suffix);
    }
    httpd_resp_sendstr_chunk(request, "]}");
    return httpd_resp_sendstr_chunk(request, NULL);
}

static esp_err_t config_post(httpd_req_t *request)
{
    provisioning_portal_touch(g_provisioning_portal);
    if (request->content_len <= 0 || request->content_len > FORM_BODY_MAX)
        return provisioning_portal_send_json(request, "400 Bad Request", "{\"error\":\"配置内容无效\"}");
    char *body = calloc(1, (size_t)request->content_len + 1);
    if (!body) return provisioning_portal_send_json(request, "500 Internal Server Error", "{\"error\":\"内存不足\"}");
    int offset = 0;
    while (offset < request->content_len) {
        int received = httpd_req_recv(request, body + offset, request->content_len - offset);
        if (received <= 0) { free(body); return ESP_FAIL; }
        offset += received;
    }

    network_config_t existing = {0};
    network_config_t candidate;
    bool present = false;
    esp_err_t result = config_store_load(g_provisioning_portal->store, &existing, &present);
    if (result == ESP_OK) result = provisioning_form_parse(body, &existing, present, &candidate);
    free(body);
    if (result != ESP_OK)
        return provisioning_portal_send_json(request, "400 Bad Request", "{\"error\":\"请检查 Wi-Fi 名称和密码\"}");
    result = config_store_save(g_provisioning_portal->store, &candidate);
    if (result != ESP_OK)
        return provisioning_portal_send_json(request, "500 Internal Server Error", "{\"error\":\"保存失败\"}");
    if (xSemaphoreTake(g_provisioning_portal->lock, portMAX_DELAY) == pdTRUE) {
        g_provisioning_portal->saved_config = candidate;
        g_provisioning_portal->saved = true;
        g_provisioning_portal->pairing_active = false;
        g_provisioning_portal->pairing_code[0] = '\0';
        g_provisioning_portal->previous_pairing_code[0] = '\0';
        xSemaphoreGive(g_provisioning_portal->lock);
    }
    return provisioning_portal_send_json(request, "200 OK", "{\"ok\":true}");
}

static esp_err_t reset_post(httpd_req_t *request)
{
    provisioning_portal_touch(g_provisioning_portal);
    if (request->content_len <= 0 || request->content_len > FORM_BODY_MAX)
        return provisioning_portal_send_json(request, "400 Bad Request", "{\"error\":\"恢复确认无效\"}");
    char *body = calloc(1, (size_t)request->content_len + 1);
    if (!body)
        return provisioning_portal_send_json(request, "500 Internal Server Error", "{\"error\":\"内存不足\"}");
    int offset = 0;
    while (offset < request->content_len) {
        int received = httpd_req_recv(request, body + offset,
                                      request->content_len - offset);
        if (received <= 0) {
            free(body);
            return ESP_FAIL;
        }
        offset += received;
    }
    bool confirmed = provisioning_reset_confirmed(body);
    free(body);
    if (!confirmed)
        return provisioning_portal_send_json(request, "400 Bad Request", "{\"error\":\"请确认恢复默认设置\"}");

    esp_err_t result = config_store_clear(g_provisioning_portal->store);
    if (result != ESP_OK)
        return provisioning_portal_send_json(request, "500 Internal Server Error", "{\"error\":\"恢复默认设置失败\"}");
    device_control_settings_t defaults;
    device_control_settings_default(&defaults);
    result = device_control_update(g_provisioning_portal->device_control, &defaults);
    if (result != ESP_OK)
        return provisioning_portal_send_json(request, "500 Internal Server Error",
                         "{\"error\":\"恢复显示设置失败\"}");

    char response[96];
    snprintf(response, sizeof(response),
             "{\"ok\":true,\"ap_ssid\":\"%s\"}", g_provisioning_portal->ap_ssid);
    esp_err_t response_result = provisioning_portal_send_json(request, "200 OK", response);
    if (xSemaphoreTake(g_provisioning_portal->lock, portMAX_DELAY) == pdTRUE) {
        g_provisioning_portal->reset_requested = true;
        xSemaphoreGive(g_provisioning_portal->lock);
    }
    ESP_LOGW(TAG, "network and pairing configuration cleared; restart requested");
    return response_result;
}

static esp_err_t redirect_404(httpd_req_t *request, httpd_err_code_t error)
{
    (void)error;
    provisioning_portal_touch(g_provisioning_portal);
    httpd_resp_set_status(request, "303 See Other");
    httpd_resp_set_hdr(request, "Location", "/");
    return httpd_resp_sendstr(request, "Solis Monitor");
}

static esp_err_t portal_start(provisioning_portal_t **portal, esp_netif_t *dns_netif,
                              config_store_t *store,
                              device_control_t *device_control,
                              const char *label, uint32_t now_ms,
                              const char *ap_ssid, bool expiring)
{
    if (!portal || !store || !device_control || !label || g_provisioning_portal)
        return ESP_ERR_INVALID_ARG;
    provisioning_portal_t *context = calloc(1, sizeof(*context));
    if (!context) return ESP_ERR_NO_MEM;
    context->store = store;
    context->device_control = device_control;
    context->last_activity_ms = now_ms;
    context->expiring = expiring;
    context->ota_allowed = dns_netif == NULL;
    if (ap_ssid && ap_ssid[0]) {
        snprintf(context->ap_ssid, sizeof(context->ap_ssid), "%s", ap_ssid);
    } else {
        uint8_t mac[6];
        esp_err_t mac_result = esp_read_mac(mac, ESP_MAC_WIFI_SOFTAP);
        if (mac_result != ESP_OK) {
            free(context);
            return mac_result;
        }
        snprintf(context->ap_ssid, sizeof(context->ap_ssid),
                 "Solis-Monitor-%02X%02X", mac[4], mac[5]);
    }
    context->lock = xSemaphoreCreateMutex();
    if (!context->lock) { free(context); return ESP_ERR_NO_MEM; }

    httpd_config_t config = HTTPD_DEFAULT_CONFIG();
    config.max_open_sockets = 5;
    config.max_uri_handlers = 12;
    config.lru_purge_enable = true;
    esp_err_t result = httpd_start(&context->http, &config);
    if (result != ESP_OK) goto failed;
    const httpd_uri_t uris[] = {
        {.uri = "/", .method = HTTP_GET, .handler = root_get},
        {.uri = "/api/config", .method = HTTP_GET, .handler = config_get},
        {.uri = "/api/config", .method = HTTP_POST, .handler = config_post},
        {.uri = "/api/reset", .method = HTTP_POST, .handler = reset_post},
        {.uri = "/api/device", .method = HTTP_GET, .handler = provisioning_portal_device_get},
        {.uri = "/api/pair", .method = HTTP_POST, .handler = provisioning_portal_pairing_post},
        {.uri = "/api/ota/status", .method = HTTP_GET, .handler = provisioning_portal_ota_status_get},
        {.uri = "/api/ota", .method = HTTP_POST, .handler = provisioning_portal_ota_update_post},
        {.uri = "/api/control", .method = HTTP_GET, .handler = provisioning_portal_control_get},
        {.uri = "/api/control", .method = HTTP_POST, .handler = provisioning_portal_control_post},
        {.uri = "/api/restart", .method = HTTP_POST, .handler = provisioning_portal_restart_post},
        {.uri = "/api/scan", .method = HTTP_GET, .handler = scan_get},
    };
    g_provisioning_portal = context;
    for (size_t index = 0; index < sizeof(uris) / sizeof(uris[0]); ++index) {
        result = httpd_register_uri_handler(context->http, &uris[index]);
        if (result != ESP_OK) goto failed;
    }
    httpd_register_err_handler(context->http, HTTPD_404_NOT_FOUND, redirect_404);
    if (dns_netif) {
        context->dns = captive_dns_start(dns_netif);
        if (!context->dns) { result = ESP_FAIL; goto failed; }
    }
    *portal = context;
    ESP_LOGI(TAG, "%s server started", label);
    return ESP_OK;

failed:
    g_provisioning_portal = NULL;
    if (context->dns) captive_dns_stop(context->dns);
    if (context->http) httpd_stop(context->http);
    vSemaphoreDelete(context->lock);
    free(context);
    return result;
}

esp_err_t provisioning_portal_start(provisioning_portal_t **portal, esp_netif_t *ap_netif,
                                    config_store_t *store,
                                    device_control_t *device_control,
                                    const char *ssid, uint32_t now_ms)
{
    if (!ap_netif || !ssid) return ESP_ERR_INVALID_ARG;
    char label[64];
    snprintf(label, sizeof(label), "AP portal %s at 192.168.0.1", ssid);
    return portal_start(
        portal, ap_netif, store, device_control, label, now_ms, ssid, true);
}

esp_err_t provisioning_portal_start_lan(provisioning_portal_t **portal,
                                        config_store_t *store,
                                        device_control_t *device_control,
                                        uint32_t now_ms)
{
    return portal_start(
        portal, NULL, store, device_control, "LAN management", now_ms,
        NULL, false);
}

void provisioning_portal_stop(provisioning_portal_t *portal)
{
    if (!portal) return;
    if (portal->dns) captive_dns_stop(portal->dns);
    if (portal->http) httpd_stop(portal->http);
    g_provisioning_portal = NULL;
    vSemaphoreDelete(portal->lock);
    free(portal);
}

bool provisioning_portal_take_saved(provisioning_portal_t *portal, network_config_t *config)
{
    if (!portal || !config || xSemaphoreTake(portal->lock, portMAX_DELAY) != pdTRUE) return false;
    bool saved = portal->saved;
    if (saved) {
        *config = portal->saved_config;
        portal->saved = false;
    }
    xSemaphoreGive(portal->lock);
    return saved;
}

bool provisioning_portal_take_reset_requested(provisioning_portal_t *portal)
{
    if (!portal || xSemaphoreTake(portal->lock, portMAX_DELAY) != pdTRUE)
        return false;
    bool requested = portal->reset_requested;
    portal->reset_requested = false;
    xSemaphoreGive(portal->lock);
    return requested;
}

uint32_t provisioning_portal_remaining_seconds(provisioning_portal_t *portal, uint32_t now_ms)
{
    if (!portal || !portal->expiring) return 0;
    uint32_t elapsed = (uint32_t)(now_ms - portal->last_activity_ms);
    if (elapsed >= PROVISIONING_TIMEOUT_MS) return 0;
    return (PROVISIONING_TIMEOUT_MS - elapsed + 999U) / 1000U;
}

bool provisioning_portal_expired(provisioning_portal_t *portal, uint32_t now_ms)
{
    return portal && portal->expiring &&
           (uint32_t)(now_ms - portal->last_activity_ms) >= PROVISIONING_TIMEOUT_MS;
}
