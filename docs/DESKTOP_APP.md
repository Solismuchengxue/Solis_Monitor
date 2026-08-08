# Solis Monitor 桌面端

`app/LibreHardwareMonitor/` 中的源码来自官方 Libre Hardware Monitor v0.9.6，按 MPL-2.0 许可证保留原始版权和许可证文件。

当前基线已完成桌面程序中文化，并将窗口和托盘显示名改为 **Solis Monitor**。硬件访问、传感器枚举、日志、桌面小工具和远程 Web 服务等原有功能保持不变。CPU、GPU/显存、内存、物理硬盘、FPS、出口网速和 Codex 使用情况已经映射到进程内统一快照，并通过 `18472/api/v1/metrics` 提供给 ESP32；后续 PC 端功能继续在 `app/LibreHardwareMonitor/` 开发，不再扩展旧的独立 Agent。

默认启动链为 `Program` → `DesktopHostLauncher` → 原生 WPF
`SolisWpfDesktopHost`。`SolisDesktopBackend` 统一拥有硬件采集、设置和
`SolisRuntime`；后者负责指标快照、Codex/天气/网络采集、Device API、设备发现、
诊断、设备控制、OTA 和采集定时器。WPF 主窗口直接承载控制中心，
`SolisTaskbarHost` 负责托盘生命周期。旧 `MainForm`、WinForms 托盘和原始传感器树
只在显式 `--legacy-ui` 回退路径中创建，不属于默认用户路径。

PC 端迁移历史和兼容边界见 [PC_MIGRATION.md](PC_MIGRATION.md)。

## 普通界面

普通界面定位为单设备控制中心，不再重复小屏上的 CPU、GPU、内存、硬盘、网络、Codex 和天气数据看板。当前外壳采用 Windows 11 设置式结构，跟随 Windows 深浅色主题和系统强调色，左侧固定为：

- 设备；
- 服务；
- 启动与托盘；
- 固件更新。

设备发现、配对、配置、通知和 OTA 功能已经形成当前产品基线。固件更新页选择本地文件后会先校验 ESP32-S3 芯片、`solis_monitor` 项目名、版本和 SHA-256，并显示二次确认框；只有用户明确确认后，才继续读取设备槽容量并使用当前配对令牌上传。PC 程序和固件均不提供 GitHub Release 在线检查、下载或自动更新。

“服务”页集中显示设备 API、Codex 和天气 API 状态，并提供天气配置、重启 PC 服务和复制诊断信息。设备 API 不再占用设备页，天气和诊断也不再各自占用普通导航。

“设备”页在发现并配对副屏后开放亮度、夜间背光和远程重启。亮度与夜间计划会先从副屏读取，保存时同步当前 Windows 时区；远程重启必须经过确认。所有控制请求都使用配对流程自动保存的设备令牌，界面不要求用户手工输入令牌。

Libre Hardware Monitor 的原始传感器树、Web 服务、日志和模拟数据入口没有删除。侧栏默认只显示程序版本号；在 10 秒内连续点击版本号 10 次后，版本号上方才会出现“开发者模式”入口。解锁状态跨重启保留，可在开发者页面关闭；开发者模式的“视图”菜单可返回 Solis 控制中心。

## 设备向导

程序不会根据首次运行或配置状态自动弹出全局向导。设备页只提供“设备向导”入口：

- “副屏已连接 Wi-Fi”：提示双击 GPIO21 开启发现，列出局域网内处于发现状态的副屏；选择设备、输入屏幕显示的 6 位码后，PC 自动同步本机 IPv4、固定 Device API 端口和内部令牌。
- “配置或更换 Wi-Fi”：提示长按 GPIO21 进入 AP WebUI；PC 在接下来的 10 分钟暂停副屏离线通知，设备重新上线后提前结束维护状态。

天气设置、静默启动和开机启动不在设备向导重复出现。稳定行为以本文、`REQUIREMENTS.md` 和 `PROTOCOL.md` 为准，后续变更与验收条件维护在根目录 `TODO.md`。

## 编译

需要 Windows 和 .NET 10 SDK：

```powershell
dotnet restore .\app\LibreHardwareMonitor\LibreHardwareMonitor\LibreHardwareMonitor.csproj -p:NuGetAudit=false
dotnet build .\app\LibreHardwareMonitor\LibreHardwareMonitor\LibreHardwareMonitor.csproj --configuration Release --no-restore -p:Platform=x64
```

测试程序位于：

```text
app\LibreHardwareMonitor\bin\Release\net10.0-windows\SolisMonitor.exe
```

为读取部分硬件传感器，运行时仍建议使用管理员权限。

## 发布输入

