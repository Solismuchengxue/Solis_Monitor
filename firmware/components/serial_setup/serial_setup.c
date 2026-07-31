#include "serial_setup.h"

#include <errno.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include "driver/uart.h"
#include "driver/uart_vfs.h"
#include "esp_system.h"
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"

#define SERIAL_SETUP_LINE_SIZE 128
#define SERIAL_SETUP_STACK_SIZE 4096
#define SERIAL_SETUP_UART_RX_BUFFER_SIZE 256

typedef struct {
    config_store_t *store;
    serial_setup_reconnect_fn reconnect;
    void *reconnect_context;
} serial_setup_context_t;

typedef enum {
    SERIAL_SETUP_LINE_OK,
    SERIAL_SETUP_LINE_EOF,
    SERIAL_SETUP_LINE_TOO_LONG,
} serial_setup_line_result_t;

static serial_setup_context_t serial_setup_context;
static bool serial_setup_started;

size_t serial_setup_trim_line(char *line)
{
    size_t length;

    if (!line) return 0;
    length = strlen(line);
    if (length != 0 && line[length - 1] == '\n') line[--length] = '\0';
    if (length != 0 && line[length - 1] == '\r') line[--length] = '\0';
    return length;
}

esp_err_t serial_setup_format_summary(const network_config_t *config, bool present,
                                      char *output, size_t output_size)
{
    int written;

    if (!output || output_size == 0) return ESP_ERR_INVALID_ARG;
    if (!present) {
        written = snprintf(output, output_size, "not configured");
    } else {
        if (!config || network_config_validate(config) != ESP_OK) return ESP_ERR_INVALID_ARG;
        written = snprintf(output, output_size, "ssid=%s host=%s port=%u token=****%.4s",
                           config->ssid, config->host, (unsigned)config->port,
                           config->token + sizeof(config->token) - 5);
    }
    if (written < 0 || (size_t)written >= output_size) return ESP_ERR_INVALID_SIZE;
    return ESP_OK;
}

static serial_setup_line_result_t serial_setup_read_line(char line[SERIAL_SETUP_LINE_SIZE])
{
    int character;

    if (!fgets(line, SERIAL_SETUP_LINE_SIZE, stdin)) return SERIAL_SETUP_LINE_EOF;
    if (strchr(line, '\n')) {
        serial_setup_trim_line(line);
        return SERIAL_SETUP_LINE_OK;
    }

    while ((character = getchar()) != '\n' && character != EOF) {
    }
    line[0] = '\0';
    return SERIAL_SETUP_LINE_TOO_LONG;
}

static bool serial_setup_copy_field(char *destination, size_t destination_size, const char *source)
{
    size_t length = strlen(source);

    if (length >= destination_size) return false;
    memcpy(destination, source, length + 1);
    return true;
}

static bool serial_setup_read_field(const char *prompt, char *destination, size_t destination_size)
{
    char line[SERIAL_SETUP_LINE_SIZE];

    fputs(prompt, stdout);
    fflush(stdout);
    if (serial_setup_read_line(line) != SERIAL_SETUP_LINE_OK) {
        fputs("input rejected\n", stdout);
        return false;
    }
    if (strcmp(line, "cancel") == 0) {
        fputs("cancelled\n", stdout);
        return false;
    }
    if (!serial_setup_copy_field(destination, destination_size, line)) {
        fputs("input rejected\n", stdout);
        return false;
    }
    return true;
}

static bool serial_setup_parse_port(const char *line, uint16_t *port)
{
    char *end;
    unsigned long value;

    if (line[0] == '\0') {
        *port = NETCFG_DEFAULT_PORT;
        return true;
    }
    errno = 0;
    value = strtoul(line, &end, 10);
    if (errno == ERANGE || *line == '\0' || *end != '\0' || value == 0 || value > UINT16_MAX) {
        return false;
    }
    *port = (uint16_t)value;
    return true;
}

