#include "metrics_protocol.h"

#include <math.h>
#include <stdbool.h>
#include <string.h>

#include "cJSON.h"

static const char *skip_whitespace(const char *cursor, const char *end)
{
    while (cursor < end && (*cursor == ' ' || *cursor == '\t' ||
                            *cursor == '\r' || *cursor == '\n')) cursor++;
    return cursor;
}

static const char *skip_json_string(const char *cursor, const char *end)
{
    if (cursor >= end || *cursor != '"') return NULL;
    for (cursor++; cursor < end; cursor++) {
        if (*cursor == '"') return cursor + 1;
        if (*cursor == '\\') {
            cursor++;
            if (cursor >= end) return NULL;
        }
    }
    return NULL;
}

static const char *skip_json_value(const char *cursor, const char *end)
{
    cursor = skip_whitespace(cursor, end);
    if (cursor >= end) return NULL;
    if (*cursor == '"') return skip_json_string(cursor, end);

    if (*cursor == '{' || *cursor == '[') {
        char close = *cursor == '{' ? '}' : ']';
        bool object = *cursor == '{';
        cursor = skip_whitespace(cursor + 1, end);
        if (cursor < end && *cursor == close) return cursor + 1;

        for (;;) {
            if (object) {
                cursor = skip_json_string(cursor, end);
                if (!cursor) return NULL;
                cursor = skip_whitespace(cursor, end);
                if (cursor >= end || *cursor != ':') return NULL;
                cursor++;
            }
            cursor = skip_json_value(cursor, end);
            if (!cursor) return NULL;
            cursor = skip_whitespace(cursor, end);
            if (cursor >= end) return NULL;
            if (*cursor == close) return cursor + 1;
            if (*cursor != ',') return NULL;
            cursor = skip_whitespace(cursor + 1, end);
        }
    }

    const char *start = cursor;
    while (cursor < end && *cursor != ',' && *cursor != '}' && *cursor != ']' &&
           *cursor != ' ' && *cursor != '\t' && *cursor != '\r' && *cursor != '\n') {
        cursor++;
    }
    return cursor == start ? NULL : cursor;
}

static bool parse_uint64_token(const char *start, const char *end, uint64_t *value);
static bool parse_int64_token(const char *start, const char *end, int64_t *value);

static bool decode_hex4(const char *cursor, const char *end, uint32_t *value)
{
    if (end - cursor < 4) return false;

    uint32_t result = 0;
    for (size_t index = 0; index < 4; ++index) {
        char character = cursor[index];
        uint32_t digit;

        if (character >= '0' && character <= '9') digit = (uint32_t)(character - '0');
        else if (character >= 'a' && character <= 'f') digit = (uint32_t)(character - 'a' + 10);
        else if (character >= 'A' && character <= 'F') digit = (uint32_t)(character - 'A' + 10);
        else return false;
        result = result * 16 + digit;
    }
    *value = result;
    return true;
}

static bool json_key_equals(const char *start, const char *end, const char *name)
{
    const char *cursor = start + 1;
    const char *limit = end - 1;
    size_t name_index = 0;

    while (cursor < limit) {
        uint32_t codepoint;

        if (*cursor != '\\') {
            codepoint = (unsigned char)*cursor++;
        } else {
            if (++cursor >= limit) return false;
            switch (*cursor++) {
            case '"': codepoint = '"'; break;
            case '\\': codepoint = '\\'; break;
            case '/': codepoint = '/'; break;
            case 'b': codepoint = '\b'; break;
            case 'f': codepoint = '\f'; break;
            case 'n': codepoint = '\n'; break;
            case 'r': codepoint = '\r'; break;
            case 't': codepoint = '\t'; break;
            case 'u':
                if (!decode_hex4(cursor, limit, &codepoint)) return false;
                cursor += 4;
                if (codepoint >= 0xD800 && codepoint <= 0xDBFF) {
                    uint32_t low;

                    if (limit - cursor < 6 || cursor[0] != '\\' || cursor[1] != 'u' ||
                        !decode_hex4(cursor + 2, limit, &low) ||
                        low < 0xDC00 || low > 0xDFFF) {
                        return false;
                    }
                    codepoint = 0x10000 + ((codepoint - 0xD800) << 10) + (low - 0xDC00);
                    cursor += 6;
                } else if (codepoint >= 0xDC00 && codepoint <= 0xDFFF) {
                    return false;
                }
                break;
            default:
                return false;
            }
        }

        if (name[name_index] == '\0' || codepoint != (unsigned char)name[name_index]) return false;
        name_index++;
    }
    return name[name_index] == '\0';
}

