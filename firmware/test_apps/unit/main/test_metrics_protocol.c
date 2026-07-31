#include <math.h>
#include <string.h>
#include <stdio.h>

#include "metrics_protocol.h"
#include "unity.h"

extern const char complete_json_start[]
    asm("_binary_metrics_complete_json_start");
extern const char complete_json_end[]
    asm("_binary_metrics_complete_json_end");

static size_t complete_json_length(void)
{
    size_t length = (size_t)(complete_json_end - complete_json_start);
    if (length > 0 && complete_json_start[length - 1] == '\0') length--;
    return length;
}

static void assert_unchanged(const dashboard_state_t *state, const dashboard_state_t *expected,
                             const metrics_metadata_t *metadata,
                             const metrics_metadata_t *expected_metadata)
{
    TEST_ASSERT_EQUAL_MEMORY(expected, state, sizeof(*state));
    TEST_ASSERT_EQUAL_MEMORY(expected_metadata, metadata, sizeof(*metadata));
}

static void make_metadata_json_with_keys(char *json, size_t json_size,
                                         const char *sequence_key, const char *generated_at_key,
                                         const char *sequence, const char *generated_at,
                                         const char *additional_root_fields)
{
    snprintf(json, json_size,
             "{\"schema\":1,\"%s\":%s,\"%s\":%s,%s"
             "\"system\":{\"time\":\"\",\"cpu_usage\":0,\"cpu_temp_c\":0,"
             "\"cpu_ghz\":0,\"cpu_w\":0,\"gpu_usage\":0,\"gpu_temp_c\":0,"
             "\"gpu_ghz\":0,\"gpu_w\":0,\"memory_usage\":0,\"fps\":0,"
             "\"nvme_temp_c\":0,\"download_mbps\":0,\"upload_mbps\":0},"
             "\"codex\":{\"online\":true,\"project\":\"\",\"context_used\":0,"
             "\"weekly_remaining\":0},\"environment\":{\"location\":\"\","
             "\"weather\":\"\",\"outdoor_low_c\":0,\"outdoor_high_c\":0,"
             "\"indoor_temp_c\":0,\"humidity\":0}}",
             sequence_key, sequence, generated_at_key, generated_at, additional_root_fields);
}

static void make_metadata_json(char *json, size_t json_size, const char *sequence,
                               const char *generated_at)
{
    make_metadata_json_with_keys(json, json_size, "sequence", "generated_at",
                                 sequence, generated_at, "");
}

static void make_nested_metadata_json(char *json, size_t json_size, const char *sequence,
                                       const char *generated_at)
{
    snprintf(json, json_size,
             "{\"schema\":1,\"system\":{\"sequence\":1,\"generated_at\":2,"
             "\"time\":\"\",\"cpu_usage\":0,\"cpu_temp_c\":0,\"cpu_ghz\":0,"
             "\"cpu_w\":0,\"gpu_usage\":0,\"gpu_temp_c\":0,\"gpu_ghz\":0,"
             "\"gpu_w\":0,\"memory_usage\":0,\"fps\":0,\"nvme_temp_c\":0,"
             "\"download_mbps\":0,\"upload_mbps\":0},\"codex\":{\"online\":true,"
             "\"project\":\"sequence generated_at\",\"context_used\":0,"
             "\"weekly_remaining\":0},\"environment\":{\"location\":\"\","
             "\"weather\":\"\",\"outdoor_low_c\":0,\"outdoor_high_c\":0,"
             "\"indoor_temp_c\":0,\"humidity\":0},\"sequence\":%s,\"generated_at\":%s}",
             sequence, generated_at);
}

