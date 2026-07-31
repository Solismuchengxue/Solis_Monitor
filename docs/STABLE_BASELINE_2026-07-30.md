# Solis Monitor 稳定基线验收记录（2026-07-30）

## 目标与边界

- 目标版本：PC `0.9.6`，固件 `0.1.5`。
- 当前分支：`main`。
- 执行前 HEAD：`26181f90e1070f5fac59240b67f3b2eac004bb66`。
- 本轮基于现有非洁净工作树审计和重建，不重置、覆盖或拆分已有改动。
- 构建和测试单并发顺序执行。
- 不读取 `%LocalAppData%\SolisMonitor` 的配置内容。
- 不安装、不启动或覆盖 `D:\Solis Monitor`，不执行串口烧录或 OTA。
- 不删除、暂存、提交、打标签或推送。

## 执行前边界

### Git

- `git branch --show-current`：`main`
- `git rev-parse HEAD`：`26181f90e1070f5fac59240b67f3b2eac004bb66`
- 状态入口：82 个
  - 已跟踪修改：65 个
  - 已跟踪删除：1 个
  - 未跟踪入口：16 个
- `git diff --stat`：66 个已跟踪文件，5850 行新增、3577 行删除。
- `git diff --check`：退出码 0。
- Git 报告多处 LF 将在未来被 Git 写入时转换为 CRLF；本轮未因此改写文件。

### D 盘现有安装

- 路径：`D:\Solis Monitor\SolisMonitor.exe`
- 长度：254464 字节
- 最后写入时间（UTC）：`2026-07-30 12:18:57`
- SHA-256：`1EC0B8185A047ED97D4F68B25F45FF007834711F1480F2546FE04FA5193791AF`

## 工作树用途分类

分类规则按照当前用途，不代表 Git 提交决策。所有不确定项保持原状。

### 1. 正式源码

