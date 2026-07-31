#include "provisioning_portal_internal.h"

#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include "esp_app_desc.h"
#include "esp_wifi.h"
#include "ota_update.h"

static bool parse_long_value(const char *body, const char *key,
                             long minimum, long maximum, long *output)
{
    char value[16];
    if (!output || !provisioning_portal_form_value(body, key, value, sizeof(value)))
        return false;
    char *end = NULL;
    long parsed = strtol(value, &end, 10);
    if (!end || *end || parsed < minimum || parsed > maximum)
        return false;
    *output = parsed;
    return true;
}

esp_err_t provisioning_device_control_parse(
    const char *body, device_control_settings_t *settings)
{
    if (!body || !settings) return ESP_ERR_INVALID_ARG;
    char night_enabled[8];
    long brightness;
    long start;
    long end;
    long utc_offset;
    if (!parse_long_value(
            body, "brightness", DEVICE_CONTROL_MIN_BRIGHTNESS,
            DEVICE_CONTROL_MAX_BRIGHTNESS, &brightness) ||
        !provisioning_portal_form_value(
            body, "night_enabled", night_enabled, sizeof(night_enabled)) ||
        !parse_long_value(body, "night_start", 0, 24 * 60 - 1, &start) ||
        !parse_long_value(body, "night_end", 0, 24 * 60 - 1, &end) ||
        !parse_long_value(
            body, "utc_offset", DEVICE_CONTROL_MIN_UTC_OFFSET_MINUTES,
            DEVICE_CONTROL_MAX_UTC_OFFSET_MINUTES, &utc_offset)) {
        return ESP_ERR_INVALID_ARG;
    }

    bool enabled;
    if (strcmp(night_enabled, "1") == 0 ||
        strcmp(night_enabled, "true") == 0) {
        enabled = true;
    } else if (strcmp(night_enabled, "0") == 0 ||
               strcmp(night_enabled, "false") == 0) {
        enabled = false;
    } else {
        return ESP_ERR_INVALID_ARG;
    }

    device_control_settings_t candidate = {
        .brightness_percent = (uint8_t)brightness,
        .night_enabled = enabled,
        .night_start_minute = (uint16_t)start,
        .night_end_minute = (uint16_t)end,
        .utc_offset_minutes = (int16_t)utc_offset,
    };
    if (!device_control_settings_valid(&candidate))
        return ESP_ERR_INVALID_ARG;
    *settings = candidate;
    return ESP_OK;
}

bool provisioning_device_info_format(char *output, size_t output_size,
                                     const char *hostname, const char *firmware,
                                     const char *ip, int rssi, bool has_rssi,
                                     bool paired, bool pairing)
{
    if (!output || output_size == 0 || !hostname || !firmware || !ip) return false;
    char rssi_text[16];
    if (has_rssi) snprintf(rssi_text, sizeof(rssi_text), "%d", rssi);
    else snprintf(rssi_text, sizeof(rssi_text), "null");

    int written = snprintf(
        output, output_size,
        "{\"product\":\"Solis Monitor\",\"hostname\":\"%s\",\"firmware\":\"%s\","
        "\"ip\":\"%s\",\"rssi\":%s,\"paired\":%s,\"pairing\":%s}",
        hostname, firmware, ip, rssi_text, paired ? "true" : "false",
        pairing ? "true" : "false");
    return written > 0 && (size_t)written < output_size;
}