static void make_availability_json(char *json, size_t json_size,
                                   const char *system_availability)
{
    snprintf(json, json_size,
             "{\"schema\":1,\"sequence\":7,\"generated_at\":8,"
             "\"system\":{\"time\":\"12:34\",\"cpu_usage\":10,\"cpu_temp_c\":20,"
             "\"cpu_ghz\":3.5,\"cpu_w\":40,\"gpu_usage\":50,\"gpu_temp_c\":null,"
             "\"gpu_ghz\":2.0,\"gpu_w\":80,\"memory_usage\":60,\"fps\":70,"
             "\"nvme_temp_c\":30,\"download_mbps\":100,\"upload_mbps\":20},"
             "\"codex\":{\"online\":true,\"project\":\"Solis_Monitor\","
             "\"context_used\":55,\"weekly_remaining\":75},"
             "\"environment\":{\"location\":\"大连\",\"weather\":\"晴\","
             "\"wind_direction\":\"东南风\",\"wind_scale\":\"4\","
             "\"weather_icon\":4,"
             "\"outdoor_low_c\":18,\"outdoor_high_c\":28,\"indoor_temp_c\":24,"
             "\"humidity\":50},\"availability\":{\"system\":{%s},"
             "\"codex\":{\"project\":false,\"context_used\":false},"
             "\"environment\":{\"weather\":false,\"outdoor_range\":false,"
             "\"indoor_temp_c\":true,\"humidity\":true,"
             "\"wind_direction\":false,\"wind_scale\":false,"
             "\"weather_icon\":false}}}",
             system_availability);
}

TEST_CASE("metadata preserves exact uint64 and int64 boundary values", "[metrics_protocol]")
{
    struct {
        const char *sequence;
        const char *generated_at;
        uint64_t expected_sequence;
        int64_t expected_generated_at;
    } cases[] = {
        {"9007199254740991", "-9223372036854775808", UINT64_C(9007199254740991), INT64_MIN},
        {"9007199254740993", "9223372036854775807", UINT64_C(9007199254740993), INT64_MAX},
        {"18446744073709551615", "0", UINT64_MAX, 0},
    };
    dashboard_state_t previous = {0};

    for (size_t index = 0; index < sizeof(cases) / sizeof(cases[0]); ++index) {
        char json[1024];
        dashboard_state_t next = {0};
        metrics_metadata_t metadata = {0};

        make_metadata_json(json, sizeof(json), cases[index].sequence, cases[index].generated_at);
        TEST_ASSERT_EQUAL(ESP_OK, metrics_protocol_apply(json, strlen(json), &previous,
                                                          &next, &metadata));
        TEST_ASSERT_EQUAL_UINT64(cases[index].expected_sequence, metadata.sequence);
        TEST_ASSERT_EQUAL_INT64(cases[index].expected_generated_at, metadata.generated_at);
    }
}

TEST_CASE("metadata rejects noninteger and out of range tokens transactionally", "[metrics_protocol]")
{
    struct {
        const char *sequence;
        const char *generated_at;
    } cases[] = {
        {"-1", "0"}, {"18446744073709551616", "0"}, {"1.0", "0"}, {"1e3", "0"},
        {"1", "9223372036854775808"}, {"1", "-9223372036854775809"},
        {"1", "1.0"}, {"1", "1e3"},
    };
    dashboard_state_t previous = {0};
    dashboard_state_t next = {.system = {.cpu_usage = 9}};
    metrics_metadata_t metadata = {.sequence = 9, .generated_at = 10};
    dashboard_state_t expected = next;
    metrics_metadata_t expected_metadata = metadata;

    for (size_t index = 0; index < sizeof(cases) / sizeof(cases[0]); ++index) {
        char json[1024];

        make_metadata_json(json, sizeof(json), cases[index].sequence, cases[index].generated_at);
        TEST_ASSERT_EQUAL(ESP_ERR_INVALID_ARG, metrics_protocol_apply(json, strlen(json), &previous,
                                                                        &next, &metadata));
        assert_unchanged(&next, &expected, &metadata, &expected_metadata);
    }
}

TEST_CASE("metadata scanner uses only exact root keys", "[metrics_protocol]")
{
    char json[1024];
    dashboard_state_t previous = {0};
    dashboard_state_t next = {0};
    metrics_metadata_t metadata = {0};

    make_nested_metadata_json(json, sizeof(json), "9007199254740993", "-9223372036854775808");
    TEST_ASSERT_EQUAL(ESP_OK, metrics_protocol_apply(json, strlen(json), &previous,
                                                      &next, &metadata));
    TEST_ASSERT_EQUAL_UINT64(UINT64_C(9007199254740993), metadata.sequence);
    TEST_ASSERT_EQUAL_INT64(INT64_MIN, metadata.generated_at);
}

