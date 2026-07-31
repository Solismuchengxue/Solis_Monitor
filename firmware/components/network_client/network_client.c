#include "network_client_internal.h"

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <strings.h>

#include "esp_event.h"
#include "esp_http_client.h"
#include "esp_log.h"
#include "esp_mac.h"
#include "esp_netif.h"
#include "esp_system.h"
#include "esp_wifi.h"
#include "freertos/FreeRTOS.h"
#include "freertos/event_groups.h"
#include "freertos/semphr.h"
#include "freertos/task.h"
#include "metrics_protocol.h"
#include "provisioning_portal.h"
#include "apps/dhcpserver/dhcpserver.h"

#define NETWORK_EVENT_GOT_IP BIT0
#define NETWORK_EVENT_COMMAND BIT1
#define NETWORK_EVENT_PROVISION BIT2
#define NETWORK_EVENT_PHYSICAL BIT3
#define NETWORK_EVENT_MODE_EXIT BIT4
#define NETWORK_POLL_INTERVAL_MS 1000U
#define NETWORK_SOURCE_TIMEOUT_MS 5000U
#define NETWORK_HTTP_TIMEOUT_MS 800
#define NETWORK_HTTP_BODY_BYTES 4096U
#define NETWORK_URL_BYTES 96U
#define NETWORK_AUTHORIZATION_BYTES 80U

static const char *const TAG = "network_client";
static network_client_t *s_network_client;
static bool s_network_stack_initialized;

struct network_client {
    config_store_t *config_store;
    dashboard_store_t *dashboard_store;
    device_control_t *device_control;
    EventGroupHandle_t events;
    SemaphoreHandle_t http_mutex;
    esp_http_client_handle_t http;
    esp_netif_t *station_netif;
    esp_netif_t *ap_netif;
    provisioning_portal_t *portal;
    provisioning_portal_t *management_portal;
    uint32_t init_stages;
    network_config_t config;
    bool configured;
    char url[NETWORK_URL_BYTES];
    char authorization[NETWORK_AUTHORIZATION_BYTES];
    char body[NETWORK_HTTP_BODY_BYTES + 1];
    size_t body_length;
    bool body_overflow;
    bool json_content_type;
    uint32_t last_valid_ms;
    uint32_t last_setup_log_ms;
    uint32_t last_unauthorized_log_ms;
    bool ever_valid;
    bool reported_expired;
    bool setup_log_written;
    bool unauthorized_log_written;
    unsigned wifi_failures;
    bool automatic_portal_attempted;
    bool management_start_attempted;
    char portal_ssid[33];
};

bool network_source_expired(uint32_t now_ms, uint32_t last_valid_ms, bool ever_valid)
{
    return ever_valid && (uint32_t)(now_ms - last_valid_ms) >= NETWORK_SOURCE_TIMEOUT_MS;
}

uint32_t network_retry_delay_ms(unsigned failure_count)
{
    if (failure_count <= 1) return 1000;
    if (failure_count >= 6) return 30000;
    return 1000U << (failure_count - 1);
}

network_task_action_t network_task_next_action(bool configured, bool got_ip,
                                               bool reconnect_requested)
{
    if (reconnect_requested) return NETWORK_TASK_RELOAD;
    if (!configured) return NETWORK_TASK_WAIT;
    return got_ip ? NETWORK_TASK_POLL : NETWORK_TASK_RETRY;
}

uint32_t network_freshness_wait_ms(uint32_t requested_ms, uint32_t now_ms,
                                   uint32_t last_valid_ms, bool ever_valid,
                                   bool reported_expired)
{
    if (!ever_valid || reported_expired) return requested_ms;

    uint32_t elapsed_ms = (uint32_t)(now_ms - last_valid_ms);
    uint32_t remaining_ms = elapsed_ms >= NETWORK_SOURCE_TIMEOUT_MS
                                ? 0
                                : NETWORK_SOURCE_TIMEOUT_MS - elapsed_ms;
    return requested_ms < remaining_ms ? requested_ms : remaining_ms;
}

bool network_apply_source_freshness(dashboard_store_t *store, uint32_t now_ms,
                                    uint32_t last_valid_ms, bool ever_valid,
                                    bool *reported_expired)
{
    if (!store || !reported_expired || *reported_expired ||
        !network_source_expired(now_ms, last_valid_ms, ever_valid)) {
        return false;
    }
    if (!dashboard_store_set_source_online(store, false)) return false;
    *reported_expired = true;
    return true;
}