static bool scan_root_metadata(const char *json, size_t length,
                               uint64_t *sequence, int64_t *generated_at)
{
    const char *end = json + length;
    const char *cursor = skip_whitespace(json, end);
    const char *sequence_start = NULL;
    const char *sequence_end = NULL;
    const char *generated_at_start = NULL;
    const char *generated_at_end = NULL;

    if (cursor >= end || *cursor != '{') return false;
    cursor = skip_whitespace(cursor + 1, end);
    while (cursor < end && *cursor != '}') {
        const char *key_start = cursor;
        cursor = skip_json_string(cursor, end);
        if (!cursor) return false;
        const char *key_end = cursor;
        cursor = skip_whitespace(cursor, end);
        if (cursor >= end || *cursor != ':') return false;
        cursor = skip_whitespace(cursor + 1, end);
        const char *value_start = cursor;
        cursor = skip_json_value(cursor, end);
        if (!cursor) return false;

        if (json_key_equals(key_start, key_end, "sequence")) {
            if (sequence_start) return false;
            sequence_start = value_start;
            sequence_end = cursor;
        } else if (json_key_equals(key_start, key_end, "generated_at")) {
            if (generated_at_start) return false;
            generated_at_start = value_start;
            generated_at_end = cursor;
        }

        cursor = skip_whitespace(cursor, end);
        if (cursor >= end || *cursor == '}') break;
        if (*cursor != ',') return false;
        cursor = skip_whitespace(cursor + 1, end);
    }

    return sequence_start && generated_at_start &&
           parse_uint64_token(sequence_start, sequence_end, sequence) &&
           parse_int64_token(generated_at_start, generated_at_end, generated_at);
}

static bool parse_uint64_token(const char *start, const char *end, uint64_t *value)
{
    if (start >= end || *start < '0' || *start > '9') return false;
    if (*start == '0' && start + 1 != end) return false;

    uint64_t result = 0;
    for (const char *cursor = start; cursor < end; ++cursor) {
        if (*cursor < '0' || *cursor > '9') return false;
        uint64_t digit = (uint64_t)(*cursor - '0');
        if (result > (UINT64_MAX - digit) / 10) return false;
        result = result * 10 + digit;
    }
    *value = result;
    return true;
}

static bool parse_int64_token(const char *start, const char *end, int64_t *value)
{
    bool negative = start < end && *start == '-';
    uint64_t magnitude;

    if (negative) start++;
    if (!parse_uint64_token(start, end, &magnitude)) return false;
    if (!negative && magnitude > INT64_MAX) return false;
    if (negative && magnitude > (uint64_t)INT64_MAX + 1) return false;

    if (negative && magnitude == (uint64_t)INT64_MAX + 1) *value = INT64_MIN;
    else if (negative) *value = -(int64_t)magnitude;
    else *value = (int64_t)magnitude;
    return true;
}

static bool read_metadata_tokens(const char *json, size_t length,
                                 uint64_t *sequence, int64_t *generated_at)
{
    return scan_root_metadata(json, length, sequence, generated_at);
}

static bool read_number(const cJSON *object, const char *name, float *value)
{
    const cJSON *item = cJSON_GetObjectItemCaseSensitive(object, name);

    if (!item) return false;
    if (cJSON_IsNull(item)) {
        *value = NAN;
        return true;
    }
    if (!cJSON_IsNumber(item) || !isfinite(item->valuedouble)) return false;
    *value = (float)item->valuedouble;
    return isfinite(*value);
}

