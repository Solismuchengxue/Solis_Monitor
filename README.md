# Solis Monitor

使用 ESP-IDF 6.0.2 开发的 ESP32-S3 + NT35510 800×480 电脑副屏项目。Windows 桌面端基于已中文化的 Libre Hardware Monitor v0.9.6 源码开发，位于 `app/`。

## 小屏效果

![Solis Monitor 小屏设备效果](docs/images/small-screen-hero.png)

| 电脑传感器页 | Codex 与环境页 |
| :---: | :---: |
| ![Solis Monitor 电脑传感器页](docs/images/small-screen-pc.png) | ![Solis Monitor Codex 与环境页](docs/images/small-screen-codex.png) |

## PC 端界面预览

### 设备管理

![Solis Monitor 设备管理界面](docs/images/solis-monitor-device.png)

| 服务链路 | 固件更新 |
| :---: | :---: |
| ![Solis Monitor 服务链路界面](docs/images/solis-monitor-service.png) | ![Solis Monitor 固件更新界面](docs/images/solis-monitor-firmware.png) |

## PC 端源码

- `app/`：Solis Monitor 新桌面端，也是后续 PC 功能的唯一开发基线。
- `docs/DESKTOP_APP.md`：桌面端构建、运行和设备 API 说明。
- `docs/WEATHER.md`：和风天气本地配置、刷新和失败回退说明。
- `docs/PC_MIGRATION.md`：PC 端指标、协议、安全和迁移历史。
当前新桌面端已保留 Libre Hardware Monitor 原有功能并完成中文化，CPU、GPU/显存、内存、物理硬盘、FPS、出口网速以及 Codex 最后活动项目指标已通过独立的 HTTP 设备 API 提供给 ESP32。Codex 指标包含项目、任务、模型、推理强度、上下文、累计 Token 和两类周额度。旧 Agent 的能力已迁入当前桌面端，原独立 Agent 源码已经移除。

## 关键目录

1) 启动与运行入口
- `firmware/main/` 设备启动入口：`app_main.c`
  - 作用：初始化串口、网络、UI、采集链路并启动主循环。
- `firmware/components/` 固件功能模块库（建议按此处改需求）：
  - `network_config/`：Wi-Fi/AP 配置、NVS 配置持久化
  - `network_client/`：HTTP 客户端、服务端通信
  - `metrics/`：指标协议（PC 指标 JSON 的编码/解析）
  - `ui/` + `renderer/`：界面状态与绘制
  - `display/`：液晶底层显示初始化/刷屏
  - `serial_setup/`：串口与通信配置
  - `firmware/components/board/`：板级引脚与按键/外设抽象
  - `ui_assets/`：固件内嵌图片与字体资源
- `app/LibreHardwareMonitor/LibreHardwareMonitor/`：PC 端主程序（桌面端采集服务）
  - `Solis/` 目录：业务层（Codex 指标、硬件映射、网络采集、设备 API、安全）
  - `UI/`：托盘/窗口交互与显示逻辑

2) 数据与配置来源（你查问题/支持联调时最关键）
- `reference/`：原理图、引脚定义、硬件参数、布局图、template 等
- `reference/assets/`：小屏图标、字体、加载动图等显示素材（保留历史上 `assets/` 入口兼容）
- `DESIGN.md`：稳定需求、总体架构和关键设计决策
- `TODO.md`：当前任务、环境阻塞、实施顺序和验收条件的唯一台账
- `docs/`：硬件说明、旧实现提炼、协议、测试、UI、Codex 和仓库审计等专题文档
- `README.md` / `docs/DESKTOP_APP.md` / `docs/PC_MIGRATION.md`：当前默认行为、硬件约定、迁移与运行文档
- `firmware/sdkconfig.defaults` / `firmware/partitions.csv`：固件构建与分区基线

3) 构建、验证与发布（改完后必跑，且别把产物当源码）
- `tools/generate_assets.py`：图片/字体转二进制资源
- `tools/verify.ps1`：本地核验脚本
- `tools/tests/` 与工具链相关的自动检查（辅助回归）
- `app/tests/`：PC 端冒烟测试入口（当前为主功能验证补丁）
- `firmware/test_apps/unit/`：固件单测工作区（重建成本较高，可按需使用）
- `firmware/build/` / `firmware/test_apps/unit/build/` / `app/**/bin,obj` 构建产物目录（建议忽略，不作为代码关注主线）