esp_err_t provisioning_portal_device_get(httpd_req_t *request)
{
    provisioning_portal_touch(g_provisioning_portal);
    network_config_t config = {0};
    bool present = false;
    if (config_store_load(g_provisioning_portal->store, &config, &present) != ESP_OK)
        return provisioning_portal_send_json(request, "500 Internal Server Error", "{\"error\":\"读取设备状态失败\"}");

    esp_netif_t *station = esp_netif_get_handle_from_ifkey("WIFI_STA_DEF");
    esp_netif_ip_info_t ip_info = {0};
    const char *hostname = "Solis_Monitor";
    if (station) {
        const char *configured_hostname = NULL;
        if (esp_netif_get_hostname(station, &configured_hostname) == ESP_OK &&
            configured_hostname && configured_hostname[0]) {
            hostname = configured_hostname;
        }
        esp_netif_get_ip_info(station, &ip_info);
    }

    char ip[16];
    snprintf(ip, sizeof(ip), IPSTR, IP2STR(&ip_info.ip));
    wifi_ap_record_t access_point = {0};
    bool has_rssi = esp_wifi_sta_get_ap_info(&access_point) == ESP_OK;
    const esp_app_desc_t *description = esp_app_get_description();
    char response[256];
    bool paired = present && strlen(config.token) == NETCFG_TOKEN_HEX_LENGTH;
    bool pairing = provisioning_portal_pairing_active(g_provisioning_portal, provisioning_portal_now_ms());
    if (!provisioning_device_info_format(
            response, sizeof(response), hostname,
            description ? description->version : "unknown", ip,
            has_rssi ? access_point.rssi : 0, has_rssi, paired, pairing)) {
        return provisioning_portal_send_json(request, "500 Internal Server Error", "{\"error\":\"设备状态过长\"}");
    }
    return provisioning_portal_send_json(request, "200 OK", response);
}

static bool request_has_paired_token(httpd_req_t *request)
{
    if (!request || !g_provisioning_portal || !g_provisioning_portal->ota_allowed) return false;

    network_config_t config = {0};
    bool present = false;
    if (config_store_load(g_provisioning_portal->store, &config, &present) != ESP_OK ||
        !present || strlen(config.token) != NETCFG_TOKEN_HEX_LENGTH) {
        return false;
    }

    size_t length = httpd_req_get_hdr_value_len(request, "Authorization");
    if (length == 0 || length >= 80) return false;
    char authorization[80];
    if (httpd_req_get_hdr_value_str(
            request, "Authorization", authorization,
            sizeof(authorization)) != ESP_OK) {
        return false;
    }
    return provisioning_bearer_token_matches(authorization, config.token);
}

esp_err_t provisioning_portal_ota_status_get(httpd_req_t *request)
{
    provisioning_portal_touch(g_provisioning_portal);
    if (!g_provisioning_portal->ota_allowed)
        return provisioning_portal_send_json(request, "404 Not Found", "{\"error\":\"仅支持局域网 OTA\"}");
    if (!request_has_paired_token(request)) {
        httpd_resp_set_hdr(request, "WWW-Authenticate", "Bearer");
        return provisioning_portal_send_json(request, "401 Unauthorized", "{\"error\":\"设备令牌无效\"}");
    }

    ota_update_status_t status;
    if (ota_update_get_status(&status) != ESP_OK || !status.supported) {
        return provisioning_portal_send_json(
            request, "503 Service Unavailable",
            "{\"error\":\"当前分区表不支持 OTA\"}");
    }

    char response[256];
    int written = snprintf(
        response, sizeof(response),
        "{\"product\":\"Solis Monitor\",\"chip\":\"esp32s3\","
        "\"project\":\"%s\",\"version\":\"%s\","
        "\"max_image_size\":%u,\"rollback\":%s}",
        status.project_name, status.version,
        (unsigned)status.max_image_size,
        status.rollback_enabled ? "true" : "false");
    if (written < 0 || (size_t)written >= sizeof(response))
        return provisioning_portal_send_json(request, "500 Internal Server Error", "{\"error\":\"OTA 状态过长\"}");
    return provisioning_portal_send_json(request, "200 OK", response);
}