static bool read_optional_number(const cJSON *object, const char *name, float *value)
{
    const cJSON *item = cJSON_GetObjectItemCaseSensitive(object, name);
    if (!item)
    {
        *value = NAN;
        return true;
    }
    if (cJSON_IsNull(item))
    {
        *value = NAN;
        return true;
    }
    if (!cJSON_IsNumber(item) || !isfinite(item->valuedouble))
        return false;
    *value = (float)item->valuedouble;
    return isfinite(*value);
}

static bool read_number_when_present(const cJSON *object, const char *name, float *value)
{
    const cJSON *item = cJSON_GetObjectItemCaseSensitive(object, name);
    if (!item) return true;
    if (cJSON_IsNull(item)) {
        *value = NAN;
        return true;
    }
    if (!cJSON_IsNumber(item) || !isfinite(item->valuedouble)) return false;
    *value = (float)item->valuedouble;
    return isfinite(*value);
}

static void copy_utf8(char *destination, size_t destination_size, const char *source)
{
    size_t source_length = strlen(source);
    size_t limit = source_length < destination_size - 1 ? source_length : destination_size - 1;
    size_t offset = 0;
    size_t copied = 0;

    while (offset < limit) {
        unsigned char lead = (unsigned char)source[offset];
        size_t width = 1;

        if (lead >= 0xC2 && lead <= 0xDF) width = 2;
        else if (lead >= 0xE0 && lead <= 0xEF) width = 3;
        else if (lead >= 0xF0 && lead <= 0xF4) width = 4;
        if (offset + width > limit) break;

        for (size_t index = 1; index < width; ++index) {
            if (((unsigned char)source[offset + index] & 0xC0) != 0x80) {
                width = 0;
                break;
            }
        }
        if (width == 0) break;
        offset += width;
        copied = offset;
    }

    memcpy(destination, source, copied);
    destination[copied] = '\0';
}

static bool read_string(const cJSON *object, const char *name, char *value, size_t value_size)
{
    const cJSON *item = cJSON_GetObjectItemCaseSensitive(object, name);

    if (!item) return false;
    if (cJSON_IsNull(item)) {
        value[0] = '\0';
        return true;
    }
    if (!cJSON_IsString(item) || !item->valuestring) return false;
    copy_utf8(value, value_size, item->valuestring);
    return true;
}

static bool read_optional_string(const cJSON *object, const char *name, char *value, size_t value_size)
{
    const cJSON *item = cJSON_GetObjectItemCaseSensitive(object, name);

    if (!item)
    {
        value[0] = '\0';
        return true;
    }
    if (cJSON_IsNull(item))
    {
        value[0] = '\0';
        return true;
    }
    if (!cJSON_IsString(item) || !item->valuestring)
        return false;
    copy_utf8(value, value_size, item->valuestring);
    return true;
}

static bool read_bool(const cJSON *object, const char *name, bool *value)
{
    const cJSON *item = cJSON_GetObjectItemCaseSensitive(object, name);

    if (!item) return false;
    if (cJSON_IsNull(item)) {
        *value = false;
        return true;
    }
    if (!cJSON_IsBool(item)) return false;
    *value = cJSON_IsTrue(item);
    return true;
}

static bool read_availability_flag(const cJSON *object, const char *name,
                                   bool *present, bool *available)
{
    const cJSON *item = cJSON_GetObjectItemCaseSensitive(object, name);
    if (!item) {
        *present = false;
        return true;
    }
    if (!cJSON_IsBool(item)) return false;
    *present = true;
    *available = cJSON_IsTrue(item);
    return true;
}

typedef struct {
    const char *name;
    float *value;
} number_availability_t;

typedef struct {
    const char *name;
    char *value;
} string_availability_t;

static bool apply_number_availability(const cJSON *object,
                                      const number_availability_t *fields,
                                      size_t count)
{
    for (size_t index = 0; index < count; ++index) {
        bool present;
        bool available;
        if (!read_availability_flag(object, fields[index].name, &present, &available))
            return false;
        if (present && !available) *fields[index].value = NAN;
    }
    return true;
}

static bool apply_string_availability(const cJSON *object,
                                      const string_availability_t *fields,
                                      size_t count)
{
    for (size_t index = 0; index < count; ++index) {
        bool present;
        bool available;
        if (!read_availability_flag(object, fields[index].name, &present, &available))
            return false;
        if (present && !available) fields[index].value[0] = '\0';
    }
    return true;
}