- `app/LibreHardwareMonitor/LibreHardwareMonitor/LibreHardwareMonitor.csproj`
- `app/LibreHardwareMonitor/LibreHardwareMonitor/Program.cs`
- `app/LibreHardwareMonitor/LibreHardwareMonitor/Solis/Codex/CodexMetricsCollector.cs`
- `app/LibreHardwareMonitor/LibreHardwareMonitor/Solis/Codex/CodexMetricsReading.cs`
- `app/LibreHardwareMonitor/LibreHardwareMonitor/Solis/DeviceApi/DeviceMetricsEnvelope.cs`
- `app/LibreHardwareMonitor/LibreHardwareMonitor/Solis/DeviceApi/DeviceMetricsServer.cs`
- `app/LibreHardwareMonitor/LibreHardwareMonitor/Solis/DeviceControl/DeviceDiscoveryService.cs`
- `app/LibreHardwareMonitor/LibreHardwareMonitor/Solis/Metrics/MetricsSnapshot.cs`
- `app/LibreHardwareMonitor/LibreHardwareMonitor/Solis/Metrics/MetricsSnapshotStore.cs`
- `app/LibreHardwareMonitor/LibreHardwareMonitor/Solis/SolisRuntime.cs`
- `app/LibreHardwareMonitor/LibreHardwareMonitor/Solis/Startup/SingleInstanceCoordinator.cs`
- `app/LibreHardwareMonitor/LibreHardwareMonitor/Solis/Weather/QWeatherMetricsCollector.cs`
- `app/LibreHardwareMonitor/LibreHardwareMonitor/UI/AboutBox.cs`
- `app/LibreHardwareMonitor/LibreHardwareMonitor/UI/AuthForm.cs`
- `app/LibreHardwareMonitor/LibreHardwareMonitor/UI/DeviceSetupWizardForm.cs`
- `app/LibreHardwareMonitor/LibreHardwareMonitor/UI/InterfacePortForm.cs`
- `app/LibreHardwareMonitor/LibreHardwareMonitor/UI/MainForm.cs`
- `app/LibreHardwareMonitor/LibreHardwareMonitor/UI/ParameterForm.cs`
- `app/LibreHardwareMonitor/LibreHardwareMonitor/UI/RestoreDefaultsConfirmationForm.cs`
- `app/LibreHardwareMonitor/LibreHardwareMonitor/UI/SolisControlCenterControl.Developer.cs`
- `app/LibreHardwareMonitor/LibreHardwareMonitor/UI/SolisControlCenterControl.Device.cs`
- `app/LibreHardwareMonitor/LibreHardwareMonitor/UI/SolisControlCenterControl.Firmware.cs`
- `app/LibreHardwareMonitor/LibreHardwareMonitor/UI/SolisControlCenterControl.Service.cs`
- `app/LibreHardwareMonitor/LibreHardwareMonitor/UI/SolisControlCenterControl.Startup.cs`
- `app/LibreHardwareMonitor/LibreHardwareMonitor/UI/SolisControlCenterControl.cs`
- `app/LibreHardwareMonitor/LibreHardwareMonitor/UI/StartupManager.cs`
- `app/LibreHardwareMonitor/LibreHardwareMonitor/UI/WeatherSettingsForm.cs`
- `app/LibreHardwareMonitor/LibreHardwareMonitor/Utilities/PersistentSettings.cs`
- `app/LibreHardwareMonitor/LibreHardwareMonitorLib/Hardware/Sensor.cs`
- `app/LibreHardwareMonitor/LibreHardwareMonitorLib/Hardware/Storage/SmartAttribute.cs`
- `app/LibreHardwareMonitor/LibreHardwareMonitorLib/Hardware/Storage/StorageDevice.cs`
- `app/LibreHardwareMonitor/LibreHardwareMonitorLib/Hardware/Storage/StorageDeviceSensor.cs`
- `app/LibreHardwareMonitor/LibreHardwareMonitorLib/Hardware/Storage/StorageGroup.cs`
- `app/LibreHardwareMonitor/LibreHardwareMonitorLib/LibreHardwareMonitorLib.csproj`
- `firmware/components/device_control/device_control.c`
- `firmware/components/device_control/include/device_control.h`
- `firmware/components/metrics/metrics_protocol.c`
- `firmware/components/ui/dashboard_state.c`
- `firmware/components/ui/include/dashboard_state.h`
- `firmware/components/ui/include/ui.h`
- `firmware/components/ui/ui.c`
- `firmware/main/app_main.c`
- `firmware/version.txt`
- `tools/Publish-PC.ps1`
- `tools/verify.ps1`
- `tools/generate_assets.py`
- `tools/glyphs.txt`
- `app/LibreHardwareMonitor/LibreHardwareMonitor/Solis/Codex/CodexLocalWeeklyUsageReader.cs`
- `app/LibreHardwareMonitor/LibreHardwareMonitor/Solis/Codex/CodexWeeklyUsageTracker.cs`
- `app/LibreHardwareMonitor/LibreHardwareMonitor/Solis/Codex/JsonlMarkerReader.cs`
- `app/LibreHardwareMonitor/LibreHardwareMonitor/Solis/Desktop/`
- `app/LibreHardwareMonitor/LibreHardwareMonitor/Solis/Startup/DesktopHostLauncher.cs`
- `app/LibreHardwareMonitor/LibreHardwareMonitor/Solis/Startup/DesktopHostSelector.cs`
- `app/LibreHardwareMonitor/LibreHardwareMonitor/UI/NightBacklightSettingsForm.cs`
- `app/LibreHardwareMonitor/LibreHardwareMonitor/UI/SolisControlCenterControl.Visuals.cs`
- `app/LibreHardwareMonitor/LibreHardwareMonitor/UI/SolisControlCenterControl.WpfShell.cs`
- `app/LibreHardwareMonitor/LibreHardwareMonitor/UI/SolisDialog.cs`
- `app/LibreHardwareMonitor/LibreHardwareMonitor/UI/Wpf/`
- `tools/Measure-SolisMonitorMemory.ps1`

### 2. 回归测试

