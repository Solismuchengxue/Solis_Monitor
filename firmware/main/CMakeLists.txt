idf_component_register(
    SRCS "app_main.c"
    INCLUDE_DIRS "."
    REQUIRES board display renderer ui metrics environment network_config network_client serial_setup nvs_flash esp_timer esp_netif ota_update device_control
)

if(SOLIS_OTA_ROLLBACK_TEST)
    target_compile_definitions(${COMPONENT_LIB} PRIVATE SOLIS_OTA_ROLLBACK_TEST=1)
endif()
