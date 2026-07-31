idf_component_register(
    SRCS "dashboard_store.c" "metrics_protocol.c"
    INCLUDE_DIRS "include"
    REQUIRES ui freertos
)