TEST_CASE("metadata accepts escaped semantic keys exactly", "[metrics_protocol]")
{
    char json[1024];
    dashboard_state_t previous = {0};
    dashboard_state_t next = {0};
    metrics_metadata_t metadata = {0};

    make_metadata_json_with_keys(json, sizeof(json), "\\u0073equence", "generated_\\u0061t",
                                 "9007199254740993", "-9223372036854775808", "");
    TEST_ASSERT_EQUAL(ESP_OK, metrics_protocol_apply(json, strlen(json), &previous,
                                                      &next, &metadata));
    TEST_ASSERT_EQUAL_UINT64(UINT64_C(9007199254740993), metadata.sequence);
    TEST_ASSERT_EQUAL_INT64(INT64_MIN, metadata.generated_at);
}

TEST_CASE("metadata rejects escaped and literal semantic duplicates transactionally",
          "[metrics_protocol]")
{
    const char *duplicates[] = {
        "\"\\u0073equence\":2,", "\"generated_\\u0061t\":3,",
        "\"sequence\":2,\"generated_at\":3,",
    };
    dashboard_state_t previous = {0};
    dashboard_state_t next = {.system = {.cpu_usage = 9}};
    metrics_metadata_t metadata = {.sequence = 9, .generated_at = 10};
    dashboard_state_t expected = next;
    metrics_metadata_t expected_metadata = metadata;

    for (size_t index = 0; index < sizeof(duplicates) / sizeof(duplicates[0]); ++index) {
        char json[1024];

        make_metadata_json_with_keys(json, sizeof(json), "sequence", "generated_at", "1", "2",
                                     duplicates[index]);
        TEST_ASSERT_EQUAL(ESP_ERR_INVALID_ARG, metrics_protocol_apply(json, strlen(json), &previous,
                                                                        &next, &metadata));
        assert_unchanged(&next, &expected, &metadata, &expected_metadata);
    }
}

TEST_CASE("metadata rejects missing semantic keys transactionally", "[metrics_protocol]")
{
    const char *sequence_keys[] = {"missing", "sequence"};
    const char *generated_at_keys[] = {"generated_at", "missing"};
    dashboard_state_t previous = {0};
    dashboard_state_t next = {.system = {.cpu_usage = 9}};
    metrics_metadata_t metadata = {.sequence = 9, .generated_at = 10};
    dashboard_state_t expected = next;
    metrics_metadata_t expected_metadata = metadata;

    for (size_t index = 0; index < sizeof(sequence_keys) / sizeof(sequence_keys[0]); ++index) {
        char json[1024];

        make_metadata_json_with_keys(json, sizeof(json), sequence_keys[index],
                                     generated_at_keys[index], "1", "2", "");
        TEST_ASSERT_EQUAL(ESP_ERR_INVALID_ARG, metrics_protocol_apply(json, strlen(json), &previous,
                                                                        &next, &metadata));
        assert_unchanged(&next, &expected, &metadata, &expected_metadata);
    }
}