static void serial_setup_run_setup(const serial_setup_context_t *context)
{
    network_config_t config = {0};
    char port_line[SERIAL_SETUP_LINE_SIZE];
    esp_err_t result;

    if (!serial_setup_read_field("SSID: ", config.ssid, sizeof(config.ssid)) ||
        !serial_setup_read_field("Password: ", config.password, sizeof(config.password)) ||
        !serial_setup_read_field("Windows IPv4: ", config.host, sizeof(config.host))) {
        return;
    }

    fputs("Port [18472]: ", stdout);
    fflush(stdout);
    if (serial_setup_read_line(port_line) != SERIAL_SETUP_LINE_OK) {
        fputs("input rejected\n", stdout);
        return;
    }
    if (strcmp(port_line, "cancel") == 0) {
        fputs("cancelled\n", stdout);
        return;
    }
    if (!serial_setup_parse_port(port_line, &config.port)) {
        fputs("input rejected\n", stdout);
        return;
    }
    if (!serial_setup_read_field("Device token: ", config.token, sizeof(config.token))) {
        return;
    }

    result = network_config_validate(&config);
    if (result != ESP_OK) {
        fputs("invalid configuration\n", stdout);
        return;
    }
    network_config_normalize_token(&config);
    result = config_store_save(context->store, &config);
    if (result != ESP_OK) {
        printf("save failed: %s\n", esp_err_to_name(result));
        return;
    }
    fputs("saved\n", stdout);
    context->reconnect(context->reconnect_context);
}

static void serial_setup_run_show(const serial_setup_context_t *context)
{
    network_config_t config = {0};
    char summary[160];
    bool present = false;
    esp_err_t result = config_store_load(context->store, &config, &present);

    if (result == ESP_OK) result = serial_setup_format_summary(&config, present, summary, sizeof(summary));
    if (result != ESP_OK) {
        printf("show failed: %s\n", esp_err_to_name(result));
        return;
    }
    printf("%s\n", summary);
}

static void serial_setup_run_clear(const serial_setup_context_t *context)
{
    char confirmation[SERIAL_SETUP_LINE_SIZE];
    esp_err_t result;

    fputs("Type CLEAR to erase network settings: ", stdout);
    fflush(stdout);
    if (serial_setup_read_line(confirmation) != SERIAL_SETUP_LINE_OK ||
        strcmp(confirmation, "CLEAR") != 0) {
        fputs("clear cancelled\n", stdout);
        return;
    }
    result = config_store_clear(context->store);
    printf("clear result: %s\n", esp_err_to_name(result));
    fflush(stdout);
    vTaskDelay(pdMS_TO_TICKS(100));
    esp_restart();
}

static void serial_setup_task(void *argument)
{
    const serial_setup_context_t *context = argument;
    char line[SERIAL_SETUP_LINE_SIZE];

    for (;;) {
        fputs("solis> ", stdout);
        fflush(stdout);
        if (serial_setup_read_line(line) != SERIAL_SETUP_LINE_OK) {
            fputs("input rejected\n", stdout);
            if (feof(stdin)) {
                clearerr(stdin);
                vTaskDelay(pdMS_TO_TICKS(100));
            }
            continue;
        }
        if (strcmp(line, "setup") == 0) {
            serial_setup_run_setup(context);
        } else if (strcmp(line, "show") == 0) {
            serial_setup_run_show(context);
        } else if (strcmp(line, "reconnect") == 0) {
            context->reconnect(context->reconnect_context);
            fputs("reconnect requested\n", stdout);
        } else if (strcmp(line, "clear") == 0) {
            serial_setup_run_clear(context);
        } else if (line[0] != '\0') {
            fputs("commands: setup show reconnect clear\n", stdout);
        }
    }
}

esp_err_t serial_setup_start(config_store_t *store,
                             serial_setup_reconnect_fn reconnect,
                             void *reconnect_context)
{
    BaseType_t created;
    esp_err_t result;

    if (!store || !reconnect) return ESP_ERR_INVALID_ARG;
    if (serial_setup_started) return ESP_ERR_INVALID_STATE;

    result = uart_driver_install(CONFIG_ESP_CONSOLE_UART_NUM, SERIAL_SETUP_UART_RX_BUFFER_SIZE, 0, 0, NULL, 0);
    if (result != ESP_OK) return result;
    uart_vfs_dev_use_driver(CONFIG_ESP_CONSOLE_UART_NUM);

    serial_setup_context.store = store;
    serial_setup_context.reconnect = reconnect;
    serial_setup_context.reconnect_context = reconnect_context;
    created = xTaskCreate(serial_setup_task, "serial_setup", SERIAL_SETUP_STACK_SIZE,
                           &serial_setup_context, tskIDLE_PRIORITY + 1, NULL);
    if (created != pdPASS) {
        uart_vfs_dev_use_nonblocking(CONFIG_ESP_CONSOLE_UART_NUM);
        uart_driver_delete(CONFIG_ESP_CONSOLE_UART_NUM);
        return ESP_ERR_NO_MEM;
    }
    serial_setup_started = true;
    return ESP_OK;
}
