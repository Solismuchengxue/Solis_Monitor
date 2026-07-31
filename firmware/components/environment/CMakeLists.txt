idf_component_register(
    SRCS "environment_sensor.c"
    INCLUDE_DIRS "include"
    REQUIRES board metrics esp-idf-lib__dht
)