static bool apply_system_availability(const cJSON *object, system_metrics_t *system)
{
    number_availability_t fields[] = {
        {"cpu_usage", &system->cpu_usage},
        {"cpu_temp_c", &system->cpu_temp_c},
        {"cpu_ghz", &system->cpu_ghz},
        {"cpu_w", &system->cpu_w},
        {"gpu_usage", &system->gpu_usage},
        {"gpu_temp_c", &system->gpu_temp_c},
        {"gpu_ghz", &system->gpu_ghz},
        {"gpu_w", &system->gpu_w},
        {"gpu_memory_usage", &system->gpu_memory_usage},
        {"gpu_memory_used_mb", &system->gpu_memory_used_mb},
        {"gpu_memory_total_mb", &system->gpu_memory_total_mb},
        {"gpu_memory_temp_c", &system->gpu_memory_temp_c},
        {"memory_usage", &system->memory_usage},
        {"memory_temp_c", &system->memory_temp_c},
        {"fps", &system->fps},
        {"nvme_temp_c", &system->nvme_temp_c},
        {"download_mbps", &system->download_mbps},
        {"upload_mbps", &system->upload_mbps},
    };
    return apply_number_availability(object, fields, sizeof(fields) / sizeof(fields[0]));
}

static bool apply_codex_availability(const cJSON *object, codex_metrics_t *codex)
{
    number_availability_t numbers[] = {
        {"context_used", &codex->context_used},
        {"context_used_k", &codex->context_used_k},
        {"context_window_k", &codex->context_window_k},
        {"weekly_remaining", &codex->main_weekly_remaining},
        {"main_weekly_remaining", &codex->main_weekly_remaining},
        {"spark_weekly_remaining", &codex->spark_weekly_remaining},
    };
    string_availability_t strings[] = {
        {"project", codex->project},
        {"main_quota_name", codex->main_quota_name},
        {"main_quota_reset_at", codex->main_quota_reset_at},
        {"spark_quota_name", codex->spark_quota_name},
        {"spark_quota_reset_at", codex->spark_quota_reset_at},
    };
    return apply_number_availability(object, numbers, sizeof(numbers) / sizeof(numbers[0])) &&
           apply_string_availability(object, strings, sizeof(strings) / sizeof(strings[0]));
}

static bool apply_environment_availability(const cJSON *object,
                                           environment_metrics_t *environment)
{
    number_availability_t numbers[] = {
        {"indoor_temp_c", &environment->indoor_temp_c},
        {"humidity", &environment->humidity},
        {"weather_icon", &environment->weather_icon},
    };
    string_availability_t strings[] = {
        {"weather", environment->weather},
        {"wind_direction", environment->wind_direction},
        {"wind_scale", environment->wind_scale},
    };
    bool range_present;
    bool range_available;

    if (!apply_number_availability(object, numbers, sizeof(numbers) / sizeof(numbers[0])) ||
        !apply_string_availability(object, strings, sizeof(strings) / sizeof(strings[0])) ||
        !read_availability_flag(object, "outdoor_range", &range_present, &range_available)) {
        return false;
    }
    if (range_present && !range_available) {
        environment->outdoor_low_c = NAN;
        environment->outdoor_high_c = NAN;
    }
    return true;
}

static bool apply_availability(const cJSON *root, dashboard_state_t *state)
{
    const cJSON *availability = cJSON_GetObjectItemCaseSensitive(root, "availability");
    if (!availability) return true;
    if (!cJSON_IsObject(availability)) return false;

    const cJSON *system = cJSON_GetObjectItemCaseSensitive(availability, "system");
    const cJSON *codex = cJSON_GetObjectItemCaseSensitive(availability, "codex");
    const cJSON *environment = cJSON_GetObjectItemCaseSensitive(availability, "environment");
    if (!cJSON_IsObject(system) || !cJSON_IsObject(codex) || !cJSON_IsObject(environment))
        return false;

    return apply_system_availability(system, &state->system) &&
           apply_codex_availability(codex, &state->codex) &&
           apply_environment_availability(environment, &state->environment);
}

