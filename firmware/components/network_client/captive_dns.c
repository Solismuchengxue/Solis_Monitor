/*
 * Wildcard captive-portal DNS server, adapted from ESP-IDF's captive_portal
 * example (SPDX-License-Identifier: Unlicense OR CC0-1.0).
 */
#include "captive_dns.h"

#include <stdbool.h>
#include <stdlib.h>
#include <string.h>

#include "esp_log.h"
#include "freertos/FreeRTOS.h"
#include "freertos/semphr.h"
#include "freertos/task.h"
#include "lwip/sockets.h"

#define DNS_PORT 53
#define DNS_PACKET_MAX 256

typedef struct __attribute__((packed)) {
    uint16_t id, flags, questions, answers, authorities, additional;
} dns_header_t;

typedef struct __attribute__((packed)) {
    uint16_t name, type, class_value;
    uint32_t ttl;
    uint16_t length;
    uint32_t address;
} dns_answer_t;

struct captive_dns_server {
    volatile bool running;
    esp_netif_t *netif;
    SemaphoreHandle_t stopped;
};

static const char *TAG = "captive_dns";

static int make_reply(uint8_t *packet, size_t received, size_t capacity, uint32_t address)
{
    if (received < sizeof(dns_header_t) + 5 || received + sizeof(dns_answer_t) > capacity)
        return -1;
    dns_header_t *header = (dns_header_t *)packet;
    if (ntohs(header->questions) != 1 || (ntohs(header->flags) & 0x7800U) != 0) return -1;

    size_t cursor = sizeof(*header);
    while (cursor < received && packet[cursor] != 0) {
        uint8_t label = packet[cursor];
        if ((label & 0xC0U) != 0 || label > 63 || cursor + 1U + label >= received) return -1;
        cursor += 1U + label;
    }
    if (cursor + 5U > received) return -1;
    cursor++;
    uint16_t type;
    memcpy(&type, packet + cursor, sizeof(type));
    if (ntohs(type) != 1) return -1;

    header->flags = htons(0x8180);
    header->answers = htons(1);
    header->authorities = 0;
    header->additional = 0;
    dns_answer_t answer = {
        .name = htons(0xC00C), .type = htons(1), .class_value = htons(1),
        .ttl = htonl(60), .length = htons(4), .address = address,
    };
    memcpy(packet + received, &answer, sizeof(answer));
    return (int)(received + sizeof(answer));
}

static void dns_task(void *argument)
{
    struct captive_dns_server *server = argument;
    int socket_fd = socket(AF_INET, SOCK_DGRAM, IPPROTO_IP);
    if (socket_fd >= 0) {
        struct timeval timeout = {.tv_sec = 0, .tv_usec = 250000};
        setsockopt(socket_fd, SOL_SOCKET, SO_RCVTIMEO, &timeout, sizeof(timeout));
        struct sockaddr_in address = {
            .sin_family = AF_INET, .sin_port = htons(DNS_PORT),
            .sin_addr.s_addr = htonl(INADDR_ANY),
        };
        if (bind(socket_fd, (struct sockaddr *)&address, sizeof(address)) != 0) {
            ESP_LOGE(TAG, "bind failed: errno=%d", errno);
            close(socket_fd);
            socket_fd = -1;
        }
    }

    while (server->running && socket_fd >= 0) {
        uint8_t packet[DNS_PACKET_MAX];
        struct sockaddr_storage source;
        socklen_t source_length = sizeof(source);
        int received = recvfrom(socket_fd, packet, sizeof(packet) - sizeof(dns_answer_t), 0,
                                (struct sockaddr *)&source, &source_length);
        if (received < 0) continue;
        esp_netif_ip_info_t ip = {0};
        if (esp_netif_get_ip_info(server->netif, &ip) != ESP_OK) continue;
        int reply = make_reply(packet, (size_t)received, sizeof(packet), ip.ip.addr);
        if (reply > 0)
            sendto(socket_fd, packet, (size_t)reply, 0,
                   (struct sockaddr *)&source, source_length);
    }
    if (socket_fd >= 0) close(socket_fd);
    xSemaphoreGive(server->stopped);
    vTaskDelete(NULL);
}

captive_dns_handle_t captive_dns_start(esp_netif_t *netif)
{
    if (!netif) return NULL;
    struct captive_dns_server *server = calloc(1, sizeof(*server));
    if (!server) return NULL;
    server->netif = netif;
    server->stopped = xSemaphoreCreateBinary();
    server->running = true;
    if (!server->stopped ||
        xTaskCreate(dns_task, "captive_dns", 4096, server, tskIDLE_PRIORITY + 2, NULL) != pdPASS) {
        if (server->stopped) vSemaphoreDelete(server->stopped);
        free(server);
        return NULL;
    }
    return server;
}

void captive_dns_stop(captive_dns_handle_t server)
{
    if (!server) return;
    server->running = false;
    xSemaphoreTake(server->stopped, portMAX_DELAY);
    vSemaphoreDelete(server->stopped);
    free(server);
}