- `app/tests/SolisMonitor.Metrics.SmokeTests/SmokeTests.Codex.cs`
- `app/tests/SolisMonitor.Metrics.SmokeTests/SmokeTests.Device.cs`
- `app/tests/SolisMonitor.Metrics.SmokeTests/SmokeTests.Protocol.cs`
- `app/tests/SolisMonitor.Metrics.SmokeTests/SmokeTests.Runner.cs`
- `app/tests/SolisMonitor.Metrics.SmokeTests/SmokeTests.Shared.cs`
- `app/tests/SolisMonitor.Metrics.SmokeTests/SmokeTests.Startup.cs`
- `app/tests/SolisMonitor.Metrics.SmokeTests/SmokeTests.Ui.cs`
- `app/tests/SolisMonitor.Metrics.SmokeTests/SmokeTests.Weather.cs`
- `app/tests/SolisMonitor.Metrics.SmokeTests/SolisMonitor.Metrics.SmokeTests.csproj`
- `app/tests/SolisMonitor.Metrics.SmokeTests/SmokeTests.Storage.cs`
- `firmware/test_apps/unit/main/fixtures/schema1/metrics_complete.json`
- `firmware/test_apps/unit/main/test_metrics_protocol.c`
- `firmware/test_apps/unit/main/test_ui.c`
- `tools/tests/test_generate_assets.py`
- `tools/tests/test_project_config.py`

### 3. 正式文档

- `TODO.md`
- `docs/PC_UI_DESIGN.md`
- `docs/plans/`（验收时存在，2026-07-31 文档收口后移除）
- 本验收记录 `docs/STABLE_BASELINE_2026-07-30.md`

### 4. 受控生成资源与验收证据

- `firmware/components/ui_assets/generated_font_20.bin`
- `firmware/components/ui_assets/generated_font_24.bin`
- `firmware/components/ui_assets/generated_font_56.bin`
- `firmware/components/ui_assets/generated_font_metadata.c`
- `firmware/components/ui_assets/generated_font_metadata.json`
- `docs/qa/design-qa.md`（验收时存在，2026-07-31 文档收口后移除）
- `docs/qa/*.png`（验收时存在，2026-07-31 文档收口后移除）
- `docs/qa/*.jpg`（验收时存在，2026-07-31 文档收口后移除）

这些字体文件由仓库工具生成并参与固件构建；QA 图片在本次验收时作为设计和实机
对照证据，后续删除不改写本记录中的历史验收结论。

### 5. 本地生成物

- `build/` 及语言工具链的 `bin/`、`obj/` 已由 `.gitignore` 排除，未出现在工作树状态中。
- 本轮开始时 `build/` 仅保留 `pc-release/`、`installer/` 和 `ota/` 三组有效交付物。

### 6. 不确定内容

- `app/LibreHardwareMonitor/LibreHardwareMonitorLib/Hardware/Storage/SmartAttributeTranslator.cs`
  当前为已跟踪删除。其删除可能属于硬盘温度实现的有意收敛，但在构建与回归测试完成前，
  仅记录为不确定项，不恢复也不确认删除。

## 原始 Git 状态快照