network_cleanup_action_t network_init_cleanup_next(uint32_t *completed_stages)
{
    if (!completed_stages) return NETWORK_CLEANUP_DONE;

    static const struct {
        uint32_t stage;
        network_cleanup_action_t action;
    } cleanup_order[] = {
        {NETWORK_INIT_IP_HANDLER, NETWORK_CLEANUP_IP_HANDLER},
        {NETWORK_INIT_WIFI_HANDLER, NETWORK_CLEANUP_WIFI_HANDLER},
        {NETWORK_INIT_WIFI_STARTED, NETWORK_CLEANUP_WIFI_STOP},
        {NETWORK_INIT_DEFAULT_AP, NETWORK_CLEANUP_DEFAULT_AP},
        {NETWORK_INIT_DEFAULT_STA, NETWORK_CLEANUP_DEFAULT_STA},
        {NETWORK_INIT_WIFI, NETWORK_CLEANUP_WIFI_DEINIT},
        {NETWORK_INIT_DEFAULT_EVENT_LOOP, NETWORK_CLEANUP_DEFAULT_EVENT_LOOP},
    };

    for (size_t index = 0; index < sizeof(cleanup_order) / sizeof(cleanup_order[0]); ++index) {
        if (*completed_stages & cleanup_order[index].stage) {
            *completed_stages &= ~cleanup_order[index].stage;
            return cleanup_order[index].action;
        }
    }
    return NETWORK_CLEANUP_DONE;
}

static uint32_t network_now_ms(void)
{
    return (uint32_t)(xTaskGetTickCount() * portTICK_PERIOD_MS);
}

static bool network_config_equal(const network_config_t *left, const network_config_t *right)
{
    return left->port == right->port && strcmp(left->ssid, right->ssid) == 0 &&
           strcmp(left->password, right->password) == 0 && strcmp(left->host, right->host) == 0 &&
           strcmp(left->token, right->token) == 0;
}

static void network_destroy_http(network_client_t *client)
{
    if (xSemaphoreTake(client->http_mutex, portMAX_DELAY) != pdTRUE) return;
    if (client->http) {
        esp_http_client_cleanup(client->http);
        client->http = NULL;
    }
    xSemaphoreGive(client->http_mutex);
}

static esp_err_t network_http_event(esp_http_client_event_t *event)
{
    network_client_t *client = event ? event->user_data : NULL;

    if (!client) return ESP_ERR_INVALID_ARG;
    if (event->event_id == HTTP_EVENT_ON_HEADER && event->header_key && event->header_value &&
        strcasecmp(event->header_key, "Content-Type") == 0 &&
        strncasecmp(event->header_value, "application/json", 16) == 0) {
        client->json_content_type = true;
    }
    if (event->event_id == HTTP_EVENT_ON_DATA) {
        if (event->data_len < 0 || (size_t)event->data_len > NETWORK_HTTP_BODY_BYTES - client->body_length) {
            client->body_overflow = true;
        } else if (!client->body_overflow && event->data_len > 0) {
            memcpy(client->body + client->body_length, event->data, (size_t)event->data_len);
            client->body_length += (size_t)event->data_len;
            client->body[client->body_length] = '\0';
        }
    }
    return ESP_OK;
}

static esp_err_t network_create_http(network_client_t *client)
{
    int written = snprintf(client->url, sizeof(client->url), "http://%s:%u/api/v1/metrics",
                           client->config.host, client->config.port);
    if (written < 0 || (size_t)written >= sizeof(client->url)) return ESP_ERR_INVALID_SIZE;
    written = snprintf(client->authorization, sizeof(client->authorization), "Bearer %s",
                       client->config.token);
    if (written < 0 || (size_t)written >= sizeof(client->authorization)) return ESP_ERR_INVALID_SIZE;

    const esp_http_client_config_t http_config = {
        .url = client->url,
        .method = HTTP_METHOD_GET,
        .timeout_ms = NETWORK_HTTP_TIMEOUT_MS,
        .disable_auto_redirect = true,
        .transport_type = HTTP_TRANSPORT_OVER_TCP,
        .event_handler = network_http_event,
        .user_data = client,
    };
    esp_http_client_handle_t http = esp_http_client_init(&http_config);
    if (!http) return ESP_ERR_NO_MEM;
    esp_err_t result = esp_http_client_set_header(http, "Authorization", client->authorization);
    if (result == ESP_OK) result = esp_http_client_set_header(http, "Accept", "application/json");
    if (result != ESP_OK) {
        esp_http_client_cleanup(http);
        return result;
    }

    if (xSemaphoreTake(client->http_mutex, portMAX_DELAY) != pdTRUE) {
        esp_http_client_cleanup(http);
        return ESP_FAIL;
    }
    client->http = http;
    xSemaphoreGive(client->http_mutex);
    return ESP_OK;
}