TEST_CASE("schema one payload maps every metric and metadata", "[metrics_protocol]")
{
    dashboard_state_t previous = {0};
    dashboard_state_t next = {0};
    metrics_metadata_t metadata = {0};

    TEST_ASSERT_EQUAL(ESP_OK, metrics_protocol_apply(complete_json_start, complete_json_length(),
                                                      &previous, &next, &metadata));
    TEST_ASSERT_TRUE(next.source_online);
    TEST_ASSERT_EQUAL_STRING("23:15", next.system.time);
    TEST_ASSERT_EQUAL_FLOAT(45, next.system.cpu_usage);
    TEST_ASSERT_EQUAL_FLOAT(63, next.system.cpu_temp_c);
    TEST_ASSERT_EQUAL_FLOAT(4.8f, next.system.cpu_ghz);
    TEST_ASSERT_EQUAL_FLOAT(95, next.system.cpu_w);
    TEST_ASSERT_EQUAL_STRING("Intel Core", next.system.cpu_name);
    TEST_ASSERT_EQUAL_FLOAT(81, next.system.gpu_usage);
    TEST_ASSERT_EQUAL_FLOAT(74, next.system.gpu_temp_c);
    TEST_ASSERT_EQUAL_FLOAT(2.6f, next.system.gpu_ghz);
    TEST_ASSERT_EQUAL_FLOAT(245, next.system.gpu_w);
    TEST_ASSERT_EQUAL_STRING("NVIDIA RTX", next.system.gpu_name);
    TEST_ASSERT_EQUAL_FLOAT(60, next.system.gpu_memory_usage);
    TEST_ASSERT_EQUAL_FLOAT(7373, next.system.gpu_memory_used_mb);
    TEST_ASSERT_EQUAL_FLOAT(12288, next.system.gpu_memory_total_mb);
    TEST_ASSERT_EQUAL_FLOAT(78, next.system.gpu_memory_temp_c);
    TEST_ASSERT_EQUAL_FLOAT(42, next.system.memory_usage);
    TEST_ASSERT_EQUAL_FLOAT(52, next.system.memory_temp_c);
    TEST_ASSERT_EQUAL_FLOAT(12, next.system.memory_used_gb);
    TEST_ASSERT_EQUAL_FLOAT(32, next.system.memory_total_gb);
    TEST_ASSERT_EQUAL_FLOAT(132, next.system.fps);
    TEST_ASSERT_EQUAL_FLOAT(40, next.system.nvme_temp_c);
    TEST_ASSERT_EQUAL_FLOAT(128, next.system.download_mbps);
    TEST_ASSERT_EQUAL_FLOAT(24, next.system.upload_mbps);
    TEST_ASSERT_EQUAL_STRING("以太网", next.system.network_name);
    TEST_ASSERT_EQUAL_UINT(2, next.system.storage_count);
    TEST_ASSERT_EQUAL_STRING("NVMe A", next.system.storage[0].name);
    TEST_ASSERT_EQUAL_FLOAT(65, next.system.storage[0].usage);
    TEST_ASSERT_EQUAL_FLOAT(40, next.system.storage[0].temp_c);
    TEST_ASSERT_TRUE(next.codex.online);
    TEST_ASSERT_EQUAL_STRING("Solis_Monitor", next.codex.project);
    TEST_ASSERT_EQUAL_STRING("当前任务", next.codex.task);
    TEST_ASSERT_EQUAL_STRING("gpt-5.6-sol", next.codex.model);
    TEST_ASSERT_EQUAL_STRING("high", next.codex.reasoning_effort);
    TEST_ASSERT_EQUAL_FLOAT(45, next.codex.context_used);
    TEST_ASSERT_EQUAL_FLOAT(90, next.codex.context_used_k);
    TEST_ASSERT_EQUAL_FLOAT(200, next.codex.context_window_k);
    TEST_ASSERT_EQUAL_FLOAT(123456, next.codex.total_tokens);
    TEST_ASSERT_EQUAL_FLOAT(45678, next.codex.weekly_used_tokens);
    TEST_ASSERT_EQUAL_FLOAT(0, next.codex.main_weekly_remaining);
    TEST_ASSERT_EQUAL_FLOAT(97, next.codex.spark_weekly_remaining);
    TEST_ASSERT_EQUAL_STRING("主周额度", next.codex.main_quota_name);
    TEST_ASSERT_EQUAL_STRING("07-29 08:46", next.codex.main_quota_reset_at);
    TEST_ASSERT_EQUAL_STRING("GPT-5.3-Codex-Spark", next.codex.spark_quota_name);
    TEST_ASSERT_EQUAL_STRING("07-29 08:47", next.codex.spark_quota_reset_at);
    TEST_ASSERT_EQUAL_STRING("辽宁·大连", next.environment.location);
    TEST_ASSERT_EQUAL_STRING("阵雨", next.environment.weather);
    TEST_ASSERT_EQUAL_STRING("东南风", next.environment.wind_direction);
    TEST_ASSERT_EQUAL_STRING("4", next.environment.wind_scale);
    TEST_ASSERT_EQUAL_FLOAT(26, next.environment.weather_icon);
    TEST_ASSERT_EQUAL_FLOAT(20, next.environment.outdoor_low_c);
    TEST_ASSERT_EQUAL_FLOAT(27, next.environment.outdoor_high_c);
    TEST_ASSERT_EQUAL_FLOAT(26, next.environment.indoor_temp_c);
    TEST_ASSERT_EQUAL_FLOAT(56, next.environment.humidity);
    TEST_ASSERT_EQUAL_UINT64(42, metadata.sequence);
    TEST_ASSERT_EQUAL_INT64(1784300000, metadata.generated_at);
}

