# 仓库收敛审计

> 审计日期：2026-07-26
> 审计范围：目录、Git 状态、构建入口、引用关系、生成资源、文档、CodeGraph 依赖图和本地产物
> 执行边界：本文只记录审计结论；删除和重构必须按根目录 `TODO.md` 分批确认、实施和验证。

## 1. 结论

当前仓库已经形成清晰的 `app/`、`firmware/`、`docs/`、`reference/` 和
`tools/` 主结构。主要问题不是有效源码体积，而是：

1. 本地存在约 3.12 GB 可再生成的构建、测试、发布和 `bin/obj` 产物；
2. 旧首次启动向导已经失去运行入口，但源码和测试仍保留；
3. 根目录 `TODO.md` 曾混入大量已完成过程，失去待办台账作用；
4. PC 控制中心、主窗体、固件配网门户和冒烟测试入口承担过多职责；
5. 少量未引用资源、无效的嵌套 GitHub 配置和过宽的历史构建矩阵仍待评估。

审计时 Git 位于 `main`，工作区干净。受版本控制内容约 28.7 MB，本地体积
主要由生成物造成。

## 2. 本地产物

以下目录均已被仓库 `.gitignore` 忽略，能够由现有工具链重新生成：

| 内容 | 审计时大小 | 结论 |
| --- | ---: | --- |
| `build/` | 1526.69 MB | 多轮固件、OTA、PC 发布、安装器和 UI 预览 |
| `build_pairing/` | 145.22 MB | 旧配对阶段构建，没有当前脚本引用 |
| `firmware/build*` | 约 378.46 MB | 正式固件历史构建 |
| `firmware/test_apps/unit/build*` | 约 576 MB | 多轮 ESP-IDF 单元测试构建 |
| `app/**/bin`、`app/**/obj` | 571.04 MB | .NET 编译与发布缓存 |
| `firmware/managed_components/` | 1.35 MB | ESP-IDF 可重新下载组件 |
| `firmware/sdkconfig.old` | 0.09 MB | 旧配置副本 |
| `.superpowers/` | 0.05 MB | 已结束任务的 brief、report 和 review 临时文件 |

清理时应先保留或另行归档两个最终交付物：

- `build/installer/SolisMonitor-0.9.6-win-x64-setup.exe`
- `build/release/solis_monitor-0.1.4.bin`

除这两个交付物外，预计可释放约 3.09 GB。

2026-07-26 已按上述边界完成本地产物清理，实际删除 20593 个生成文件并保留两个
交付物。由于它们仍位于被忽略的 `build/` 中，后续不得直接执行 `git clean -fdX`；
执行全量忽略文件清理前必须先将交付物另行归档。

以下本地目录有明确用途，不应纳入普通清理：

- `.venv/`：资源生成依赖 Pillow 和 fonttools，删除后需重新创建；
- `.codegraph/`：当前代码图索引，审计时为 40.61 MB；
- `.vscode/`：本机 ESP-IDF/串口开发设置；
- `DEVLOG.md`：被 Git 忽略的本地维护记录。

空目录 `.agents/` 和 `docs/plans/` 没有实际内容。`docs/plans/` 已经没有计划文件，
文档目录树也不应继续宣称它存在。

## 3. 明确过时的受控内容

### 3.1 旧首次启动向导

`app/LibreHardwareMonitor/LibreHardwareMonitor/UI/FirstRunOnboardingForm.cs`
共 436 行。CodeGraph 显示其 41 个相关符号全部封闭在文件内部，没有运行入口
实例化该窗体。

`app/LibreHardwareMonitor/LibreHardwareMonitor/Solis/Startup/FirstRunOnboardingPolicy.cs`
只会返回“不显示”，运行时没有调用，只有冒烟测试验证它永久为 `false`。

这与当前产品决策一致：程序不自动弹出首次向导，用户只从设备页打开“设备向导”。
因此应删除窗体、策略和对应的三条无意义测试。

实施状态：已于 2026-07-26 删除，并通过 .NET 10 x64 构建和 63 项冒烟测试。

### 3.2 旧网卡选择属性

`MainForm.SelectableNetworkAdapters` 只有定义，没有调用者。自动选择活跃网卡的内部
采集逻辑仍在使用；只应删除该遗留属性，不删除网卡采集实现。

实施状态：已于 2026-07-26 删除，自动网卡选择相关测试继续通过。

### 3.3 未使用字体

`reference/assets/MaoKenZhuYuanTi-MaokenZhuyuanTi-2.ttf` 为 5.44 MB，全仓库没有引用。
当前资源生成明确使用 `HarmonyOS_Sans_SC_Medium.ttf`。前者是受控删除候选。

实施状态：已于 2026-07-26 删除，资源生成测试继续通过。