```text
 M TODO.md
 M app/LibreHardwareMonitor/LibreHardwareMonitor/LibreHardwareMonitor.csproj
 M app/LibreHardwareMonitor/LibreHardwareMonitor/Program.cs
 M app/LibreHardwareMonitor/LibreHardwareMonitor/Solis/Codex/CodexMetricsCollector.cs
 M app/LibreHardwareMonitor/LibreHardwareMonitor/Solis/Codex/CodexMetricsReading.cs
 M app/LibreHardwareMonitor/LibreHardwareMonitor/Solis/DeviceApi/DeviceMetricsEnvelope.cs
 M app/LibreHardwareMonitor/LibreHardwareMonitor/Solis/DeviceApi/DeviceMetricsServer.cs
 M app/LibreHardwareMonitor/LibreHardwareMonitor/Solis/DeviceControl/DeviceDiscoveryService.cs
 M app/LibreHardwareMonitor/LibreHardwareMonitor/Solis/Metrics/MetricsSnapshot.cs
 M app/LibreHardwareMonitor/LibreHardwareMonitor/Solis/Metrics/MetricsSnapshotStore.cs
 M app/LibreHardwareMonitor/LibreHardwareMonitor/Solis/SolisRuntime.cs
 M app/LibreHardwareMonitor/LibreHardwareMonitor/Solis/Startup/SingleInstanceCoordinator.cs
 M app/LibreHardwareMonitor/LibreHardwareMonitor/Solis/Weather/QWeatherMetricsCollector.cs
 M app/LibreHardwareMonitor/LibreHardwareMonitor/UI/AboutBox.cs
 M app/LibreHardwareMonitor/LibreHardwareMonitor/UI/AuthForm.cs
 M app/LibreHardwareMonitor/LibreHardwareMonitor/UI/DeviceSetupWizardForm.cs
 M app/LibreHardwareMonitor/LibreHardwareMonitor/UI/InterfacePortForm.cs
 M app/LibreHardwareMonitor/LibreHardwareMonitor/UI/MainForm.cs
 M app/LibreHardwareMonitor/LibreHardwareMonitor/UI/ParameterForm.cs
 M app/LibreHardwareMonitor/LibreHardwareMonitor/UI/RestoreDefaultsConfirmationForm.cs
 M app/LibreHardwareMonitor/LibreHardwareMonitor/UI/SolisControlCenterControl.Developer.cs
 M app/LibreHardwareMonitor/LibreHardwareMonitor/UI/SolisControlCenterControl.Device.cs
 M app/LibreHardwareMonitor/LibreHardwareMonitor/UI/SolisControlCenterControl.Firmware.cs
 M app/LibreHardwareMonitor/LibreHardwareMonitor/UI/SolisControlCenterControl.Service.cs
 M app/LibreHardwareMonitor/LibreHardwareMonitor/UI/SolisControlCenterControl.Startup.cs
 M app/LibreHardwareMonitor/LibreHardwareMonitor/UI/SolisControlCenterControl.cs
 M app/LibreHardwareMonitor/LibreHardwareMonitor/UI/StartupManager.cs
 M app/LibreHardwareMonitor/LibreHardwareMonitor/UI/WeatherSettingsForm.cs
 M app/LibreHardwareMonitor/LibreHardwareMonitor/Utilities/PersistentSettings.cs
 M app/LibreHardwareMonitor/LibreHardwareMonitorLib/Hardware/Sensor.cs
 M app/LibreHardwareMonitor/LibreHardwareMonitorLib/Hardware/Storage/SmartAttribute.cs
 D app/LibreHardwareMonitor/LibreHardwareMonitorLib/Hardware/Storage/SmartAttributeTranslator.cs
 M app/LibreHardwareMonitor/LibreHardwareMonitorLib/Hardware/Storage/StorageDevice.cs
 M app/LibreHardwareMonitor/LibreHardwareMonitorLib/Hardware/Storage/StorageDeviceSensor.cs
 M app/LibreHardwareMonitor/LibreHardwareMonitorLib/Hardware/Storage/StorageGroup.cs
 M app/LibreHardwareMonitor/LibreHardwareMonitorLib/LibreHardwareMonitorLib.csproj
 M app/tests/SolisMonitor.Metrics.SmokeTests/SmokeTests.Codex.cs
 M app/tests/SolisMonitor.Metrics.SmokeTests/SmokeTests.Device.cs
 M app/tests/SolisMonitor.Metrics.SmokeTests/SmokeTests.Protocol.cs
 M app/tests/SolisMonitor.Metrics.SmokeTests/SmokeTests.Runner.cs
 M app/tests/SolisMonitor.Metrics.SmokeTests/SmokeTests.Shared.cs
 M app/tests/SolisMonitor.Metrics.SmokeTests/SmokeTests.Startup.cs
 M app/tests/SolisMonitor.Metrics.SmokeTests/SmokeTests.Ui.cs
 M app/tests/SolisMonitor.Metrics.SmokeTests/SmokeTests.Weather.cs
 M app/tests/SolisMonitor.Metrics.SmokeTests/SolisMonitor.Metrics.SmokeTests.csproj
 M firmware/components/device_control/device_control.c
 M firmware/components/device_control/include/device_control.h
 M firmware/components/metrics/metrics_protocol.c
 M firmware/components/ui/dashboard_state.c
 M firmware/components/ui/include/dashboard_state.h
 M firmware/components/ui/include/ui.h
 M firmware/components/ui/ui.c
 M firmware/components/ui_assets/generated_font_20.bin
 M firmware/components/ui_assets/generated_font_24.bin
 M firmware/components/ui_assets/generated_font_56.bin
 M firmware/components/ui_assets/generated_font_metadata.c
 M firmware/components/ui_assets/generated_font_metadata.json
 M firmware/main/app_main.c
 M firmware/test_apps/unit/main/fixtures/schema1/metrics_complete.json
 M firmware/test_apps/unit/main/test_metrics_protocol.c
 M firmware/test_apps/unit/main/test_ui.c
 M firmware/version.txt
 M tools/Publish-PC.ps1
 M tools/generate_assets.py
 M tools/glyphs.txt
 M tools/tests/test_generate_assets.py
?? app/LibreHardwareMonitor/LibreHardwareMonitor/Solis/Codex/CodexLocalWeeklyUsageReader.cs
?? app/LibreHardwareMonitor/LibreHardwareMonitor/Solis/Codex/CodexWeeklyUsageTracker.cs
?? app/LibreHardwareMonitor/LibreHardwareMonitor/Solis/Codex/JsonlMarkerReader.cs
?? app/LibreHardwareMonitor/LibreHardwareMonitor/Solis/Desktop/
?? app/LibreHardwareMonitor/LibreHardwareMonitor/Solis/Startup/DesktopHostLauncher.cs
?? app/LibreHardwareMonitor/LibreHardwareMonitor/Solis/Startup/DesktopHostSelector.cs
?? app/LibreHardwareMonitor/LibreHardwareMonitor/UI/NightBacklightSettingsForm.cs
?? app/LibreHardwareMonitor/LibreHardwareMonitor/UI/SolisControlCenterControl.Visuals.cs
?? app/LibreHardwareMonitor/LibreHardwareMonitor/UI/SolisControlCenterControl.WpfShell.cs
?? app/LibreHardwareMonitor/LibreHardwareMonitor/UI/SolisDialog.cs
?? app/LibreHardwareMonitor/LibreHardwareMonitor/UI/Wpf/
?? app/tests/SolisMonitor.Metrics.SmokeTests/SmokeTests.Storage.cs
?? docs/PC_UI_DESIGN.md
?? docs/plans/
?? docs/qa/
?? tools/Measure-SolisMonitorMemory.ps1
```