static bool read_storage_devices(const cJSON *object, system_metrics_t *system)
{
    const cJSON *array = cJSON_GetObjectItemCaseSensitive(object, "storage_devices");
    if (!array || cJSON_IsNull(array)) {
        system->storage_count = 0;
        return true;
    }
    if (!cJSON_IsArray(array)) return false;

    system->storage_count = 0;
    int count = cJSON_GetArraySize(array);
    for (int index = 0; index < count && system->storage_count < DASHBOARD_MAX_STORAGE_DEVICES;
         ++index) {
        const cJSON *item = cJSON_GetArrayItem(array, index);
        if (!cJSON_IsObject(item)) return false;

        storage_device_metrics_t *storage = &system->storage[system->storage_count];
        storage->usage = NAN;
        storage->temp_c = NAN;
        if (!read_string(item, "name", storage->name, sizeof(storage->name)) ||
            !read_optional_number(item, "usage", &storage->usage) ||
            !read_optional_number(item, "temp_c", &storage->temp_c)) {
            return false;
        }
        system->storage_count++;
    }
    return true;
}

static bool read_system(const cJSON *object, system_metrics_t *system)
{
    return read_string(object, "time", system->time, sizeof(system->time)) &&
           read_optional_string(object, "cpu_name", system->cpu_name, sizeof(system->cpu_name)) &&
           read_number(object, "cpu_usage", &system->cpu_usage) &&
           read_number(object, "cpu_temp_c", &system->cpu_temp_c) &&
           read_number(object, "cpu_ghz", &system->cpu_ghz) &&
           read_number(object, "cpu_w", &system->cpu_w) &&
           read_optional_string(object, "gpu_name", system->gpu_name, sizeof(system->gpu_name)) &&
           read_number(object, "gpu_usage", &system->gpu_usage) &&
           read_number(object, "gpu_temp_c", &system->gpu_temp_c) &&
           read_number(object, "gpu_ghz", &system->gpu_ghz) &&
           read_number(object, "gpu_w", &system->gpu_w) &&
           read_optional_number(object, "gpu_memory_usage", &system->gpu_memory_usage) &&
           read_optional_number(object, "gpu_memory_used_mb", &system->gpu_memory_used_mb) &&
           read_optional_number(object, "gpu_memory_total_mb", &system->gpu_memory_total_mb) &&
           read_optional_number(object, "gpu_memory_temp_c", &system->gpu_memory_temp_c) &&
           read_number(object, "memory_usage", &system->memory_usage) &&
           read_optional_number(object, "memory_temp_c", &system->memory_temp_c) &&
           read_optional_number(object, "memory_used_gb", &system->memory_used_gb) &&
           read_optional_number(object, "memory_total_gb", &system->memory_total_gb) &&
           read_number(object, "fps", &system->fps) &&
           read_number(object, "nvme_temp_c", &system->nvme_temp_c) &&
           read_number(object, "download_mbps", &system->download_mbps) &&
           read_number(object, "upload_mbps", &system->upload_mbps) &&
           read_optional_string(object, "network_name", system->network_name,
                                sizeof(system->network_name)) &&
           read_storage_devices(object, system);
}

static bool read_codex(const cJSON *object, codex_metrics_t *codex)
{
    return read_bool(object, "online", &codex->online) &&
           read_string(object, "project", codex->project, sizeof(codex->project)) &&
           read_optional_string(object, "task", codex->task, sizeof(codex->task)) &&
           read_optional_string(object, "model", codex->model, sizeof(codex->model)) &&
           read_optional_string(object, "reasoning_effort", codex->reasoning_effort,
                                sizeof(codex->reasoning_effort)) &&
           read_optional_number(object, "context_used", &codex->context_used) &&
           read_optional_number(object, "context_used_k", &codex->context_used_k) &&
           read_optional_number(object, "context_window_k", &codex->context_window_k) &&
           read_optional_number(object, "total_tokens", &codex->total_tokens) &&
           read_optional_number(object, "weekly_used_tokens", &codex->weekly_used_tokens) &&
           read_optional_number(object, "weekly_remaining", &codex->main_weekly_remaining) &&
           read_number_when_present(object, "main_weekly_remaining",
                                    &codex->main_weekly_remaining) &&
           read_optional_string(object, "main_quota_name", codex->main_quota_name,
                                sizeof(codex->main_quota_name)) &&
           read_optional_string(object, "main_quota_reset_at", codex->main_quota_reset_at,
                               sizeof(codex->main_quota_reset_at)) &&
           read_optional_number(object, "spark_weekly_remaining",
                                &codex->spark_weekly_remaining) &&
           read_optional_string(object, "spark_quota_name", codex->spark_quota_name,
                               sizeof(codex->spark_quota_name)) &&
           read_optional_string(object, "spark_quota_reset_at", codex->spark_quota_reset_at,
                               sizeof(codex->spark_quota_reset_at));
}

