# 构建与测试

本文记录 Solis Monitor 当前可重复执行的桌面端、工具链、固件和实机验证方法。协议规则见 [`PROTOCOL.md`](PROTOCOL.md)，任务状态见 [`../TODO.md`](../TODO.md)。

## 环境

- Windows 11、PowerShell；
- .NET SDK 10.0，桌面端目标为 `net10.0-windows` / x64；
- ESP-IDF 6.0.2，目标芯片 `esp32s3`；
- 项目 `.venv`，依赖见 `requirements-tools.txt`；
- 实机串口 COM4，115200 baud。

联网 `dotnet restore` 在 Codex 沙箱身份下可能因 Schannel `SEC_E_NO_CREDENTIALS` 失败；项目和 NuGet 源本身已在正常 Windows 用户环境验证可用。遇到该错误时应在 VS Code/ESP-IDF 正常用户终端运行，不要修改依赖或 NuGet 源绕过。

## 桌面端

```powershell
dotnet restore .\app\LibreHardwareMonitor\LibreHardwareMonitor\LibreHardwareMonitor.csproj -p:NuGetAudit=false
dotnet build .\app\LibreHardwareMonitor\LibreHardwareMonitor\LibreHardwareMonitor.csproj --configuration Release --no-restore -p:Platform=x64
dotnet run --project .\app\tests\SolisMonitor.Metrics.SmokeTests\SolisMonitor.Metrics.SmokeTests.csproj -p:Platform=x64 --no-restore
```

冒烟测试失败时会按测试名称输出异常和堆栈，并在全部用例结束后返回退出码 1。当前构建问题和兼容范围决策统一跟踪在根目录 `TODO.md`，不得为了消除警告而跳过测试或擅自改变目标框架。

## Python 工具与仓库结构

```powershell
.\.venv\Scripts\python.exe -m unittest discover -s tools\tests -p "test_*.py" -v
git diff --check
```

资源测试会重新生成临时输出并比较哈希，确认 800×480 RGB565 页面、字体和
配网门户 gzip 生成可重复；结构测试保护固件/桌面端入口、ESP32-S3 默认目标和
串口配置。

## 正式固件

先加载 ESP-IDF 6.0.2：

```powershell
& 'D:\ESP-IDF\v6.0.2\esp-idf\export.ps1'
idf.py -C .\firmware -B build\ota build
```

构建必须退出 0，并由 `check_sizes.py` 确认镜像装入 `0x3E0000` OTA 分区。当前正式镜像位于 `build/ota/solis_monitor.bin`；`esptool image-info` 必须显示芯片为 ESP32-S3、项目名为 `solis_monitor`，并确认镜像校验和与 Validation hash 有效。

## 固件单元测试

构建测试镜像：

```powershell
& 'D:\ESP-IDF\v6.0.2\esp-idf\export.ps1'
Push-Location .\firmware\test_apps\unit
idf.py -B '..\..\..\build\firmware-unit-idf6' build
Pop-Location
```

schema 1 的共享样例只有一个源文件：

`firmware/test_apps/unit/main/fixtures/schema1/metrics_complete.json`

ESP-IDF 将它直接嵌入 Unity 测试镜像；桌面端测试项目把同一文件链接到测试输出目录并做协议模型往返验证，不维护第二份副本。

### COM4 运行

本板虽然有 CH340C 和自动下载相关电路，但仅依靠 DTR/RTS 不能稳定进入下载模式，
GPIO0 又同时连接 LCD DB0。2026-07-26 重新对照验证后确认：正常启动时 COM4 可打开，
GPIO0 下拉并断电重连后若使用 `--before no-reset`，ROM 仍可能完全不回应；保持
GPIO0 下拉，再让 esptool 执行一次 `default-reset` 才能稳定连接。因此当前串口写入
必须同时使用人工下拉和复位时序：

1. 退出所有串口监视器；
2. 断开 USB，让 GPIO0 保持低电平后重新插入 USB，并等待 COM4 枚举稳定；
3. 先使用 `esptool --before default-reset --after no-reset chip-id` 确认 ROM 能识别
   ESP32-S3、2 MB PSRAM 和正确 MAC；
4. 在对应构建目录中使用
   `esptool --before default-reset --after no-reset write-flash '@flash_args'`；