static void network_update_freshness(network_client_t *client, uint32_t now_ms)
{
    network_apply_source_freshness(client->dashboard_store, now_ms, client->last_valid_ms,
                                   client->ever_valid, &client->reported_expired);
}

static TickType_t network_wait_ticks(uint32_t wait_ms)
{
    if (wait_ms == UINT32_MAX) return portMAX_DELAY;
    if (wait_ms == 0) return 0;

    TickType_t ticks = pdMS_TO_TICKS(wait_ms);
    return ticks == 0 ? 1 : ticks;
}

static EventBits_t network_wait_events(network_client_t *client, EventBits_t event_bits,
                                       uint32_t requested_ms)
{
    uint32_t remaining_ms = requested_ms;

    for (;;) {
        uint32_t wait_ms = network_freshness_wait_ms(
            remaining_ms, network_now_ms(), client->last_valid_ms, client->ever_valid,
            client->reported_expired);
        EventBits_t received = xEventGroupWaitBits(client->events, event_bits, pdFALSE, pdFALSE,
                                                   network_wait_ticks(wait_ms));
        network_update_freshness(client, network_now_ms());
        if (received & event_bits) return received;
        if (requested_ms == UINT32_MAX) continue;
        if (wait_ms >= remaining_ms) return 0;
        remaining_ms -= wait_ms;
    }
}

static void network_delay_until_next_poll(network_client_t *client, TickType_t *wake_time)
{
    const TickType_t poll_ticks = pdMS_TO_TICKS(NETWORK_POLL_INTERVAL_MS);
    TickType_t after_poll = xTaskGetTickCount();

    if ((TickType_t)(after_poll - *wake_time) >= poll_ticks) {
        *wake_time = after_poll;
    }
    TickType_t scheduled_ticks = (TickType_t)(*wake_time + poll_ticks - after_poll);
    uint32_t scheduled_ms = (uint32_t)(scheduled_ticks * portTICK_PERIOD_MS);
    uint32_t freshness_ms = network_freshness_wait_ms(
        scheduled_ms, network_now_ms(), client->last_valid_ms, client->ever_valid,
        client->reported_expired);
    if (freshness_ms < scheduled_ms) {
        vTaskDelay(network_wait_ticks(freshness_ms));
        network_update_freshness(client, network_now_ms());
    }
    vTaskDelayUntil(wake_time, poll_ticks);
    network_update_freshness(client, network_now_ms());
}

static bool network_poll(network_client_t *client)
{
    dashboard_state_t previous;
    dashboard_state_t next;
    metrics_metadata_t metadata;
    esp_http_client_handle_t http = client->http;

    if (!http || !dashboard_store_snapshot(client->dashboard_store, &previous,
                                            &metadata.sequence, &metadata.generated_at)) {
        return false;
    }
    client->body_length = 0;
    client->body[0] = '\0';
    client->body_overflow = false;
    client->json_content_type = false;

    esp_err_t result = esp_http_client_perform(http);
    int status = esp_http_client_get_status_code(http);
    if (status == 401) {
        uint32_t now_ms = network_now_ms();
        if (!client->unauthorized_log_written ||
            (uint32_t)(now_ms - client->last_unauthorized_log_ms) >= 30000U) {
            ESP_LOGW(TAG, "unauthorized; check device token");
            client->last_unauthorized_log_ms = now_ms;
            client->unauthorized_log_written = true;
        }
    }
    if (result != ESP_OK || status != 200 || !client->json_content_type || client->body_overflow ||
        client->body_length == 0 || client->body_length > NETWORK_HTTP_BODY_BYTES ||
        metrics_protocol_apply(client->body, client->body_length, &previous, &next, &metadata) != ESP_OK) {
        return false;
    }
    if (!dashboard_store_replace(client->dashboard_store, &next, metadata.sequence, metadata.generated_at)) {
        return false;
    }

    client->last_valid_ms = network_now_ms();
    client->ever_valid = true;
    client->reported_expired = false;
    client->wifi_failures = 0;
    client->unauthorized_log_written = false;
    return true;
}

