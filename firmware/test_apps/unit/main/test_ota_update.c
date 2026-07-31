#include <string.h>

#include "esp_app_desc.h"
#include "esp_app_format.h"
#include "ota_update.h"
#include "unity.h"

static size_t make_image_header(uint8_t *buffer, size_t capacity,
                                const char *project, const char *version,
                                esp_chip_id_t chip_id)
{
    const size_t required = sizeof(esp_image_header_t) +
                            sizeof(esp_image_segment_header_t) +
                            sizeof(esp_app_desc_t);
    TEST_ASSERT_GREATER_OR_EQUAL(required, capacity);
    memset(buffer, 0, capacity);

    esp_image_header_t *image = (esp_image_header_t *)buffer;
    image->magic = ESP_IMAGE_HEADER_MAGIC;
    image->chip_id = chip_id;

    esp_app_desc_t *description = (esp_app_desc_t *)(
        buffer + sizeof(esp_image_header_t) +
        sizeof(esp_image_segment_header_t));
    description->magic_word = ESP_APP_DESC_MAGIC_WORD;
    snprintf(description->project_name, sizeof(description->project_name),
             "%s", project);
    snprintf(description->version, sizeof(description->version), "%s", version);
    return required;
}

TEST_CASE("OTA image header accepts the Solis ESP32-S3 firmware", "[ota]")
{
    uint8_t header[sizeof(esp_image_header_t) +
                   sizeof(esp_image_segment_header_t) +
                   sizeof(esp_app_desc_t)];
    size_t header_size = make_image_header(
        header, sizeof(header), "solis_monitor", "1.2.3",
        ESP_CHIP_ID_ESP32S3);
    ota_image_info_t info = {0};

    TEST_ASSERT_EQUAL(
        ESP_OK,
        ota_update_image_inspect(
            header, header_size, 1024, 4096, &info));
    TEST_ASSERT_EQUAL_STRING("solis_monitor", info.project_name);
    TEST_ASSERT_EQUAL_STRING("1.2.3", info.version);
    TEST_ASSERT_EQUAL_UINT16(ESP_CHIP_ID_ESP32S3, info.chip_id);
}

TEST_CASE("OTA image header rejects incompatible or oversized firmware", "[ota]")
{
    uint8_t header[sizeof(esp_image_header_t) +
                   sizeof(esp_image_segment_header_t) +
                   sizeof(esp_app_desc_t)];
    ota_image_info_t info = {0};

    size_t header_size = make_image_header(
        header, sizeof(header), "other_project", "1.2.3",
        ESP_CHIP_ID_ESP32S3);
    TEST_ASSERT_EQUAL(
        ESP_ERR_INVALID_ARG,
        ota_update_image_inspect(
            header, header_size, 1024, 4096, &info));

    header_size = make_image_header(
        header, sizeof(header), "solis_monitor", "1.2.3",
        ESP_CHIP_ID_ESP32);
    TEST_ASSERT_EQUAL(
        ESP_ERR_INVALID_VERSION,
        ota_update_image_inspect(
            header, header_size, 1024, 4096, &info));

    header_size = make_image_header(
        header, sizeof(header), "solis_monitor", "1.2.3",
        ESP_CHIP_ID_ESP32S3);
    TEST_ASSERT_EQUAL(
        ESP_ERR_INVALID_SIZE,
        ota_update_image_inspect(
            header, header_size, 4097, 4096, &info));

    header[0] = 0;
    TEST_ASSERT_EQUAL(
        ESP_ERR_INVALID_RESPONSE,
        ota_update_image_inspect(
            header, header_size, 1024, 4096, &info));
}

TEST_CASE("OTA boot confirmation waits for stable network health", "[ota]")
{
    TEST_ASSERT_FALSE(ota_update_health_ready(29999, true));
    TEST_ASSERT_FALSE(ota_update_health_ready(30000, false));
    TEST_ASSERT_TRUE(ota_update_health_ready(30000, true));
}
