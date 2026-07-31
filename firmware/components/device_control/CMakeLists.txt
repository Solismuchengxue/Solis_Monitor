idf_component_register(
    SRCS "device_control.c"
    INCLUDE_DIRS "include"
    REQUIRES nvs_flash freertos
)