static void network_station_event(void *argument, esp_event_base_t event_base,
                                  int32_t event_id, void *event_data)
{
    (void)event_data;
    network_client_t *client = argument;

    if (!client) return;
    if (event_base == WIFI_EVENT && event_id == WIFI_EVENT_AP_STACONNECTED) {
        const wifi_event_ap_staconnected_t *connected = event_data;
        if (connected) ESP_LOGI(TAG, "provisioning client joined, aid=%d", connected->aid);
    } else if (event_base == WIFI_EVENT && event_id == WIFI_EVENT_AP_STADISCONNECTED) {
        const wifi_event_ap_stadisconnected_t *disconnected = event_data;
        if (disconnected) ESP_LOGI(TAG, "provisioning client left, aid=%d reason=%d",
                                   disconnected->aid, disconnected->reason);
    } else if (event_base == WIFI_EVENT && event_id == WIFI_EVENT_STA_DISCONNECTED) {
        xEventGroupClearBits(client->events, NETWORK_EVENT_GOT_IP);
        client->management_start_attempted = false;
        dashboard_store_set_wifi_state(client->dashboard_store, false, NULL);
    } else if (event_base == IP_EVENT && event_id == IP_EVENT_STA_GOT_IP) {
        xEventGroupSetBits(client->events, NETWORK_EVENT_GOT_IP);
        dashboard_store_set_wifi_state(client->dashboard_store, true, client->config.ssid);
    } else if (event_base == IP_EVENT && event_id == IP_EVENT_ASSIGNED_IP_TO_CLIENT) {
        const ip_event_assigned_ip_to_client_t *assigned = event_data;
        if (assigned) ESP_LOGI(TAG, "provisioning client assigned " IPSTR,
                               IP2STR(&assigned->ip));
    }
}

bool network_format_station_hostname(const uint8_t mac[6], char *hostname,
                                     size_t hostname_size)
{
    if (!mac || !hostname || hostname_size == 0) return false;
    int written = snprintf(hostname, hostname_size, "Solis_Monitor_%02X%02X",
                           mac[4], mac[5]);
    return written > 0 && (size_t)written < hostname_size;
}

static esp_err_t network_configure_station(network_client_t *client)
{
    wifi_config_t wifi_config = {0};
    size_t ssid_length = strnlen(client->config.ssid, sizeof(client->config.ssid));
    size_t password_length = strnlen(client->config.password, sizeof(client->config.password));

    if (ssid_length > sizeof(wifi_config.sta.ssid) || password_length > sizeof(wifi_config.sta.password)) {
        return ESP_ERR_INVALID_SIZE;
    }
    memcpy(wifi_config.sta.ssid, client->config.ssid, ssid_length);
    memcpy(wifi_config.sta.password, client->config.password, password_length);
    xEventGroupClearBits(client->events, NETWORK_EVENT_GOT_IP);
    dashboard_store_set_wifi_state(client->dashboard_store, false, NULL);
    esp_wifi_disconnect();
    esp_wifi_stop();
    esp_err_t result = esp_wifi_set_mode(WIFI_MODE_STA);
    if (result != ESP_OK) return result;
    uint8_t station_mac[6];
    char hostname[33];
    result = esp_read_mac(station_mac, ESP_MAC_WIFI_STA);
    if (result != ESP_OK ||
        !network_format_station_hostname(station_mac, hostname, sizeof(hostname))) {
        return result == ESP_OK ? ESP_ERR_INVALID_SIZE : result;
    }
    result = esp_netif_set_hostname(client->station_netif, hostname);
    if (result != ESP_OK) return result;
    ESP_LOGI(TAG, "station hostname: %s", hostname);
    result = esp_netif_dhcpc_start(client->station_netif);
    if (result != ESP_OK && result != ESP_ERR_ESP_NETIF_DHCP_ALREADY_STARTED) return result;
    result = esp_wifi_set_config(WIFI_IF_STA, &wifi_config);
    if (result != ESP_OK) return result;
    result = esp_wifi_start();
    if (result != ESP_OK) return result;
    return esp_wifi_connect();
}

static void network_stop_portal(network_client_t *client)
{
    if (!client->portal) return;
    provisioning_portal_stop(client->portal);
    client->portal = NULL;
    dashboard_store_set_provisioning(client->dashboard_store, false, NULL, 0);
    esp_wifi_stop();
    esp_wifi_set_mode(WIFI_MODE_STA);
}

static void network_stop_management(network_client_t *client)
{
    dashboard_store_set_pairing(client->dashboard_store, false, NULL, 0, false);
    if (!client->management_portal) return;
    provisioning_portal_stop(client->management_portal);
    client->management_portal = NULL;
}