static bool read_environment(const cJSON *object, environment_metrics_t *environment)
{
    return read_string(object, "location", environment->location, sizeof(environment->location)) &&
           read_string(object, "weather", environment->weather, sizeof(environment->weather)) &&
           read_optional_string(object, "wind_direction", environment->wind_direction,
                                sizeof(environment->wind_direction)) &&
           read_optional_string(object, "wind_scale", environment->wind_scale,
                                sizeof(environment->wind_scale)) &&
           read_optional_number(object, "weather_icon", &environment->weather_icon) &&
           read_number(object, "outdoor_low_c", &environment->outdoor_low_c) &&
           read_number(object, "outdoor_high_c", &environment->outdoor_high_c) &&
           read_number(object, "indoor_temp_c", &environment->indoor_temp_c) &&
           read_number(object, "humidity", &environment->humidity);
}

esp_err_t metrics_protocol_apply(const char *json, size_t length,
                                 const dashboard_state_t *previous,
                                 dashboard_state_t *next,
                                 metrics_metadata_t *metadata)
{
    if (!json || !previous || !next || !metadata) return ESP_ERR_INVALID_ARG;
    if (length > METRICS_PROTOCOL_MAX_BYTES) return ESP_ERR_INVALID_SIZE;

    esp_err_t result = ESP_ERR_INVALID_ARG;
    cJSON *root = cJSON_ParseWithLength(json, length);
    dashboard_state_t candidate;
    metrics_metadata_t candidate_metadata;

    if (!root || !cJSON_IsObject(root)) goto cleanup;

    const cJSON *schema = cJSON_GetObjectItemCaseSensitive(root, "schema");
    if (!cJSON_IsNumber(schema) || !isfinite(schema->valuedouble) || schema->valuedouble != 1) {
        result = cJSON_IsNumber(schema) ? ESP_ERR_NOT_SUPPORTED : ESP_ERR_INVALID_ARG;
        goto cleanup;
    }

    uint64_t sequence;
    int64_t generated_at;
    if (!cJSON_IsNumber(cJSON_GetObjectItemCaseSensitive(root, "sequence")) ||
        !cJSON_IsNumber(cJSON_GetObjectItemCaseSensitive(root, "generated_at")) ||
        !read_metadata_tokens(json, length, &sequence, &generated_at)) {
        goto cleanup;
    }

    const cJSON *system = cJSON_GetObjectItemCaseSensitive(root, "system");
    const cJSON *codex = cJSON_GetObjectItemCaseSensitive(root, "codex");
    const cJSON *environment = cJSON_GetObjectItemCaseSensitive(root, "environment");
    if (!cJSON_IsObject(system) || !cJSON_IsObject(codex) || !cJSON_IsObject(environment)) {
        goto cleanup;
    }

    candidate = *previous;
    if (!read_system(system, &candidate.system) || !read_codex(codex, &candidate.codex) ||
        !read_environment(environment, &candidate.environment) ||
        !apply_availability(root, &candidate)) {
        goto cleanup;
    }

    candidate.source_online = true;
    dashboard_state_sanitize(&candidate);
    candidate_metadata.sequence = sequence;
    candidate_metadata.generated_at = generated_at;
    *next = candidate;
    *metadata = candidate_metadata;
    result = ESP_OK;

cleanup:
    cJSON_Delete(root);
    return result;
}
