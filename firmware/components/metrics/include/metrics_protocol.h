#pragma once

#include <stddef.h>
#include <stdint.h>

#include "dashboard_state.h"
#include "esp_err.h"

#define METRICS_PROTOCOL_MAX_BYTES 4096

typedef struct {
    uint64_t sequence;
    int64_t generated_at;
} metrics_metadata_t;

esp_err_t metrics_protocol_apply(const char *json, size_t length,
                                 const dashboard_state_t *previous,
                                 dashboard_state_t *next,
                                 metrics_metadata_t *metadata);