5. 必须看到每个写入区域的 `Hash of data verified`，再解除 GPIO0；
6. 断开并重新插入 USB，以 115200 baud 打开 COM4；
7. 若写入 Unity 镜像，在菜单输入 `*` 运行全部测试，确认失败数为 0；
8. 再次进入 ROM 下载模式，将正式固件写回；
9. 正常重启并确认设备版本、Wi-Fi、配对、PC/Codex 页面和 GPIO21 按键。

不要使用 `--after soft-reset`：ESP32-S3 写入和哈希校验成功后，该复位方式仍可能返回非零，从而造成“烧录失败”的误判。

2026-07-26 再次准备 Unity 实机测试时，COM4 曾在 ROM 连接或 flasher stub 上传前后
出现 `Cannot configure port`、`Write timeout` 和 `No serial data received`。
后续按上述“人工下拉 + default-reset”流程，以 115200 baud 完整写入正式 0.1.4：
Bootloader、分区表、OTA 数据和 3,524,096 字节应用镜像均通过写后哈希校验，应用压缩
传输耗时 94.3 秒。遇到错误时不得连续原样重试，也不得在正常运行、LCD 正在驱动 DB0
时临时拉低 GPIO0。

### 回滚保护下的 Unity OTA 运行

当正式固件、配对和本地 OTA 均正常，而 ROM 串口写入不稳定时，可以显式构建一次
OTA 兼容的 Unity 镜像：

```powershell
Push-Location .\firmware\test_apps\unit
idf.py -B '..\..\..\build\firmware-unit-ota' -D SOLIS_UNIT_OTA_COMPAT=ON build
Pop-Location
```

该开关默认关闭；普通单元测试项目仍名为 `solis_monitor_unit`，显式开启后镜像项目
名才改为 OTA 接受的 `solis_monitor`。上传前必须用 `esptool image-info` 验证芯片、
项目名和 Validation hash。测试镜像的 `app_main` 只运行 Unity 菜单，不调用
`ota_update_confirm_running()`，因此：

1. 通过受令牌保护的 `/api/ota` 上传测试镜像；
2. 等网络服务离线后，以 115200 baud 打开 COM4；
3. 输入 `*`，确认全部测试失败数为 0；
4. 正常断电重启，不下拉 GPIO0；
5. bootloader 将未确认测试槽回滚到上一正式槽；
6. 重新读取 `/api/device` 和 `/api/ota/status`，确认正式项目、版本、配对和
   `rollback=true` 已恢复。

2026-07-26 实机结果为 78 Tests、0 Failures、0 Ignored；重启后成功恢复正式
`solis_monitor` 0.1.4。

当前分区表使用 `otadata + ota_0 + ota_1`，每个应用槽为 `0x3E0000`。从旧单 `factory` 分区迁移时仍需要最后一次完整串口烧录，且烧录参数必须包含 Bootloader、分区表、`ota_data_initial.bin` 和 `ota_0` 应用；NVS 仍保持在 `0x9000`，普通完整写入不会擦除 Wi‑Fi 和配对配置。迁移后日常升级走局域网，不再为了页面小修改要求用户进入下载模式。

本地 OTA 验收顺序：

1. 桌面端选择合法的 `solis_monitor.bin`，确认芯片、项目、版本、SHA-256 和槽容量校验通过；
2. 上传完成后副屏自动重启，30 秒稳定运行且 Wi‑Fi 或 AP 管理链路可用后确认新固件；
3. 损坏镜像必须在 PC 或 `esp_ota_end()` 阶段被拒绝；
4. 中断上传后重新启动仍进入旧固件；
5. 新镜像启动失败时，Bootloader 应回滚到上一槽；
6. 调用受令牌保护的 `/api/ota/status`，确认运行版本、最大镜像大小和 `rollback=true`。

需要专项验证第 5 项时，可在独立构建目录启用
`SOLIS_OTA_ROLLBACK_TEST=ON` 并覆盖 `PROJECT_VER`。该测试镜像只在
`ESP_OTA_IMG_PENDING_VERIFY` 状态主动重启，普通构建不包含此行为。上传前必须用
`esptool image-info` 核对测试版本、芯片、校验和与 Validation hash，并保留上一槽的已确认正式版本作为恢复目标。

## 发布门禁

一次可交付验证至少包括：

- 桌面端 Release x64 构建和全部冒烟测试；
- Python 资源/结构测试；
- 正式固件和固件单元测试镜像构建；
- `git diff --check`；
- 涉及显示、按键、网络或协议解析时完成 COM4 实机验证；
- 检查最终 diff，不包含临时脚本、调试输出、凭据、构建目录或意外生成文件；
- 准确记录未运行的检查和仍未验证的风险。