## 核验结果

- 当前版本声明已核对：PC 由 `app/LibreHardwareMonitor/Directory.Build.props`
  声明为 `0.9.6`，固件由 `firmware/version.txt` 声明为 `0.1.5`。
- `TODO.md` 的当前可用固件版本已由历史值 `0.1.4` 更新为 `0.1.5`；带日期的
  `0.1.4` 实机烧录、回滚和旧交付物记录作为历史证据保留。
- `tools/verify.ps1` 已收敛为单并发入口：桌面端 restore 禁止并行，主项目与
  冒烟测试项目按单并发构建，Python 自动发现全部 `tools/tests/test_*.py`，固件先
  `reconfigure` 再使用 `ninja -j1`。
- `tools.tests.test_project_config` 新增的验证入口回归测试在脚本修改前按预期失败，
  修改后 8/8 通过；`git diff --check` 退出码 0。
- 桌面端正式项目和冒烟测试项目均使用 `--disable-parallel` 完成依赖恢复。
- 桌面端 Release x64 单并发构建成功：0 个警告、0 个错误。
- 桌面冒烟测试项目 Release x64 单并发构建成功：0 个警告、0 个错误。
- 完整桌面冒烟测试进程退出码 0；硬件采集、设备控制、OTA、WPF 生命周期、
  单实例、Codex、天气和协议等全部已登记用例通过，未启动正式桌面程序。
- Python 工具测试通过 17/17，退出码 0。
- 正式固件先完成 `reconfigure`，再由 `ninja -j1 all` 单并发构建，最后
  `idf.py size` 退出码 0。项目名 `solis_monitor`，目标 `esp32s3`，版本
  `0.1.5`；镜像 3548624 字节，应用分区余量 514608 字节（13%）。