### 3.4 嵌套 GitHub 配置

`app/LibreHardwareMonitor/.github/` 中的 Funding、Dependabot 和工作流来自上游仓库。
由于它位于当前仓库的子目录，GitHub 不会把它当作本仓库自动化配置，构建也不读取。
删除它不会删除上游 README、许可证或第三方声明。

实施状态：已于 2026-07-26 删除，上游 README、LICENSE 和
`THIRD-PARTY-NOTICES.txt` 保持不变。

## 4. 暂不删除的历史与源资源

以下内容虽然可能没有普通文本引用，但仍有明确用途：

- `HarmonyOS_Sans_SC_Medium.ttf`：资源生成脚本正在使用；
- `loading.gif`、`wificonfig.png`、`WIFIhotpot.png` 和 PC 图标源：保留的设计源素材；
- `firmware/components/ui_assets/generated_*`：固件直接嵌入，保证离线可复现构建；
- `PawnIO_setup.exe`：LibreHardwareMonitor 硬件访问能力使用；
- LibreHardwareMonitor 原始 Web 资源、传感器树和窗体：开发者模式仍保留这些能力；
- 配置迁移、旧名称清理和配对安全兼容代码：承担真实升级兼容；
- `device_control_deinit()`：当前无调用，但属于生命周期对称接口，删除收益很低；
- `docs/PC_MIGRATION.md` 与 `docs/LEGACY_REFERENCE.md`：分别保存 PC 迁移边界和旧硬件事实。

`docs/WEATHER.md` 负责天气配置与数据链路，`docs/WEATHER_ICONS.md` 负责图标映射，
两者职责不同，不应合并。

## 5. 高复杂度文件

### 5.1 `SolisControlCenterControl.cs`

- 1691 行；
- CodeGraph 影响范围 137 个符号；
- 同时包含设备、服务、启动、固件、开发者页面，以及主题、公共控件、亮度、夜间背光、
  重启、天气、诊断和恢复默认设置逻辑。

建议按页面拆分为 Device、Service、Startup、Firmware、Developer 和公共 UI 原语。
这是后续结构重构的第一优先级。

实施状态：已于 2026-07-26 完成五个页面 partial 文件拆分。核心文件降至
1024 行；构建和 63 项冒烟测试通过。公共布局、主题和导航原语仍保留在核心文件，
后续只有在继续增长时才需要进一步拆分。

### 5.2 `MainForm.cs`

- 1934 行；
- CodeGraph 影响范围 273 个符号；
- 同时管理上游传感器树、托盘、Codex、天气、设备发现、通知、OTA、启动设置和控制中心。

不能直接删除上游功能。后续可抽出 Solis 业务协调对象，让 `MainForm` 继续作为原始
LibreHardwareMonitor UI 宿主。该项风险高于普通页面拆分。

2026-07-26 在前三项结构重构和实机回归完成后重新进行了只读复审。当前文件约
1930 行、143 个符号，CodeGraph 影响范围为 274 个符号；只有
`Computer.cs`、`MainForm.Designer.cs` 和固件页 partial 直接依赖主窗体，但主窗体
内部直接持有 18 个 Solis 服务和状态字段，并向控制中心转发 20 余个委托。因此问题
不是外部调用者很多，而是后台服务的所有权和生命周期集中在 UI 类中。

确认的职责边界如下：

| 应移入 Solis 运行时 | 应继续留在 `MainForm` |
| --- | --- |
| 指标快照、Codex/天气/网络采集 | LibreHardwareMonitor `Computer` 和传感器树 |
| Device API、设备令牌和设备发现 | `BackgroundWorker` 中的硬件枚举与指标映射 |
| 诊断、离线/令牌失配/天气故障状态 | WinForms 控件、主题、托盘通知的实际显示 |
| 设备控制、OTA 客户端和采集定时器 | 设备向导、确认框和开发者模式 |
| 服务启动、停止、事件订阅和反向释放 | 上游 Web 服务、日志、图表和小工具 |

专项复审还发现四个必须在抽取前锁定的生命周期风险：

1. `DeviceDiscoveryService.Scan` 是 `async void` 定时器回调。当前 `Dispose()` 取消扫描
   后立即释放 `HttpClient`、`SemaphoreSlim` 和 `CancellationTokenSource`，没有等待
   在途扫描退出；关闭程序恰逢全子网扫描时可能与 `Release()` 或网络请求竞争。
2. 主窗体只调用两个 `System.Threading.Timer.Dispose()`，没有等待正在执行的 Codex、
   网络或天气回调结束；回调可能在后续服务释放期间继续写入状态。
3. `DeviceMetricsServer.AuthorizationObserved` 在构造时订阅，关闭前没有显式解除。
   处理器虽然检查窗体状态，但事件所有权仍不完整。
