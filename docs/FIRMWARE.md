# Solis Monitor 固件

固件位于 `firmware/`，目标为 ESP32-S3-WROOM-1（8 MB Flash、2 MB PSRAM），使用 ESP-IDF 6.0.2。硬件引脚与显示总线见 [HARDWARE.md](HARDWARE.md)，配网和配对见 [PROVISIONING.md](PROVISIONING.md)，PC 指标协议见 [PROTOCOL.md](PROTOCOL.md)。

## 启动与任务

`firmware/main/app_main.c` 依次初始化板级外设、PSRAM、NT35510 显示、800×480 RGB565 帧缓冲、NVS、网络、串口救援命令和 DHT11。主循环每 10 ms 读取 GPIO21 事件，每秒取得完整 Dashboard 快照并执行局部刷新。

主要后台职责：

- `network_client`：STA/AP 切换、PC 指标轮询、WebUI、设备发现和配对；
- `device_control`：亮度、夜间时段与时区的 NVS 持久化，以及远程重启请求；
- `environment`：GPIO47 DHT11 采样与近似校准；
- `metrics`：PC JSON 协议解析和跨任务快照；
- `renderer` / `ui`：页面静态资源、动态字段和脏矩形；
- `ota_update`：镜像头检查、非运行槽写入、启动切换和回滚确认。

## 背光与电源规则

- GPIO38 使用 ESP-IDF LEDC 以 20 kHz PWM 驱动 PT4110 EN；桌面端可设置 10%–100%，默认 100%。
- 夜间计划默认关闭。启用后，桌面端把当前 Windows UTC 偏移与起止时间同步到副屏，副屏通过 SNTP 获取时间并独立执行；未完成时间同步时不误关背光。
- 夜间熄屏后，GPIO21 第一次有效按键只唤醒背光；唤醒期间按键恢复页面操作，30 秒无后续操作再次熄屏。
- PC 指标连续 5 分钟没有有效响应时关闭背光，恢复响应后自动点亮；AP 配网、发现配对和配对成功提示期间保持点亮。
- 桌面端远程重启通过配对令牌调用局域网控制接口，只重启 ESP32。

## 8 MB Flash 分区

| 名称 | 偏移 | 大小 | 用途 |
|---|---:|---:|---|
| `nvs` | `0x9000` | `0x6000` | Wi‑Fi、PC 地址、配对令牌和设备配置 |
| `otadata` | `0xF000` | `0x2000` | OTA 启动序号与镜像状态 |
| `ota_0` | `0x20000` | `0x3E0000` | 应用槽 A |
| `ota_1` | `0x400000` | `0x3E0000` | 应用槽 B |

两个应用槽均为 3968 KiB。构建时 `check_sizes.py` 必须确认正式镜像可装入最小应用槽；当前没有文件系统分区。

## 本地 OTA

桌面端先通过 `/api/ota/status` 读取设备能力，再检查本地文件：

- ESP 应用魔数与应用描述魔数；
- ESP32-S3 芯片 ID；
- 项目名精确等于 `solis_monitor`；
- 版本非空；
- 文件不超过设备报告的非运行槽容量；
- ESP-IDF 镜像末尾内置 SHA-256 与实际镜像内容一致，并计算完整文件 SHA-256 供用户核对。

上传使用 `POST /api/ota`、`application/octet-stream` 和当前配对令牌。固件不把整份镜像放入内存，而是以 4 KiB 块写入 `esp_ota_get_next_update_partition()` 返回的槽。只有 `esp_ota_end()` 完整校验成功后才调用 `esp_ota_set_boot_partition()`，随后返回成功响应并延迟重启。

`CONFIG_BOOTLOADER_APP_ROLLBACK_ENABLE` 必须开启。新槽首次启动处于 `ESP_OTA_IMG_PENDING_VERIFY`；程序稳定运行至少 30 秒，并且 Wi‑Fi 已连接或 AP 管理页已正常运行后，才调用 `esp_ota_mark_app_valid_cancel_rollback()`。启动崩溃会由 Bootloader 回滚；传输中断不会改变当前启动槽。

## 构建

```powershell
& 'D:\ESP-IDF\v6.0.2\esp-idf\export.ps1'
idf.py -C .\firmware -B build\ota build
```

版本由 `firmware/version.txt` 提供。完整构建输出必须包含：

- Bootloader：`0x0`；
- 分区表：`0x8000`；
- OTA 初始数据：`0xF000`；
- 正式应用：`0x20000`。

从旧单 `factory` 分区迁移时需人工让 GPIO0 保持低电平并执行一次完整串口写入。迁移成功后，日常固件更新使用桌面端局域网 OTA。