TEST_CASE("availability false clears only its matching fields", "[metrics_protocol]")
{
    char json[2048];
    dashboard_state_t previous = {0};
    dashboard_state_t next = {0};
    metrics_metadata_t metadata = {0};

    make_availability_json(json, sizeof(json),
                           "\"cpu_usage\":true,\"cpu_temp_c\":false,"
                           "\"gpu_usage\":true,\"gpu_temp_c\":true,"
                           "\"download_mbps\":false,\"upload_mbps\":true");
    TEST_ASSERT_EQUAL(ESP_OK, metrics_protocol_apply(json, strlen(json), &previous,
                                                      &next, &metadata));
    TEST_ASSERT_EQUAL_FLOAT(10, next.system.cpu_usage);
    TEST_ASSERT_TRUE(isnan(next.system.cpu_temp_c));
    TEST_ASSERT_EQUAL_FLOAT(50, next.system.gpu_usage);
    TEST_ASSERT_TRUE(isnan(next.system.gpu_temp_c));
    TEST_ASSERT_TRUE(isnan(next.system.download_mbps));
    TEST_ASSERT_EQUAL_FLOAT(20, next.system.upload_mbps);
    TEST_ASSERT_EQUAL_STRING("", next.codex.project);
    TEST_ASSERT_TRUE(isnan(next.codex.context_used));
    TEST_ASSERT_EQUAL_FLOAT(75, next.codex.main_weekly_remaining);
    TEST_ASSERT_EQUAL_STRING("", next.environment.weather);
    TEST_ASSERT_EQUAL_STRING("", next.environment.wind_direction);
    TEST_ASSERT_EQUAL_STRING("", next.environment.wind_scale);
    TEST_ASSERT_TRUE(isnan(next.environment.weather_icon));
    TEST_ASSERT_TRUE(isnan(next.environment.outdoor_low_c));
    TEST_ASSERT_TRUE(isnan(next.environment.outdoor_high_c));
    TEST_ASSERT_EQUAL_FLOAT(24, next.environment.indoor_temp_c);
    TEST_ASSERT_EQUAL_FLOAT(50, next.environment.humidity);
}

TEST_CASE("invalid availability flag rejects transactionally", "[metrics_protocol]")
{
    char json[2048];
    dashboard_state_t previous = {.system = {.cpu_usage = 5}};
    dashboard_state_t next = {.system = {.cpu_usage = 9}};
    metrics_metadata_t metadata = {.sequence = 9, .generated_at = 10};
    dashboard_state_t expected = next;
    metrics_metadata_t expected_metadata = metadata;

    make_availability_json(json, sizeof(json), "\"cpu_usage\":1");
    TEST_ASSERT_EQUAL(ESP_ERR_INVALID_ARG, metrics_protocol_apply(
                                                   json, strlen(json), &previous,
                                                   &next, &metadata));
    assert_unchanged(&next, &expected, &metadata, &expected_metadata);
}

