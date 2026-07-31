idf_component_register(
    SRCS "dashboard_state.c" "ui.c"
    INCLUDE_DIRS "include"
    REQUIRES renderer ui_assets
)