稳定安装包和开发便携版使用同一份经过检查的发布输入。在仓库根目录执行：

```powershell
pwsh -File .\tools\Publish-PC.ps1
```

脚本固定发布 .NET 10 x64 版本到 `build/pc-release/SolisMonitor/`，并在
`build/pc-release/release-manifest.json` 写入排序后的文件大小与 SHA-256。
发布输入必须包含主程序、硬件库、树控件、通知助手、MPL-2.0 许可证和第三方声明，
且不得包含 `SolisMonitor.config`、`LibreHardwareMonitor.config`、设备令牌或天气配置。

上游传感器配置现保存在
`%LocalAppData%\SolisMonitor\SolisMonitor.config`。首次运行新版时，若用户目录尚无
该文件，程序依次尝试复制 EXE 旁的 `SolisMonitor.config` 和旧
`LibreHardwareMonitor.config`；用户目录已有配置时绝不覆盖，同时迁移可用的
`.backup`。因此便携版迁移到安装版和安装版覆盖升级都不会把配置当作程序文件替换。

## 稳定版安装包

稳定版使用官方 Inno Setup 6 生成管理员权限的单 EXE 安装包。开发机的编译器默认位于
`D:\Inno Setup 6\ISCC.exe`，在仓库根目录执行：

```powershell
pwsh -File .\tools\Build-Installer.ps1
```

脚本会先重新生成并校验 `build/pc-release/SolisMonitor/`，再把安装包输出到
`build/installer/`。安装器创建开始菜单快捷方式，并提供默认不勾选的桌面快捷方式；
安装、覆盖升级和卸载都不会打包、覆盖或删除
`%LocalAppData%\SolisMonitor` 中的传感器配置、设备配对、天气和其他用户设置。
覆盖升级会关闭正在占用安装文件的程序；卸载时会先结束托盘主程序，再清理指向已删除
程序的开机计划任务、注册表启动项和通知注册。

Inno Setup 官方安装目前未附带简体中文语言文件，因此当前构建先使用官方内置英文安装
向导；安装选项和 Solis Monitor 应用界面仍使用中文。引入外部翻译文件前需单独核对
来源和许可证。

安装包当前不带数字签名。首次安装时 Windows 可能显示发布者未知；正式对外发布前需使用
可信代码签名证书签署安装包和主程序。

对外运行产物使用 `SolisMonitor.exe`、`SolisMonitor.dll` 和
`SolisMonitor.NotificationHost.exe`；上游源码目录、命名空间、
`LibreHardwareMonitorLib.dll` 及 MPL-2.0 版权归属保持不变。旧便携版首次启动新版时，
若还没有 `SolisMonitor.config`，程序会只复制一次同目录的
`LibreHardwareMonitor.config`，不会覆盖之后产生的新配置。

Solis Monitor 主程序只支持 `.NET 10 x64`，与正式发布、安装器、通知助手和回归测试
保持一致。主程序使用 `app.net10.manifest` 和
`ApplicationHighDpiMode=SystemAware`，保留管理员权限及 Windows 兼容性声明。

上游 `Aga.Controls` 仍是面向 net472 的 WinForms 控件程序集，包含原始传感器树的
设计时支持；为避免复制或改写上游控件，本项目保留其目标框架和架构声明，并在已经
通过实际运行和回归测试的 .NET 10 主程序中定向抑制 `NU1702`。
`LibreHardwareMonitorLib` 同样保留上游多目标框架和多架构声明，产品发布只消费其中的
`.NET 10 x64` 输出。旧传感器 Web 服务的查询参数解析使用项目内实现，不引用
`System.Web`；URL 解码、`+` 转空格和重复参数合并行为由桌面端冒烟测试覆盖。

服务页底部提供独立的红色“恢复默认设置”区域。确认弹窗会明确列出将清除的副屏配对、
设备令牌、天气、启动、开发者和其他 Solis 用户配置；执行后重新启动并进入设备页。
程序文件、固件、日志以及 LibreHardwareMonitor 上游传感器配置不会被删除。

设备 API 独立于 LHM 原有的 8085 Web 服务，继续使用固件已经支持的 Bearer Token 和 schema 1 JSON。桌面端会复用 `%LocalAppData%\SolisMonitor\settings.json` 中的旧令牌；防火墙只需在专用网络放行 TCP 18472。

## Windows 通知

副屏连续离线、设备令牌失配和天气异常使用 Windows 11 原生通知中心，不再依赖旧式托盘气泡。天气 API Key、认证、权限、额度、Host 或请求被服务端明确拒绝时立即通知；DNS、网络超时和服务端 5xx 连续失败满 30 分钟后通知。每次故障期间只通知一次，恢复后静默复位。