TEST_CASE("invalid schema and shape reject without altering outputs", "[metrics_protocol]")
{
    dashboard_state_t previous = {.system = {.cpu_usage = 5}};
    dashboard_state_t next = {.system = {.cpu_usage = 9}};
    metrics_metadata_t metadata = {.sequence = 9, .generated_at = 10};
    dashboard_state_t expected = next;
    metrics_metadata_t expected_metadata = metadata;
    const char bad_schema[] = "{\"schema\":2}";
    const char missing_system[] =
        "{\"schema\":1,\"sequence\":1,\"generated_at\":1,\"codex\":{},\"environment\":{}}";
    const char missing_codex[] =
        "{\"schema\":1,\"sequence\":1,\"generated_at\":1,\"system\":{},\"environment\":{}}";
    const char missing_environment[] =
        "{\"schema\":1,\"sequence\":1,\"generated_at\":1,\"system\":{},\"codex\":{}}";
    const char missing_field[] =
        "{\"schema\":1,\"sequence\":1,\"generated_at\":1,\"system\":{},\"codex\":{},\"environment\":{}}";

    TEST_ASSERT_EQUAL(ESP_ERR_NOT_SUPPORTED, metrics_protocol_apply(bad_schema, strlen(bad_schema),
                                                                      &previous, &next, &metadata));
    assert_unchanged(&next, &expected, &metadata, &expected_metadata);
    TEST_ASSERT_EQUAL(ESP_ERR_INVALID_ARG, metrics_protocol_apply(missing_system,
                                                                    strlen(missing_system),
                                                                    &previous, &next, &metadata));
    assert_unchanged(&next, &expected, &metadata, &expected_metadata);
    TEST_ASSERT_EQUAL(ESP_ERR_INVALID_ARG, metrics_protocol_apply(missing_codex,
                                                                    strlen(missing_codex),
                                                                    &previous, &next, &metadata));
    assert_unchanged(&next, &expected, &metadata, &expected_metadata);
    TEST_ASSERT_EQUAL(ESP_ERR_INVALID_ARG, metrics_protocol_apply(missing_environment,
                                                                    strlen(missing_environment),
                                                                    &previous, &next, &metadata));
    assert_unchanged(&next, &expected, &metadata, &expected_metadata);
    TEST_ASSERT_EQUAL(ESP_ERR_INVALID_ARG, metrics_protocol_apply(missing_field,
                                                                    strlen(missing_field),
                                                                    &previous, &next, &metadata));
    assert_unchanged(&next, &expected, &metadata, &expected_metadata);
}

TEST_CASE("null metrics clear corresponding prior fields", "[metrics_protocol]")
{
    dashboard_state_t previous = {.source_online = false,
                                  .system = {.time = "00:00", .cpu_usage = 2, .cpu_temp_c = 3,
                                             .cpu_ghz = 4, .cpu_w = 5, .gpu_usage = 6,
                                             .gpu_temp_c = 7, .gpu_ghz = 8, .gpu_w = 9,
                                             .memory_usage = 10, .fps = 11, .nvme_temp_c = 12,
                                             .download_mbps = 13, .upload_mbps = 14},
                                  .codex = {.online = true, .project = "旧项目",
                                            .context_used = 15, .main_weekly_remaining = 16},
                                   .environment = {.location = "大连", .weather = "晴",
                                                   .wind_direction = "东南风", .wind_scale = "4",
                                                   .weather_icon = 4,
                                                  .outdoor_low_c = 17, .outdoor_high_c = 18,
                                                  .indoor_temp_c = 19, .humidity = 20}};
    dashboard_state_t next = {0};
    metrics_metadata_t metadata = {0};
    const char all_null[] =
        "{\"schema\":1,\"sequence\":1,\"generated_at\":2,\"system\":{\"time\":null,"
        "\"cpu_usage\":null,\"cpu_temp_c\":null,\"cpu_ghz\":null,\"cpu_w\":null,"
        "\"gpu_usage\":null,\"gpu_temp_c\":null,\"gpu_ghz\":null,\"gpu_w\":null,"
        "\"memory_usage\":null,\"fps\":null,\"nvme_temp_c\":null,\"download_mbps\":null,"
        "\"upload_mbps\":null},\"codex\":{\"online\":null,\"project\":null,"
        "\"context_used\":null,\"weekly_remaining\":null},\"environment\":{\"location\":null,"
        "\"weather\":null,\"wind_direction\":null,\"wind_scale\":null,"
        "\"weather_icon\":null,"
        "\"outdoor_low_c\":null,\"outdoor_high_c\":null,"
        "\"indoor_temp_c\":null,\"humidity\":null}}";

    TEST_ASSERT_EQUAL(ESP_OK, metrics_protocol_apply(all_null, strlen(all_null), &previous,
                                                      &next, &metadata));
    TEST_ASSERT_TRUE(next.source_online);
    TEST_ASSERT_EQUAL_STRING("", next.system.time);
    TEST_ASSERT_TRUE(isnan(next.system.cpu_usage));
    TEST_ASSERT_TRUE(isnan(next.system.gpu_temp_c));
    TEST_ASSERT_TRUE(isnan(next.system.memory_usage));
    TEST_ASSERT_TRUE(isnan(next.system.download_mbps));
    TEST_ASSERT_FALSE(next.codex.online);
    TEST_ASSERT_EQUAL_STRING("", next.codex.project);
    TEST_ASSERT_TRUE(isnan(next.codex.context_used));
    TEST_ASSERT_TRUE(isnan(next.codex.main_weekly_remaining));
    TEST_ASSERT_EQUAL_STRING("", next.environment.location);
    TEST_ASSERT_EQUAL_STRING("", next.environment.weather);
    TEST_ASSERT_EQUAL_STRING("", next.environment.wind_direction);
    TEST_ASSERT_EQUAL_STRING("", next.environment.wind_scale);
    TEST_ASSERT_TRUE(isnan(next.environment.weather_icon));
    TEST_ASSERT_TRUE(isnan(next.environment.outdoor_low_c));
    TEST_ASSERT_TRUE(isnan(next.environment.indoor_temp_c));
    TEST_ASSERT_TRUE(isnan(next.environment.humidity));
    TEST_ASSERT_EQUAL_UINT64(1, metadata.sequence);
    TEST_ASSERT_EQUAL_INT64(2, metadata.generated_at);
}

