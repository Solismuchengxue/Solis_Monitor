#pragma once

#include "provisioning_portal.h"

#include "captive_dns.h"
#include "esp_http_server.h"
#include "freertos/FreeRTOS.h"
#include "freertos/semphr.h"

#define FORM_BODY_MAX 768

struct provisioning_portal {
    httpd_handle_t http;
    captive_dns_handle_t dns;
    config_store_t *store;
    device_control_t *device_control;
    SemaphoreHandle_t lock;
    volatile uint32_t last_activity_ms;
    bool expiring;
    bool saved;
    bool reset_requested;
    bool ota_allowed;
    bool ota_restart_requested;
    bool pairing_saved;
    network_config_t saved_config;
    char ap_ssid[33];
    bool pairing_active;
    char pairing_code[PAIRING_CODE_LENGTH + 1];
    char previous_pairing_code[PAIRING_CODE_LENGTH + 1];
    uint32_t pairing_code_started_ms;
    uint32_t previous_pairing_code_valid_until_ms;
};

extern provisioning_portal_t *g_provisioning_portal;

uint32_t provisioning_portal_now_ms(void);
void provisioning_portal_touch(provisioning_portal_t *portal);
bool provisioning_portal_form_value(
    const char *body, const char *key, char *output, size_t output_size);
esp_err_t provisioning_portal_send_json(
    httpd_req_t *request, const char *status, const char *body);

esp_err_t provisioning_portal_device_get(httpd_req_t *request);
esp_err_t provisioning_portal_ota_status_get(httpd_req_t *request);
esp_err_t provisioning_portal_control_get(httpd_req_t *request);
esp_err_t provisioning_portal_control_post(httpd_req_t *request);
esp_err_t provisioning_portal_restart_post(httpd_req_t *request);
esp_err_t provisioning_portal_ota_update_post(httpd_req_t *request);
esp_err_t provisioning_portal_pairing_post(httpd_req_t *request);
