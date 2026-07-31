# PC—ESP32 指标协议

本文描述 Solis Monitor 桌面端与 ESP32 固件之间当前使用的 schema 1 指标协议。稳定架构见 [`../DESIGN.md`](../DESIGN.md)，任务状态见 [`../TODO.md`](../TODO.md)。

## 传输

- ESP32 每秒通过 HTTP `GET /api/v1/metrics` 拉取一次完整快照。
- 请求使用 `Authorization: Bearer <device-token>`。
- 响应为 UTF-8 JSON，最大 4096 字节，并使用 `Cache-Control: no-store`。
- HTTP 连续 5 秒没有有效响应时只将 PC 链路标为离线，保留最后一次完整快照。
- 协议仅用于可信局域网；当前没有 TLS，也不使用 WebSocket。

## 设备管理与 OTA

副屏在局域网 HTTP 80 端口提供设备发现和管理接口。Wi‑Fi WebUI 的查看、扫描、保存与恢复默认设置按可信家庭局域网边界开放；固件写入具有更高破坏性，因此 OTA 接口必须使用配对后生成的 `Authorization: Bearer <device-token>`。

| 接口 | 方法 | 鉴权 | 作用 |
|---|---|---|---|
| `/api/device` | GET | 无 | 设备名、IP、固件版本、信号和配对/发现状态 |
| `/api/control` | GET | 设备令牌 | 读取亮度、夜间计划与副屏本地时区 |
| `/api/control` | POST | 设备令牌 | 保存亮度、夜间计划与副屏本地时区 |
| `/api/restart` | POST | 设备令牌 | 延迟重启副屏，不重启 PC 后台服务 |
| `/api/ota/status` | GET | 设备令牌 | 芯片、项目、运行版本、OTA 槽容量和回滚能力 |
| `/api/ota` | POST | 设备令牌 | 以 `application/octet-stream` 流式上传应用 `.bin` |

控制与 OTA 只在 STA 局域网管理服务开放，AP 配网入口不接受这些操作。控制接口使用 `application/x-www-form-urlencoded`，字段为 `brightness`（10–100）、`night_enabled`（0/1）、`night_start` 与 `night_end`（从零点开始的分钟数）和 `utc_offset`（UTC 分钟偏移）。固件端对整组字段校验成功后才写入 NVS。

OTA 固件端先检查内容长度、ESP 镜像头、ESP32-S3 芯片 ID、`solis_monitor` 项目名和 OTA 槽容量，再以 4 KiB 块流式写入非运行槽。传输中断调用 `esp_ota_abort()`，完整传输由 `esp_ota_end()` 校验镜像后才切换启动分区。

## 根对象

| 字段 | 类型 | 规则 |
|---|---|---|
| `schema` | number | 必须精确等于整数 `1` |
| `sequence` | uint64 | 每次完整发布递增；固件按原始 JSON 整数解析，禁止小数、指数和越界值 |
| `generated_at` | int64 | Unix 秒；禁止小数、指数和越界值 |
| `system` | object | 必须存在 |
| `codex` | object | 必须存在 |
| `environment` | object | 必须存在 |
| `availability` | object | 新版桌面端总是发送；旧 schema 1 可省略 |

解析是事务性的：任一必需结构、已知字段类型或元数据非法时，整份响应失败，当前屏幕状态和元数据都不改变。未知新增字段被忽略，以便 schema 1 向后扩展。

## `system`

基础必需字段为 `time`、CPU 占用/温度/频率/功耗、GPU 占用/核心温度/频率/功耗、内存占用、FPS、兼容 NVMe 温度以及上传/下载速率。字段必须存在，但值可为 `null`；`null` 表示不可用。

当前可选扩展字段如下：

| 字段 | 含义 | 单位 |
|---|---|---|
| `cpu_name`、`gpu_name` | 处理器名称 | 文本 |
| `gpu_memory_usage` | 显存占用率 | `%` |
| `gpu_memory_used_mb`、`gpu_memory_total_mb` | 显存已用/总量 | MB |
| `gpu_memory_temp_c` | 显存温度 | °C |
| `memory_temp_c` | 内存温度 | °C |
| `memory_used_gb`、`memory_total_gb` | 内存已用/总量 | GB |
| `network_name` | 当前出口网卡名称 | 文本 |
| `storage_devices[]` | 最多 4 块物理硬盘 | 数组 |