static esp_err_t network_start_portal(network_client_t *client)
{
    if (client->portal) return ESP_OK;
    network_stop_management(client);
    network_destroy_http(client);
    xEventGroupClearBits(client->events, NETWORK_EVENT_GOT_IP);
    dashboard_store_set_wifi_state(client->dashboard_store, false, NULL);
    dashboard_store_set_source_online(client->dashboard_store, false);
    esp_wifi_disconnect();
    esp_wifi_stop();

    esp_err_t result = esp_netif_dhcpc_stop(client->station_netif);
    if (result != ESP_OK && result != ESP_ERR_ESP_NETIF_DHCP_ALREADY_STOPPED) goto failed;
    const esp_netif_ip_info_t empty_station_ip = {0};
    result = esp_netif_set_ip_info(client->station_netif, &empty_station_ip);
    if (result != ESP_OK) goto failed;

    esp_netif_ip_info_t ip = {0};
    IP4_ADDR(&ip.ip, 192, 168, 0, 1);
    IP4_ADDR(&ip.gw, 192, 168, 0, 1);
    IP4_ADDR(&ip.netmask, 255, 255, 255, 0);
    esp_netif_dhcps_stop(client->ap_netif);
    result = esp_netif_set_ip_info(client->ap_netif, &ip);
    if (result != ESP_OK) goto failed;
    dhcps_lease_t lease = {.enable = true};
    IP4_ADDR(&lease.start_ip, 192, 168, 0, 2);
    IP4_ADDR(&lease.end_ip, 192, 168, 0, 9);
    result = esp_netif_dhcps_option(client->ap_netif, ESP_NETIF_OP_SET,
                                   ESP_NETIF_REQUESTED_IP_ADDRESS,
                                   &lease, sizeof(lease));
    if (result != ESP_OK) goto failed;
    uint8_t mac[6];
    result = esp_read_mac(mac, ESP_MAC_WIFI_SOFTAP);
    if (result != ESP_OK) goto failed;
    snprintf(client->portal_ssid, sizeof(client->portal_ssid), "Solis-Monitor-%02X%02X",
             mac[4], mac[5]);
    wifi_config_t ap = {0};
    ap.ap.ssid_len = strlen(client->portal_ssid);
    memcpy(ap.ap.ssid, client->portal_ssid, ap.ap.ssid_len);
    ap.ap.authmode = WIFI_AUTH_OPEN;
    ap.ap.max_connection = 4;
    result = esp_wifi_set_mode(WIFI_MODE_APSTA);
    if (result == ESP_OK) result = esp_wifi_set_config(WIFI_IF_AP, &ap);
    if (result == ESP_OK) result = esp_wifi_start();
    if (result != ESP_OK) goto failed;
    result = esp_netif_dhcps_start(client->ap_netif);
    if (result != ESP_OK && result != ESP_ERR_ESP_NETIF_DHCP_ALREADY_STARTED) goto failed;
    result = provisioning_portal_start(
        &client->portal, client->ap_netif, client->config_store,
        client->device_control, client->portal_ssid, network_now_ms());
    if (result != ESP_OK) {
        goto failed;
    }
    dashboard_store_set_provisioning(client->dashboard_store, true, client->portal_ssid,
                                     PROVISIONING_TIMEOUT_MS / 1000U);
    return ESP_OK;

failed:
    esp_wifi_stop();
    esp_wifi_set_mode(WIFI_MODE_STA);
    dashboard_store_set_provisioning(client->dashboard_store, false, NULL, 0);
    if (client->configured) network_configure_station(client);
    return result;
}

static bool network_reload_configuration(network_client_t *client)
{
    network_config_t config;
    bool present = false;
    esp_err_t result = config_store_load(client->config_store, &config, &present);

    network_destroy_http(client);
    network_stop_management(client);
    client->management_start_attempted = false;
    if (result != ESP_OK || !present) {
        client->configured = false;
        xEventGroupClearBits(client->events, NETWORK_EVENT_GOT_IP);
        dashboard_store_set_wifi_state(client->dashboard_store, false, NULL);
        esp_wifi_disconnect();
        esp_wifi_stop();
        return false;
    }
    if (!client->configured || !network_config_equal(&client->config, &config)) client->config = config;
    client->configured = true;
    client->wifi_failures = 0;
    return network_configure_station(client) == ESP_OK;
}

static void network_log_setup_if_due(network_client_t *client)
{
    uint32_t now_ms = network_now_ms();
    if (!client->setup_log_written || (uint32_t)(now_ms - client->last_setup_log_ms) >= 30000U) {
        ESP_LOGW(TAG, "network not configured; run setup");
        client->last_setup_log_ms = now_ms;
        client->setup_log_written = true;
    }
}

