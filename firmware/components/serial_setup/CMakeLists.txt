idf_component_register(
    SRCS "serial_setup.c"
    INCLUDE_DIRS "include"
    REQUIRES network_config esp_system freertos esp_driver_uart
)