esp_err_t provisioning_portal_control_get(httpd_req_t *request)
{
    provisioning_portal_touch(g_provisioning_portal);
    if (!g_provisioning_portal->ota_allowed)
        return provisioning_portal_send_json(request, "404 Not Found",
                         "{\"error\":\"仅支持局域网设备控制\"}");
    if (!request_has_paired_token(request)) {
        httpd_resp_set_hdr(request, "WWW-Authenticate", "Bearer");
        return provisioning_portal_send_json(request, "401 Unauthorized",
                         "{\"error\":\"设备令牌无效\"}");
    }

    device_control_settings_t settings;
    if (device_control_get(g_provisioning_portal->device_control, &settings) != ESP_OK)
        return provisioning_portal_send_json(request, "500 Internal Server Error",
                         "{\"error\":\"读取显示设置失败\"}");
    char response[192];
    snprintf(
        response, sizeof(response),
        "{\"brightness\":%u,\"night_enabled\":%s,"
        "\"night_start\":%u,\"night_end\":%u,\"utc_offset\":%d}",
        settings.brightness_percent,
        settings.night_enabled ? "true" : "false",
        settings.night_start_minute,
        settings.night_end_minute,
        settings.utc_offset_minutes);
    return provisioning_portal_send_json(request, "200 OK", response);
}

esp_err_t provisioning_portal_control_post(httpd_req_t *request)
{
    provisioning_portal_touch(g_provisioning_portal);
    if (!g_provisioning_portal->ota_allowed)
        return provisioning_portal_send_json(request, "404 Not Found",
                         "{\"error\":\"仅支持局域网设备控制\"}");
    if (!request_has_paired_token(request)) {
        httpd_resp_set_hdr(request, "WWW-Authenticate", "Bearer");
        return provisioning_portal_send_json(request, "401 Unauthorized",
                         "{\"error\":\"设备令牌无效\"}");
    }
    if (request->content_len <= 0 || request->content_len > FORM_BODY_MAX)
        return provisioning_portal_send_json(request, "400 Bad Request",
                         "{\"error\":\"显示设置无效\"}");

    char *body = calloc(1, (size_t)request->content_len + 1);
    if (!body)
        return provisioning_portal_send_json(request, "500 Internal Server Error",
                         "{\"error\":\"内存不足\"}");
    int offset = 0;
    while (offset < request->content_len) {
        int received = httpd_req_recv(
            request, body + offset, request->content_len - offset);
        if (received <= 0) {
            free(body);
            return ESP_FAIL;
        }
        offset += received;
    }

    device_control_settings_t settings;
    esp_err_t result = provisioning_device_control_parse(body, &settings);
    free(body);
    if (result != ESP_OK)
        return provisioning_portal_send_json(request, "400 Bad Request",
                         "{\"error\":\"请检查亮度、时间和时区\"}");
    result = device_control_update(g_provisioning_portal->device_control, &settings);
    if (result != ESP_OK)
        return provisioning_portal_send_json(request, "500 Internal Server Error",
                         "{\"error\":\"保存显示设置失败\"}");
    return provisioning_portal_send_json(request, "200 OK", "{\"ok\":true}");
}

esp_err_t provisioning_portal_restart_post(httpd_req_t *request)
{
    provisioning_portal_touch(g_provisioning_portal);
    if (!g_provisioning_portal->ota_allowed)
        return provisioning_portal_send_json(request, "404 Not Found",
                         "{\"error\":\"仅支持局域网设备控制\"}");
    if (!request_has_paired_token(request)) {
        httpd_resp_set_hdr(request, "WWW-Authenticate", "Bearer");
        return provisioning_portal_send_json(request, "401 Unauthorized",
                         "{\"error\":\"设备令牌无效\"}");
    }
    device_control_request_restart(g_provisioning_portal->device_control);
    return provisioning_portal_send_json(request, "200 OK", "{\"ok\":true}");
}