static void network_task(void *argument)
{
    network_client_t *client = argument;
    TickType_t wake_time = xTaskGetTickCount();

    for (;;) {
        EventBits_t bits = xEventGroupGetBits(client->events);
        if (bits & NETWORK_EVENT_MODE_EXIT) {
            xEventGroupClearBits(client->events, NETWORK_EVENT_MODE_EXIT);
            if (client->portal) {
                bool restore = client->configured;
                network_stop_portal(client);
                if (restore) network_configure_station(client);
                wake_time = xTaskGetTickCount();
            } else if (client->management_portal) {
                provisioning_portal_end_pairing(client->management_portal);
                dashboard_store_set_pairing(
                    client->dashboard_store, false, NULL, 0, false);
            }
            continue;
        }
        if (bits & NETWORK_EVENT_PHYSICAL) {
            xEventGroupClearBits(client->events, NETWORK_EVENT_PHYSICAL);
            if (client->management_portal) {
                esp_err_t pairing_result = provisioning_portal_begin_pairing(
                    client->management_portal, network_now_ms());
                if (pairing_result == ESP_OK) {
                    char pairing_code[PAIRING_CODE_LENGTH + 1] = {0};
                    provisioning_portal_pairing_code(
                        client->management_portal, network_now_ms(),
                        pairing_code);
                    uint32_t remaining =
                        provisioning_portal_pairing_remaining_seconds(
                            client->management_portal, network_now_ms());
                    dashboard_store_set_pairing(
                        client->dashboard_store, true, pairing_code,
                        remaining, false);
                    ESP_LOGI(TAG, "device discovery enabled");
                } else {
                    ESP_LOGE(TAG, "failed to start device discovery");
                }
            } else {
                ESP_LOGW(TAG, "device discovery ignored; LAN management is unavailable");
            }
            continue;
        }
        if (bits & NETWORK_EVENT_PROVISION) {
            xEventGroupClearBits(client->events, NETWORK_EVENT_PROVISION);
            network_stop_management(client);
            if (network_start_portal(client) != ESP_OK)
                ESP_LOGE(TAG, "failed to start provisioning portal");
            continue;
        }
        if (client->portal) {
            network_config_t saved;
            uint32_t now = network_now_ms();
            if (provisioning_portal_take_reset_requested(client->portal)) {
                vTaskDelay(pdMS_TO_TICKS(500));
                esp_restart();
            }
            if (provisioning_portal_take_saved(client->portal, &saved)) {
                network_stop_portal(client);
                client->automatic_portal_attempted = true;
                network_reload_configuration(client);
                wake_time = xTaskGetTickCount();
                continue;
            }
            if (provisioning_portal_expired(client->portal, now)) {
                bool restore = client->configured;
                network_stop_portal(client);
                if (restore) network_configure_station(client);
                continue;
            }
            dashboard_store_set_provisioning(
                client->dashboard_store, true, client->portal_ssid,
                provisioning_portal_remaining_seconds(client->portal, now));
            bits = network_wait_events(
                client,
                NETWORK_EVENT_COMMAND | NETWORK_EVENT_PROVISION |
                    NETWORK_EVENT_MODE_EXIT,
                1000);
            if (bits & NETWORK_EVENT_COMMAND) {
                network_stop_portal(client);
            }
            continue;
        }
        if (client->management_portal) {
            network_config_t saved;
            uint32_t now = network_now_ms();
            if (provisioning_portal_take_ota_restart_requested(
                    client->management_portal)) {
                vTaskDelay(pdMS_TO_TICKS(750));
                esp_restart();
            }
            if (provisioning_portal_take_reset_requested(
                    client->management_portal)) {
                vTaskDelay(pdMS_TO_TICKS(500));
                esp_restart();
            }
            if (provisioning_portal_take_saved(client->management_portal, &saved)) {
                network_stop_management(client);
                network_reload_configuration(client);
                wake_time = xTaskGetTickCount();
                continue;
            }
            if (provisioning_portal_take_pairing_saved(
                    client->management_portal, &saved)) {
                client->config = saved;
                network_destroy_http(client);
                dashboard_store_set_pairing(
                    client->dashboard_store, false, NULL, 0, true);
                ESP_LOGI(TAG, "device token updated without reconnecting Wi-Fi");
            }
            char pairing_code[PAIRING_CODE_LENGTH + 1] = {0};
            if (provisioning_portal_pairing_code(
                    client->management_portal, now, pairing_code)) {
                uint32_t remaining =
                    provisioning_portal_pairing_remaining_seconds(
                        client->management_portal, now);
                dashboard_store_set_pairing(
                    client->dashboard_store, true, pairing_code,
                    remaining, false);
            }
        }
        network_update_freshness(client, network_now_ms());
        network_task_action_t action = network_task_next_action(
            client->configured, (bits & NETWORK_EVENT_GOT_IP) != 0,
            (bits & NETWORK_EVENT_COMMAND) != 0);

        if (action == NETWORK_TASK_RELOAD) {
            xEventGroupClearBits(client->events, NETWORK_EVENT_COMMAND);
            network_reload_configuration(client);
            wake_time = xTaskGetTickCount();
            continue;
        }
        if (action == NETWORK_TASK_WAIT) {
            if (!client->automatic_portal_attempted) {
                client->automatic_portal_attempted = true;
                if (network_start_portal(client) == ESP_OK) continue;
            }
            network_log_setup_if_due(client);
            network_wait_events(client, NETWORK_EVENT_COMMAND | NETWORK_EVENT_PROVISION, UINT32_MAX);
            continue;
        }
        if (action == NETWORK_TASK_RETRY) {
            network_destroy_http(client);
            uint32_t delay_ms = network_retry_delay_ms(++client->wifi_failures);
            bits = network_wait_events(client, NETWORK_EVENT_COMMAND | NETWORK_EVENT_GOT_IP,
                                       delay_ms);
            if (!(bits & (NETWORK_EVENT_COMMAND | NETWORK_EVENT_GOT_IP))) esp_wifi_connect();
            continue;
        }
        if (!client->management_portal && !client->management_start_attempted) {
            client->management_start_attempted = true;
            esp_err_t management_result = provisioning_portal_start_lan(
                &client->management_portal, client->config_store,
                client->device_control, network_now_ms());
            if (management_result != ESP_OK)
                ESP_LOGW(TAG, "failed to start LAN management: %s",
                         esp_err_to_name(management_result));
        }
        if (!client->http && network_create_http(client) != ESP_OK) {
            network_update_freshness(client, network_now_ms());
            network_delay_until_next_poll(client, &wake_time);
            continue;
        }

        network_poll(client);
        network_update_freshness(client, network_now_ms());
        bits = xEventGroupGetBits(client->events);
        if (bits & NETWORK_EVENT_COMMAND || !(bits & NETWORK_EVENT_GOT_IP)) continue;
        network_delay_until_next_poll(client, &wake_time);
    }
}

