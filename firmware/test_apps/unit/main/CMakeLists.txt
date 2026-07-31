idf_component_register(
    SRCS "test_main.c" "test_renderer.c" "test_button_debounce.c" "test_dashboard_state.c" "test_dashboard_store.c" "test_metrics_protocol.c" "test_ui.c" "test_board_config.c" "test_environment_calibration.c" "test_network_config.c" "test_config_store.c" "test_serial_setup.c" "test_network_timing.c" "test_provisioning_portal.c" "test_ota_update.c" "test_device_control.c"
    INCLUDE_DIRS "."
    REQUIRES unity renderer ui_assets board ui metrics network_config serial_setup network_client ota_update device_control
    EMBED_TXTFILES "fixtures/schema1/metrics_complete.json"
    WHOLE_ARCHIVE
)

target_include_directories(${COMPONENT_LIB} PRIVATE "../../../components/network_client/private_include")
