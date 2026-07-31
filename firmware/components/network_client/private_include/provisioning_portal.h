#pragma once

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

#include "config_store.h"
#include "device_control.h"
#include "esp_err.h"
#include "esp_netif.h"

#define PROVISIONING_TIMEOUT_MS 600000U
#define PAIRING_CODE_LENGTH 6U
#define PAIRING_CODE_ROTATION_MS 60000U
#define PAIRING_CODE_GRACE_MS 10000U

typedef struct provisioning_portal provisioning_portal_t;

esp_err_t provisioning_form_parse(const char *body, const network_config_t *existing,
                                  bool has_existing, network_config_t *output);
bool provisioning_reset_confirmed(const char *body);
esp_err_t provisioning_device_control_parse(
    const char *body, device_control_settings_t *settings);
bool provisioning_bearer_token_matches(
    const char *authorization, const char *token);
bool provisioning_device_info_format(char *output, size_t output_size,
                                     const char *hostname, const char *firmware,
                                     const char *ip, int rssi, bool has_rssi,
                                     bool paired, bool pairing);
esp_err_t provisioning_pairing_token_apply(const char *body,
                                            const network_config_t *existing,
                                            network_config_t *output);
bool provisioning_pairing_code_matches(
    const char *submitted, const char *current, const char *previous,
    uint32_t previous_valid_until_ms, uint32_t now_ms);
esp_err_t provisioning_pairing_request_apply(
    const char *body, const network_config_t *existing,
    const char *current_code, const char *previous_code,
    uint32_t previous_valid_until_ms, uint32_t now_ms,
    network_config_t *output);
esp_err_t provisioning_portal_start(provisioning_portal_t **portal, esp_netif_t *ap_netif,
                                    config_store_t *store,
                                    device_control_t *device_control,
                                    const char *ssid, uint32_t now_ms);
esp_err_t provisioning_portal_start_lan(provisioning_portal_t **portal,
                                        config_store_t *store,
                                        device_control_t *device_control,
                                        uint32_t now_ms);
void provisioning_portal_stop(provisioning_portal_t *portal);
bool provisioning_portal_take_saved(provisioning_portal_t *portal, network_config_t *config);
bool provisioning_portal_take_reset_requested(provisioning_portal_t *portal);
bool provisioning_portal_take_ota_restart_requested(
    provisioning_portal_t *portal);
bool provisioning_portal_take_pairing_saved(provisioning_portal_t *portal,
                                            network_config_t *config);
uint32_t provisioning_portal_remaining_seconds(provisioning_portal_t *portal, uint32_t now_ms);
bool provisioning_portal_expired(provisioning_portal_t *portal, uint32_t now_ms);
esp_err_t provisioning_portal_begin_pairing(provisioning_portal_t *portal,
                                            uint32_t now_ms);
void provisioning_portal_end_pairing(provisioning_portal_t *portal);
bool provisioning_portal_pairing_active(provisioning_portal_t *portal,
                                        uint32_t now_ms);
uint32_t provisioning_portal_pairing_remaining_seconds(
    provisioning_portal_t *portal, uint32_t now_ms);
bool provisioning_portal_pairing_code(
    provisioning_portal_t *portal, uint32_t now_ms,
    char output[PAIRING_CODE_LENGTH + 1]);