static void network_rollback_initialization(network_client_t *client)
{
    for (;;) {
        switch (network_init_cleanup_next(&client->init_stages)) {
        case NETWORK_CLEANUP_IP_HANDLER:
            esp_event_handler_unregister(IP_EVENT, ESP_EVENT_ANY_ID, network_station_event);
            break;
        case NETWORK_CLEANUP_WIFI_HANDLER:
            esp_event_handler_unregister(WIFI_EVENT, WIFI_EVENT_STA_DISCONNECTED,
                                         network_station_event);
            break;
        case NETWORK_CLEANUP_WIFI_STOP:
            esp_wifi_stop();
            break;
        case NETWORK_CLEANUP_DEFAULT_STA:
            esp_netif_destroy_default_wifi(client->station_netif);
            client->station_netif = NULL;
            break;
        case NETWORK_CLEANUP_DEFAULT_AP:
            esp_netif_destroy_default_wifi(client->ap_netif);
            client->ap_netif = NULL;
            break;
        case NETWORK_CLEANUP_WIFI_DEINIT:
            esp_wifi_deinit();
            break;
        case NETWORK_CLEANUP_DEFAULT_EVENT_LOOP:
            esp_event_loop_delete_default();
            break;
        case NETWORK_CLEANUP_DONE:
        default:
            return;
        }
    }
}

