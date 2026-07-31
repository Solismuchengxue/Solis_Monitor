#pragma once

#include <stdbool.h>
#include <stddef.h>

#include "config_store.h"
#include "esp_err.h"

typedef void (*serial_setup_reconnect_fn)(void *context);

size_t serial_setup_trim_line(char *line);
esp_err_t serial_setup_format_summary(const network_config_t *config, bool present,
                                      char *output, size_t output_size);
esp_err_t serial_setup_start(config_store_t *store,
                             serial_setup_reconnect_fn reconnect,
                             void *reconnect_context);