硬件采集主程序仍以管理员权限运行；由于 Windows App SDK 不支持提升权限进程直接发送通知，构建会把独立的 `NotificationHost` 子目录复制到 `net10.0-windows` 输出目录。主程序通过当前用户的 Explorer 令牌启动该非管理员助手，只传递通知标题和正文；发送失败时回退到旧式托盘气泡。

所有故障通知都携带兼容参数 `target=diagnostics`。点击通知后，非管理员助手通过当前会话的命名事件唤醒已经运行的管理员主程序并切换到“服务”页，不会再次弹出 UAC；主程序未运行时仍使用兼容启动参数 `--open-diagnostics`。通知激活不开放新的网络端口，也不传递设备令牌。

服务页直接消费现有采集链路的状态，每秒检查设备 API、Codex 采集和天气 API，不创建第二套检测服务。复制的诊断文本继续包含版本、生成时间和四项底层状态，但不读取或输出 API Key、Wi-Fi 密码及完整设备令牌。

通知助手使用微软官方 `Microsoft.WindowsAppSDK` 2.3.1。当前开发便携版依赖本机已安装的 Windows App Runtime 2.3.1；稳定安装包需要把该运行时纳入安装前置条件。移动便携版目录前可运行 `NotificationHost\SolisMonitor.NotificationHost.exe --unregister-all` 清理旧路径的通知注册，原目录内直接覆盖升级不需要执行。

## 后台采集可靠性

后台指标与天气采集的已批准行为见[后台采集可靠性加固设计](superpowers/specs/2026-08-08-background-collection-reliability-design.md)。指标和天气的普通托管异常仅结束当前采集周期，后续仍按既有 Timer 周期自动重试；成功后的下一次完整采集会自动恢复相应诊断状态。该边界不处理严重进程状态异常，也不改变防重入门、Timer 生命周期或关闭流程。

指标采集失败时不发布半成品快照，保留最后一次完整指标值；连续五秒没有新快照时，沿用“PC 指标服务／指标快照未更新”诊断，下一次成功发布后恢复。天气采集失败时保留最后有效天气值，并将天气诊断标记为 `BackgroundCollectionError`；下一次成功的天气读取会恢复该诊断。后台采集故障不会触发 Windows 通知。

本机诊断日志位于 `%LocalAppData%\SolisMonitor\logs\runtime-errors.log`（实际根目录沿用 `SolisRuntime.SettingsDirectory`）。每行仅记录 UTC 时间、固定模块名、异常类型和 HResult；不记录异常消息、堆栈、Codex 输入内容、API Key、设备令牌、Wi-Fi 信息或完整路径，并对字段长度和换行进行限制。相同模块和异常类型五分钟内最多写入一次。

当前日志文件上限为 512 KiB；即将越界时仅保留一个 `runtime-errors.log.1` 备份，备份同为 512 KiB，总占用约 1 MiB。日志目录创建、打开、写入或轮转失败时静默降级：日志失败不会阻止采集故障进入各自既有诊断规则，指标仍走连续五秒无新快照路径，天气仍进入 `BackgroundCollectionError`；不递归记录日志错误，后续采集仍可恢复。

Codex 任务指标只读访问 `%CODEX_HOME%`（未设置时为当前 Windows 用户目录下的 `.codex`）：从会话索引获取任务名称，按会话文件最后写入时间选择最后活动的主任务。采集器会逐行读取会话 JSONL；首行 `session_meta` 用于取得项目目录，增量扫描只解析 `turn_context` 和 `token_count` 记录，以获得模型、推理强度、上下文和额度。指标每 5 秒刷新；连续 10 分钟没有新的计数事件时显示为“不活跃”并保留最后数值。该状态描述任务活动，而不是 Codex 进程存活。

“账户累计 TOKEN”不使用当前任务的 JSONL 累计值。桌面端启动 Codex 桌面版随附的第一方 `codex.exe app-server --stdio`，初始化后调用 `account/usage/read`，读取账户级 `lifetimeTokens`；成功后每 6 小时刷新，失败后 5 分钟重试。该过程复用 Codex 桌面版已有登录状态，不读取认证文件或令牌，也不向 ESP32 发送认证信息、对话正文或提示词。

## 指标基础验证

无需额外测试框架即可验证硬件传感器映射、GPU/NVMe 选择、网卡选择、网速计算、Codex 主任务筛选与用量解析、统一快照发布和本机出口网卡读取：

```powershell
dotnet run --project .\app\tests\SolisMonitor.Metrics.SmokeTests\SolisMonitor.Metrics.SmokeTests.csproj -p:Platform=x64
```