esp_err_t network_client_start(network_client_t **client, config_store_t *config_store,
                               dashboard_store_t *dashboard_store,
                               device_control_t *device_control)
{
    if (!client || !config_store || !dashboard_store || !device_control ||
        s_network_client || s_network_stack_initialized) {
        return ESP_ERR_INVALID_ARG;
    }
    network_client_t *context = calloc(1, sizeof(*context));
    if (!context) return ESP_ERR_NO_MEM;
    context->config_store = config_store;
    context->dashboard_store = dashboard_store;
    context->device_control = device_control;
    context->events = xEventGroupCreate();
    context->http_mutex = xSemaphoreCreateMutex();
    if (!context->events || !context->http_mutex) {
        if (context->events) vEventGroupDelete(context->events);
        if (context->http_mutex) vSemaphoreDelete(context->http_mutex);
        free(context);
        return ESP_ERR_NO_MEM;
    }

    esp_err_t result = esp_netif_init();
    if (result != ESP_OK && result != ESP_ERR_INVALID_STATE) goto failed;
    result = esp_event_loop_create_default();
    if (result == ESP_OK) context->init_stages |= NETWORK_INIT_DEFAULT_EVENT_LOOP;
    else if (result != ESP_ERR_INVALID_STATE) goto failed;
    context->station_netif = esp_netif_create_default_wifi_sta();
    if (!context->station_netif) {
        result = ESP_FAIL;
        goto failed;
    }
    context->init_stages |= NETWORK_INIT_DEFAULT_STA;
    context->ap_netif = esp_netif_create_default_wifi_ap();
    if (!context->ap_netif) {
        result = ESP_FAIL;
        goto failed;
    }
    context->init_stages |= NETWORK_INIT_DEFAULT_AP;
    wifi_init_config_t wifi_init = WIFI_INIT_CONFIG_DEFAULT();
    result = esp_wifi_init(&wifi_init);
    if (result != ESP_OK) goto failed;
    context->init_stages |= NETWORK_INIT_WIFI;
    result = esp_wifi_set_mode(WIFI_MODE_STA);
    if (result != ESP_OK) goto failed;
    result = esp_event_handler_register(WIFI_EVENT, WIFI_EVENT_STA_DISCONNECTED,
                                        network_station_event, context);
    if (result != ESP_OK) goto failed;
    context->init_stages |= NETWORK_INIT_WIFI_HANDLER;
    result = esp_event_handler_register(IP_EVENT, ESP_EVENT_ANY_ID, network_station_event, context);
    if (result != ESP_OK) goto failed;
    context->init_stages |= NETWORK_INIT_IP_HANDLER;
    if (xTaskCreate(network_task, "network_client", 8192, context, tskIDLE_PRIORITY + 1, NULL) != pdPASS) {
        result = ESP_ERR_NO_MEM;
        goto failed;
    }
    s_network_stack_initialized = true;
    s_network_client = context;
    *client = context;
    network_client_request_reconnect(context);
    return ESP_OK;

failed:
    network_rollback_initialization(context);
    vEventGroupDelete(context->events);
    vSemaphoreDelete(context->http_mutex);
    free(context);
    return result;
}

void network_client_request_reconnect(network_client_t *client)
{
    if (!client) return;
    xEventGroupSetBits(client->events, NETWORK_EVENT_COMMAND);
    if (xSemaphoreTake(client->http_mutex, portMAX_DELAY) != pdTRUE) return;
    if (client->http) esp_http_client_cancel_request(client->http);
    xSemaphoreGive(client->http_mutex);
}

bool network_client_pc_inactive(network_client_t *client, uint32_t now_ms,
                                uint32_t timeout_ms)
{
    if (!client) return false;
    return network_pc_inactive_elapsed(
        now_ms, client->last_valid_ms, client->ever_valid, timeout_ms);
}

bool network_pc_inactive_elapsed(uint32_t now_ms, uint32_t last_valid_ms,
                                 bool ever_valid, uint32_t timeout_ms)
{
    if (timeout_ms == 0) return false;
    if (!ever_valid) return now_ms >= timeout_ms;
    return (uint32_t)(now_ms - last_valid_ms) >= timeout_ms;
}

void network_client_request_provisioning(network_client_t *client)
{
    if (!client) return;
    xEventGroupSetBits(client->events, NETWORK_EVENT_PROVISION);
    if (xSemaphoreTake(client->http_mutex, portMAX_DELAY) != pdTRUE) return;
    if (client->http) esp_http_client_cancel_request(client->http);
    xSemaphoreGive(client->http_mutex);
}

void network_client_request_physical_action(network_client_t *client)
{
    if (!client) return;
    xEventGroupSetBits(client->events, NETWORK_EVENT_PHYSICAL);
    if (xSemaphoreTake(client->http_mutex, portMAX_DELAY) != pdTRUE) return;
    if (client->http) esp_http_client_cancel_request(client->http);
    xSemaphoreGive(client->http_mutex);
}

void network_client_request_mode_exit(network_client_t *client)
{
    if (!client) return;
    xEventGroupSetBits(client->events, NETWORK_EVENT_MODE_EXIT);
    if (xSemaphoreTake(client->http_mutex, portMAX_DELAY) != pdTRUE) return;
    if (client->http) esp_http_client_cancel_request(client->http);
    xSemaphoreGive(client->http_mutex);
}