esp_err_t provisioning_portal_ota_update_post(httpd_req_t *request)
{
    provisioning_portal_touch(g_provisioning_portal);
    if (!g_provisioning_portal->ota_allowed)
        return provisioning_portal_send_json(request, "404 Not Found", "{\"error\":\"仅支持局域网 OTA\"}");
    if (!request_has_paired_token(request)) {
        httpd_resp_set_hdr(request, "WWW-Authenticate", "Bearer");
        return provisioning_portal_send_json(request, "401 Unauthorized", "{\"error\":\"设备令牌无效\"}");
    }

    ota_update_status_t status;
    size_t required = ota_update_required_header_size();
    size_t image_size = request->content_len;
    if (ota_update_get_status(&status) != ESP_OK || !status.supported)
        return provisioning_portal_send_json(request, "503 Service Unavailable", "{\"error\":\"当前分区表不支持 OTA\"}");
    if (image_size < required)
        return provisioning_portal_send_json(request, "400 Bad Request", "{\"error\":\"固件文件不完整\"}");
    if (image_size > status.max_image_size)
        return provisioning_portal_send_json(request, "413 Payload Too Large", "{\"error\":\"固件超过 OTA 分区容量\"}");

    uint8_t *buffer = malloc(4096);
    if (!buffer)
        return provisioning_portal_send_json(request, "503 Service Unavailable", "{\"error\":\"内存不足\"}");

    size_t received_total = 0;
    while (received_total < required) {
        int received = httpd_req_recv(
            request, (char *)buffer + received_total,
            required - received_total);
        if (received <= 0) {
            free(buffer);
            return provisioning_portal_send_json(request, "408 Request Timeout", "{\"error\":\"固件传输中断\"}");
        }
        received_total += (size_t)received;
    }

    ota_update_session_t session = {0};
    esp_err_t result = ota_update_session_begin(
        &session, buffer, received_total, image_size);
    if (result != ESP_OK) {
        free(buffer);
        if (result == ESP_ERR_INVALID_SIZE)
            return provisioning_portal_send_json(request, "413 Payload Too Large", "{\"error\":\"固件超过 OTA 分区容量\"}");
        return provisioning_portal_send_json(request, "400 Bad Request", "{\"error\":\"固件型号、项目或镜像头无效\"}");
    }

    result = ota_update_session_write(&session, buffer, received_total);
    while (result == ESP_OK && received_total < image_size) {
        size_t remaining = image_size - received_total;
        size_t requested = remaining < 4096 ? remaining : 4096;
        int received = httpd_req_recv(request, (char *)buffer, requested);
        if (received <= 0) {
            result = ESP_ERR_TIMEOUT;
            break;
        }
        result = ota_update_session_write(
            &session, buffer, (size_t)received);
        if (result == ESP_OK) received_total += (size_t)received;
    }
    free(buffer);

    if (result != ESP_OK) {
        ota_update_session_abort(&session);
        return provisioning_portal_send_json(request, "408 Request Timeout", "{\"error\":\"固件传输中断，旧固件未改变\"}");
    }
    result = ota_update_session_finish(&session);
    if (result != ESP_OK) {
        return provisioning_portal_send_json(request, "400 Bad Request", "{\"error\":\"固件完整性校验失败，旧固件未改变\"}");
    }

    if (xSemaphoreTake(g_provisioning_portal->lock, portMAX_DELAY) == pdTRUE) {
        g_provisioning_portal->ota_restart_requested = true;
        xSemaphoreGive(g_provisioning_portal->lock);
    }
    char response[96];
    snprintf(response, sizeof(response),
             "{\"ok\":true,\"version\":\"%s\"}",
             session.image.version);
    return provisioning_portal_send_json(request, "200 OK", response);
}

bool provisioning_portal_take_ota_restart_requested(
    provisioning_portal_t *portal)
{
    if (!portal || xSemaphoreTake(portal->lock, portMAX_DELAY) != pdTRUE)
        return false;
    bool requested = portal->ota_restart_requested;
    portal->ota_restart_requested = false;
    xSemaphoreGive(portal->lock);
    return requested;
}