- `esptool image-info` 退出码 0，识别为 ESP32-S3，镜像校验哈希有效。
- 固件 Unity 测试镜像已完成 `reconfigure` 和 `ninja -j1 all`，生成
  `solis_monitor_unit.bin`；本轮没有写入实机，因此只证明测试工程可构建，
  不把 Unity 用例记为已运行。
- 固件配置阶段出现 3 条 ESP-IDF 6.0.2 上游 Kconfig 布尔默认值提示：
  `BT_NIMBLE_MESH_PROVISIONER`、`FATFS_PRINT_LLI` 和
  `FATFS_PRINT_FLOAT` 的默认值 `0` 被按 `n` 处理；不影响本轮构建结果。
- PC 发布脚本退出码 0；独立复算确认 86 个载荷文件的相对路径、大小和
  SHA-256 与发布清单全部一致，且没有 PDB 或用户配置文件。
- Inno Setup 6.7.3 编译退出码 0；只生成安装包，没有运行安装程序。

## 交付物

- 机器可读总清单：
  `build/STABLE_BASELINE_0.9.6_0.1.5.json`
- PC 发布清单：
  `build/pc-release/release-manifest.json`
  - 产品版本：
    `0.9.6+26181f90e1070f5fac59240b67f3b2eac004bb66`
  - 载荷文件数：86
  - 载荷总大小：99800671 字节
  - 清单大小：16214 字节
  - 清单 SHA-256：
    `8009CE0FEC0598B1DBB7B854BE0678FBA1D04BFFCED69A9EBF1A6322B035C31E`
- PC 可执行文件：
  `build/pc-release/SolisMonitor/SolisMonitor.exe`
  - 大小：254464 字节
  - SHA-256：
    `1EC0B8185A047ED97D4F68B25F45FF007834711F1480F2546FE04FA5193791AF`
- 安装包：
  `build/installer/SolisMonitor-0.9.6-win-x64-setup.exe`
  - 大小：33593817 字节
  - SHA-256：
    `395B2C7C7F9B349642F1DC4A17140FB40410BFCDBD2BB4B3DDB2366A7FC7F4C7`
- 正式固件：
  `build/ota/solis_monitor.bin`
  - 大小：3548624 字节
  - SHA-256：
    `9F953F0F79193D3E31A6FDB3DD710EA8F72F9A463E91D3571F8AA8E8DCA219DC`
- 固件 Unity 测试镜像：
  `build/firmware-unit-idf6/solis_monitor_unit.bin`
  - 大小：2777056 字节
  - SHA-256：
    `FAD12B386573B583172B61F8950ABF99BEDB4A95D5B4F8566C0F81749B6AF9AE`

## 未执行项目

- 未将固件 Unity 测试镜像写入实机，因此未运行实机 Unity 用例。
- 未执行串口烧录或 OTA。
- 未运行安装包，未覆盖 D 盘现有安装。
- 未读取或修改当前 Windows 用户的 Solis Monitor 配置。
- 未暂存、提交、打标签或推送 Git 改动。

## 最终边界复核

- 最终 `git diff --check` 退出码 0。
- 最终工作树仍为非洁净状态，共 84 个状态入口：
  - 已跟踪修改：67 个。
  - 已跟踪删除：1 个。
  - 未跟踪入口：16 个。
- 最终 `git diff --stat`：68 个已跟踪文件，5889 行新增、3584 行删除。
- 相比执行前，新增的已跟踪修改仅为本轮验证入口：
  `tools/verify.ps1` 和 `tools/tests/test_project_config.py`；其余既有改动均保持原状。
- `D:\Solis Monitor\SolisMonitor.exe` 的长度、UTC 写入时间和 SHA-256
  与执行前完全一致，确认没有被本轮覆盖。
- 所有 `build/` 交付物均继续受 `.gitignore` 管理，没有进入 Git 状态。
- 仓库内 Markdown 相对链接检查覆盖 58 个文件；正式文档链接通过。
  仅两个 ESP-IDF 托管组件的上游 `cJSON` README 引用了未随组件镜像附带的
  `CONTRIBUTORS.md`，分别位于正式固件与固件测试工程的
  `managed_components` 目录。本轮不修改第三方托管组件。
- 当前是 `main` 分支上的普通工作目录，不涉及 worktree 合并或分支收敛。
