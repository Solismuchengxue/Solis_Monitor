idf_component_register(
    SRCS "network_client.c"
         "provisioning_portal.c"
         "provisioning_portal_pairing.c"
         "provisioning_portal_device.c"
         "captive_dns.c"
    INCLUDE_DIRS "include"
    PRIV_INCLUDE_DIRS "private_include"
    REQUIRES esp_wifi esp_event esp_netif esp_http_client esp_http_server esp_app_format freertos lwip network_config metrics ota_update device_control
    EMBED_FILES "portal.html.gz"
)
