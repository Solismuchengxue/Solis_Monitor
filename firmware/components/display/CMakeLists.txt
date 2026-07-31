idf_component_register(
    SRCS "display.c"
    INCLUDE_DIRS "include"
    REQUIRES board renderer esp_lcd esp_lcd_nt35510
)
