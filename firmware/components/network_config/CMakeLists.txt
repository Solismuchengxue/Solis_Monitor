idf_component_register(
    SRCS "network_config.c" "config_store.c" "nvs_config_backend.c"
    INCLUDE_DIRS "include"
    REQUIRES nvs_flash lwip
)