3 秒速览（先读这块）
- 启动链路：`firmware/main/` + `firmware/components/*`（固件）+ `app/LibreHardwareMonitor/LibreHardwareMonitor/`（PC 采集服务）
- 资料来源：`reference/`、`reference/assets/`、`README*`、`firmware/sdkconfig.defaults`/`firmware/partitions.csv`
- 交付边界：`tools/` 及测试目录常改，`firmware/build`、`app/**/bin`、`app/**/obj` 常清理不提交

## 运行与配网

1. 编译后，以**管理员权限**启动 Solis Monitor：

```powershell
dotnet build .\app\LibreHardwareMonitor\LibreHardwareMonitor\LibreHardwareMonitor.csproj --configuration Release -p:Platform=x64
& .\app\LibreHardwareMonitor\bin\Release\net10.0-windows\SolisMonitor.exe
```

2. 设备令牌由桌面端生成，并在副屏开启发现后通过 6 位配对码自动同步。用户无需查看、复制或手工输入令牌。

3. Windows 防火墙规则为**手动且可选**操作：在 ESP32 需要从局域网访问 API 前才执行。请仅在受信任的专用网络配置文件中，以管理员身份执行：

```powershell
netsh advfirewall firewall add rule name="Solis Monitor Device API" dir=in action=allow protocol=TCP localport=18472 profile=private
```

4. 在 ESP-IDF 6.0.2 环境中构建并打开 COM4 串口：

```powershell
Push-Location .\firmware
idf.py set-target esp32s3
idf.py build
idf.py -p COM4 flash monitor
Pop-Location
```

5. 全新设备没有网络配置时，会自动开放 `Solis-Monitor-xxxx` 热点。手机连接后访问 `http://192.168.0.1/`，扫描并选择 Wi‑Fi，然后填写 Wi‑Fi 密码。已有配置时，长按 GPIO21 约 5 秒可再次进入配网；配网 10 分钟无操作会自动关闭。联网后双击 GPIO21 开启发现，再由桌面端设备向导输入小屏显示的 6 位配对码。

串口 `setup`、`show`、`reconnect`、`clear` 继续作为救援入口。`show` 只显示 SSID、IPv4、端口和令牌末四位，不显示 Wi‑Fi 密码或完整令牌。详细边界见 `docs/PROVISIONING.md`。

设备每秒请求一次指标并更新屏幕；连续 5 秒没有有效响应时，数据源会显示为离线但保留最后一次有效数值。需要更换令牌时，在桌面端清除配对后重新执行 6 位码配对。API 使用 HTTP 而非 HTTPS，因此只应在可信局域网中使用。

Codex 页面会显示最后活动的主任务、当前上下文占用和 7 天剩余额度；子代理不会抢占任务名称。当前问题和后续功能统一维护在 `TODO.md`。PC 与 ESP32 继续使用 HTTP，不引入 WebSocket。

## 验证

在 ESP-IDF 6.0.2 环境中运行可重复执行的主机验证：

```powershell
pwsh -NoProfile -File .\tools\verify.ps1
```

该命令会依次还原、测试并构建 Windows 解决方案，再运行 Python 检查、使用独立配置构建固件并报告大小；正式固件必须能够装入任一 `0x3E0000` OTA 分区。它不会刷写 COM4，也不会修改防火墙。分区迁移完成后，日常正式升级由桌面端“固件更新”页通过局域网执行；串口只保留为首次迁移和救援入口。

## 许可证

本仓库原创内容采用 [Mozilla Public License 2.0](LICENSE) 发布。`app/LibreHardwareMonitor/` 中基于 Libre Hardware Monitor 的源码继续受 MPL-2.0 约束；第三方组件与素材保留各自的许可证和版权声明，详见对应目录中的许可证文件及 `app/LibreHardwareMonitor/THIRD-PARTY-NOTICES.txt`。
