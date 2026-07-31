#pragma once

#include "esp_netif.h"

typedef struct captive_dns_server *captive_dns_handle_t;

captive_dns_handle_t captive_dns_start(esp_netif_t *netif);
void captive_dns_stop(captive_dns_handle_t server);