4. `QWeatherMetricsCollector` 自建 `HttpClient` 却不实现释放；测试天气和保存新配置都会
   创建新采集器，旧客户端只能等待最终回收。

这些问题目前没有证据表明已经造成用户可见故障，因此不应脱离重构单独扩大修改；
但如果直接把现有代码机械搬进新类，会把竞态和资源泄漏固化到新结构。正确顺序是先为
安全停止和客户端所有权添加回归测试，再引入一个非 UI 的 `SolisRuntime`：

1. 统一构造并持有上述 Solis 服务；
2. 以显式 `Start()` 启动 Device API、发现和采集定时器；
3. 接收主窗体映射好的 `MappedHardwareMetrics`，不接管上游硬件树；
4. 将通知作为状态或事件交给主窗体呈现，不从后台线程直接操作 WinForms；
5. 以可等待的顺序停止定时器和扫描、解除事件，再反向释放网络客户端。

第一轮不应同时重做控制中心构造 API。先让其继续读取运行时公开的窄接口，等运行时
稳定后再判断是否有必要减少委托数量，避免把生命周期重构和 UI 接口重构混成一次改动。

第一批生命周期收敛已于 2026-07-26 完成：

- `DeviceDiscoveryService` 用可等待的 `Task` 取代 `async void` 扫描所有权，释放时先
  取消并等待当前扫描，再释放信号量、取消令牌及自有 `HttpClient`，并支持重复释放；
- `QWeatherMetricsCollector` 明确区分自建和注入的 `HttpClient`，天气测试、配置切换
  和主窗体关闭都会释放对应自建实例；
- 主窗体关闭前解除 `AuthorizationObserved` 订阅，并以关闭状态阻止迟到的 Codex/
  天气回调继续发布；天气配置切换后，旧采集器的迟到结果也不会覆盖新配置；
- 新增两项生命周期回归测试，单并发 .NET 10 x64 Release 构建为 0 警告、0 错误，
  65/65 冒烟测试通过。

主窗体仍直接拥有定时器和大部分后台服务；将启动、停止和反向释放统一到
`SolisRuntime` 仍是下一批任务，不能把第一批保护误记为整体重构完成。

第二批代码已于 2026-07-26 完成。新增的 `SolisRuntime` 为非 UI 协调对象，统一拥有
指标快照、Codex/天气/网络采集、Device API、设备令牌、设备发现、诊断、三个通知
监视器、设备控制、OTA 和两个后台定时器；`MainForm` 只通过一个运行时字段访问这些
能力。硬件树读取和 `MappedHardwareMetrics` 映射、WinForms/托盘通知呈现、设备
向导、确认框及开发者模式仍留在主窗体，符合原定边界。

抽取后 `MainForm.cs` 为 1809 行，不再构造上述后台服务。运行时测试使用临时配置目录、
临时 Codex 目录和随机回环端口，验证 `Start()` 幂等、释放后停止发布、`Dispose()`
幂等及释放后拒绝重启；设备发现和天气客户端的两项第一批测试继续通过。net472 和
.NET 10 x64 Release 构建均为 0 警告、0 错误，66/66 冒烟测试通过。校验后的 79 个
发布文件已覆盖 D 盘安装目录，用户配置哈希不变；真实管理员进程、18472 schema 1
指标序列增长和已配对副屏在线状态均已确认。设备、服务、启动与托盘、固件更新页面
均通过最终人工验收，托盘正常退出。REFACTOR-004 至此完成。

### 5.3 `provisioning_portal.c`

- 1049 行；
- 混合 AP/STA HTTP 生命周期、Wi-Fi 配置、发现配对、设备控制、OTA 和恢复默认设置。

建议以后按门户生命周期、配对接口、设备控制/OTA 接口拆分。当前已有实机验证，
优先级低于删除死代码和 PC UI 拆分。

实施状态：已于 2026-07-26 完成代码拆分。核心门户降至 412 行，配对接口为
268 行，设备控制与 OTA 为 357 行，另以 49 行私有内部头共享门户状态。ESP-IDF
6.0.2 正式固件和 Unity 测试镜像均完成编译与链接。正式镜像已通过局域网 OTA
写入真实副屏；局域网 WebUI、AP 配网、6 位码发现、令牌认证、显示控制、远程重启
和 OTA 状态均完成实机验收。Unity 测试镜像通过默认关闭的 OTA 兼容构建开关写入
待确认槽，实机运行 78 Tests、0 Failures、0 Ignored；断电后成功回滚到正式
0.1.4。因此该项已完成。

### 5.4 PC 冒烟测试入口