每个 `storage_devices[]` 元素包含 `name`、`usage`、`temp_c`。硬盘没有实时温度时发送 `null`；`Warning Temperature` 是告警阈值，禁止当作实时温度。

## `codex`

`online` 和 `project` 是 schema 1 基础字段。当前扩展字段包括：

- `task`、`model`、`reasoning_effort`；
- `context_used`、`context_used_k`、`context_window_k`、`total_tokens`；
- `main_weekly_remaining`、`main_quota_name`、`main_quota_reset_at`；
- `spark_weekly_remaining`、`spark_quota_name`、`spark_quota_reset_at`。

`weekly_remaining` 是旧 schema 1 的主周额度别名。若同时存在 `main_weekly_remaining`，新字段覆盖旧别名；若新字段缺失，固件保留旧别名值。

## `environment`

包含 `location`、`weather`、`wind_direction`、`wind_scale`、`weather_icon`、`outdoor_low_c`、`outdoor_high_c`、`indoor_temp_c` 和 `humidity`。`wind_direction`、`wind_scale` 和 `weather_icon` 是 schema 1 的向后兼容可选扩展；旧发送端缺少这些字段时固件按不可用处理。天气和 DHT11 没有真实数据时发送 `null` 或空文本，固件显示 `--`。

`indoor_temp_c` 和 `humidity` 保留在 schema 1 中用于兼容，但当前固件启动本地 DHT11 任务后拥有这两个字段：PC 快照中的同名值不会覆盖 GPIO47 的本地读数。本地读取失败时固件直接写入 `NAN`，UI 独立显示 `--`；这不会改变天气、PC 或 Codex 数据。

## `availability`

旧 schema 1 响应可完全省略 `availability`，此时固件只依据指标值和 `null` 判断可用性。若根对象存在，则 `system`、`codex`、`environment` 三个子对象必须同时存在且类型正确。

已知 availability 标志可以省略；一旦出现，类型必须是 JSON 布尔值：

- `false`：只清除对应字段，数值转为不可用、文本转为空；不影响同一响应中的其他字段。
- `true`：允许使用指标对象中的值，但不能把 `null` 变成有效值。
- 非布尔值：整份响应按非法类型拒绝。
- 未知标志：忽略，供 schema 1 后续扩展。

`system` 标志覆盖 CPU、GPU、显存、内存、FPS、兼容 NVMe 温度和上下行速率。`codex` 标志覆盖项目、上下文、两类额度及其名称和重置时间。`environment.weather` 控制天气文本，`wind_direction`、`wind_scale` 和 `weather_icon` 分别控制风向、风力等级和图标，`outdoor_range` 同时控制最低/最高温，`indoor_temp_c` 和 `humidity` 分别独立控制 DHT11 值。

## 不可用与错误的区别

| 情况 | 结果 |
|---|---|
| 合法数值/字符串且 availability 未否定 | 使用新值 |
| 指标值为 `null` | 对应字段变为不可用，UI 显示 `--` |
| availability 为 `false` | 只清除对应字段 |
| 可选扩展字段缺失 | 对应字段不可用，旧基础字段仍可解析 |
| 已知字段类型错误 | 拒绝整份响应，保留上一完整快照 |
| 根对象或必需分组缺失 | 拒绝整份响应，保留上一完整快照 |
| 未知字段 | 忽略 |

## 验证入口

- 桌面端序列化与快照：`app/tests/SolisMonitor.Metrics.SmokeTests/`
- 固件协议与 availability：`firmware/test_apps/unit/main/test_metrics_protocol.c`
- PC 与固件共享样例：`firmware/test_apps/unit/main/fixtures/schema1/metrics_complete.json`
- 固件状态清理：`firmware/test_apps/unit/main/test_dashboard_state.c`
- 项目验证脚本：`tools/verify.ps1`