TEST_CASE("malformed nonfinite and oversized payloads reject transactionally", "[metrics_protocol]")
{
    dashboard_state_t previous = {0};
    dashboard_state_t next = {.system = {.cpu_usage = 9}};
    metrics_metadata_t metadata = {.sequence = 9, .generated_at = 10};
    dashboard_state_t expected = next;
    metrics_metadata_t expected_metadata = metadata;
    char oversized[METRICS_PROTOCOL_MAX_BYTES + 1] = {0};
    const char malformed[] = "{";
    const char nonfinite[] =
        "{\"schema\":1,\"sequence\":1e9999,\"generated_at\":1,\"system\":{},\"codex\":{},\"environment\":{}}";

    memset(oversized, 'x', sizeof(oversized));
    TEST_ASSERT_EQUAL(ESP_ERR_INVALID_ARG, metrics_protocol_apply(malformed, strlen(malformed),
                                                                    &previous, &next, &metadata));
    assert_unchanged(&next, &expected, &metadata, &expected_metadata);
    TEST_ASSERT_EQUAL(ESP_ERR_INVALID_ARG, metrics_protocol_apply(nonfinite, strlen(nonfinite),
                                                                    &previous, &next, &metadata));
    assert_unchanged(&next, &expected, &metadata, &expected_metadata);
    TEST_ASSERT_EQUAL(ESP_ERR_INVALID_SIZE, metrics_protocol_apply(oversized, sizeof(oversized),
                                                                     &previous, &next, &metadata));
    assert_unchanged(&next, &expected, &metadata, &expected_metadata);
}

TEST_CASE("UTF-8 strings truncate only at code point boundaries", "[metrics_protocol]")
{
    dashboard_state_t previous = {0};
    dashboard_state_t next = {0};
    metrics_metadata_t metadata = {0};
    char json[1024];

    snprintf(json, sizeof(json),
             "{\"schema\":1,\"sequence\":1,\"generated_at\":1,\"system\":{\"time\":\"12:34\","
             "\"cpu_usage\":1,\"cpu_temp_c\":1,\"cpu_ghz\":1,\"cpu_w\":1,\"gpu_usage\":1,"
             "\"gpu_temp_c\":1,\"gpu_ghz\":1,\"gpu_w\":1,\"memory_usage\":1,\"fps\":1,"
             "\"nvme_temp_c\":1,\"download_mbps\":1,\"upload_mbps\":1},\"codex\":{\"online\":true,"
             "\"project\":\"123456789012345678901234567890中\",\"context_used\":1,\"weekly_remaining\":1},"
             "\"environment\":{\"location\":\"大连\",\"weather\":\"12345678901234567890123中\","
             "\"outdoor_low_c\":1,\"outdoor_high_c\":1,\"indoor_temp_c\":1,\"humidity\":1}}");

    TEST_ASSERT_EQUAL(ESP_OK, metrics_protocol_apply(json, strlen(json), &previous, &next, &metadata));
    TEST_ASSERT_EQUAL_STRING("123456789012345678901234567890", next.codex.project);
    TEST_ASSERT_EQUAL_STRING("12345678901234567890123", next.environment.weather);
}
