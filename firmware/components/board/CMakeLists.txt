idf_component_register(
    SRCS "board.c" "button_debounce.c" "environment_calibration.c"
    INCLUDE_DIRS "include"
    REQUIRES esp_driver_gpio esp_driver_ledc
)