`app/tests/SolisMonitor.Metrics.SmokeTests/Program.cs` 已超过 2200 行。测试内容本身
有价值，应按 Codex、天气、设备、OTA、启动和诊断等领域拆成多个测试文件，不应删减
覆盖范围。

实施状态：已于 2026-07-26 完成领域拆分。`Program.cs` 从本轮拆分前的 2466 行
降为 1 行入口，
63 个测试的注册顺序、断言和失败输出保持不变；单并发 .NET 10 x64 Release 构建
为 0 警告、0 错误，63/63 冒烟测试通过。

以下大文件不属于普通手写屎山：

- `MainForm.Designer.cs`：WinForms 设计器和上游 UI；
- `generated_font_metadata.c`：生成文件；
- `CodexMetricsCollector.cs`：虽然较长，但当前仍属于同一采集领域且有专门测试。

## 6. 构建兼容范围

`BUILD-001` 已于 2026-07-26 单独实施：

- Solis Monitor 主项目收敛为 `net10.0-windows`、`x64`、`win-x64`；
- `Aga.Controls` 保留上游 `net472` 和 `x64;x86;ARM64`；
- `LibreHardwareMonitorLib` 保留上游多目标框架和多架构声明；
- 正式发布仍固定为 framework-dependent `.NET 10 x64`。

这样移除了产品从未发布或实机验证的主程序旧矩阵，同时不扩大对上游硬件库和旧控件的
改造范围。主程序和冒烟测试构建均为 0 警告、0 错误，66/66 冒烟测试通过；发布清单
包含 79 个文件并明确记录 `.NET 10 x64`。

## 7. 文档问题

审计前的 `TODO.md` 有 537 行、193 个已完成项、0 个未完成项，已经成为历史流水账。
过程性测试次数、旧部署路径和 UI 演进不应长期占用当前任务台账。

正确边界是：

- `TODO.md`：当前任务、阻塞、优先级、验收条件和持续风险；
- `DESIGN.md` 与专题文档：稳定设计和事实；
- Git 历史：已经提交的演进；
- `DEVLOG.md`：本地踩坑和过程记录。

本次收敛同步修正了 README、DESIGN 和专题文档中关于 `docs/plans/`、历史实施计划
以及旧 TODO 任务编号的描述。

## 8. 建议实施顺序

### 第一批：本地产物

1. 确认并保留最终安装包和正式固件；
2. 删除其他 `build*`、测试构建、`bin/obj` 和 `sdkconfig.old`；
3. 删除 `.superpowers/` 和空目录；
4. 检查 Git 状态不受影响。

### 第二批：明确死代码和文档

1. 删除旧首次向导、策略及对应测试；
2. 删除 `SelectableNetworkAdapters`；
3. 删除未使用字体和嵌套 `.github/`；
4. 完成 TODO、README、DESIGN 和专题文档引用收敛；
5. 运行 Python、.NET 冒烟测试和适用构建。

### 第三批：结构重构

1. 拆分 `SolisControlCenterControl`；
2. 拆分冒烟测试入口；
3. 拆分 `provisioning_portal.c`；
4. 最后评估 `MainForm` 协调层和 .NET 构建矩阵。

每批独立提交和验证，不把垃圾清理、行为修改和高风险重构混成一次变更。

## 9. 审计验证

本次审计实际获得的证据：

- CodeGraph：429 个文件、11533 个节点、32511 条关系，索引为最新；
- Python 资源与结构测试：10/10 通过；
- `git diff --check` 通过；
- Git 工作区在审计开始时干净；
- Pillow 对 `Image.getdata()` 给出的两处弃用警告已于 2026-07-26 按官方建议
  迁移到 `get_flattened_data()`；弃用警告回归测试通过，修改前后 36 个生成
  文件的 SHA-256 完全一致；
- 本次没有执行完整 .NET/ESP-IDF 构建，因为没有修改代码，并且完整构建会产生大量
  新缓存。实施源码清理时必须重新执行对应验证。

收敛实施后的追加证据：

- 单并发 .NET 10 x64 Release 构建通过，0 警告、0 错误；
- PC 冒烟测试 63/63 通过；
- Python 资源、配网门户和仓库结构测试 13/13 通过；
- 18 个受控 Markdown 文件的相对链接检查通过；
- `git diff --check` 通过，仅有 Git 对现有 LF/CRLF 策略的提示；
- 配网门户拆分后，ESP-IDF 6.0.2 正式固件和 Unity 测试镜像均完成编译与链接；
  正式镜像已通过局域网 OTA 完成门户、配对、控制和重启实机验收，Unity 实机测试
  78/78 通过并验证未确认测试槽能够回滚到正式固件；
- 构建验证产生的 `bin/obj` 和临时 ESP-IDF 构建目录已再次删除，已保留的最终
  安装包和 `0.1.4` 正式固件哈希保持不变。
