internal static partial class SmokeTests
{
    public static void Run()
    {
    var tests = new (string Name, Action Run)[]
    {
        ("手动网卡优先", ManualSelectionWins),
        ("自动选择系统出口", BestInterfaceSelectionWins),
        ("默认网关回退", GatewayFallbackWorks),
        ("上下行 Mbps 计算", ThroughputIsCalculated),
        ("网卡切换重新建立基线", InterfaceChangeRebaselines),
        ("单周期只发布一次完整快照", SnapshotStorePublishesOneCompleteCycle),
        ("并发采集不产生额外发布", SnapshotStoreConcurrentUpdatesKeepSingleSequence),
        ("CPU 指标映射", CpuMetricsAreMapped),
        ("Intel 混合核心主频映射", IntelHybridCpuClockIsMapped),
        ("独显优先且显存指标完整", DiscreteGpuAndVramMetricsAreMapped),
        ("手动 GPU 选择覆盖默认值", PreferredGpuOverridesDefault),
        ("显存占用率可由容量推导", VramUsageCanBeDerived),
        ("内存条温度取实时最高值", MemoryTemperatureUsesHighestDimm),
        ("内存容量和物理硬盘明细", MemoryAndPhysicalStorageDetailsAreMapped),
        ("NVMe 默认最高且支持手动选择", NvmeSelectionWorks),
        ("存储初始化后停止可移动介质轮询", StorageInitializationStopsRemovableMediaPolling),
        ("硬件指标合并进统一快照", SnapshotStorePublishesHardware),
        ("设备令牌生成和持久化", DeviceTokenPersists),
        ("清除配对会轮换令牌并清除本地记录", DevicePairingCanBeClearedLocally),
        ("副屏身份响应解析", DeviceIdentityResponseIsParsed),
        ("自动发现限制为本地 IPv4 子网", DeviceDiscoveryCandidatesStayInLocalSubnet),
        ("已配对设备探测失败时不扫描整个子网", PairedDeviceProbeFailureDoesNotScanSubnet),
        ("副屏重启后可直接刷新已配对设备", PairedDeviceCanBeRefreshedImmediatelyAfterRestart),
        ("设备配对码必须为六位数字", DevicePairingCodeRequiresSixDigits),
        ("设备发现释放会等待在途扫描并可重复调用", DeviceDiscoveryShutdownWaitsForActiveScan),
        ("副屏显示设置协议与令牌控制", DeviceDisplaySettingsRoundTrip),
        ("本地固件镜像头校验", FirmwareImageHeaderIsValidated),
        ("OTA 设备状态响应解析", FirmwareDeviceStatusIsParsed),
        ("本地 OTA 使用配对令牌并上传完整镜像", FirmwareUpdateUsesPairedToken),
        ("本地 OTA 在传输中断时报告保留旧固件", FirmwareUpdateInterruptionIsSafe),
        ("托盘副屏状态与 WebUI 地址", DeviceTrayPresentationUsesDiscoveredDevice),
        ("副屏连续离线两分钟只通知一次", DeviceOfflineNotificationIsDebounced),
        ("设备向导配网维护窗口暂停离线通知", DeviceProvisioningMaintenanceSuppressesOfflineNotification),
        ("设备令牌失配只通知对应副屏一次", DeviceTokenMismatchIsMatchedAndDebounced),
        ("清除配对后停止副屏通知", ClearingPairingResetsDeviceNotifications),
        ("默认桌面宿主为 WPF 且旧界面必须显式启用", DefaultDesktopHostIsWpfAndLegacyRequiresExplicitArgument),
        ("桌面入口只在显式参数下启动旧 WinForms", DesktopHostLauncherRoutesOnlyExplicitLegacyModeToWinForms),
        ("WPF 单实例请求经 Dispatcher 切回 UI 线程", WpfSingleInstanceRequestsAreMarshaledToDispatcher),
        ("桌面运行时生命周期有界且释放幂等", DesktopRuntimeLifecycleIsBounded),
        ("WPF 后台只启用副屏所需硬件组", WpfBackendUsesOnlyDisplayedHardwareGroups),
        ("WPF 后台公开可复用运行时入口", WpfBackendExposesReusableRuntime),
        ("桌面硬件采集按刷新映射发布顺序执行", DesktopHardwareMetricsPumpPublishesOneCycle),
        ("静默启动只影响 Windows 开机启动", SilentStartupOnlyAffectsWindowsStartup),
        ("夜间背光时间支持跨午夜", NightBacklightTimeSupportsCrossMidnight),
        ("夜间背光使用原生时间选择器", NightBacklightUsesNativeTimeSelectors),
        ("夜间背光时间下拉项保持深色可读", NightBacklightTimeDropdownIsReadable),
        ("夜间背光时间收起状态保持深色可读", NightBacklightCollapsedTimeSelectionIsReadable),
        ("WPF 控制中心连续切页保持单页可见", WpfControlCenterPageSwitchingKeepsOnePageVisible),
        ("原生 WPF 主窗口直接承载控制中心视图", NativeWpfMainWindowHostsControlCenterView),
        ("原生 WPF 主窗口接受同一控制中心视图实例", NativeWpfMainWindowAcceptsInjectedControlCenterView),
        ("原生 WPF 桌面宿主集中拥有后台窗口与托盘", NativeWpfDesktopHostOwnsBackendWindowAndTaskbar),
        ("开发者模式在当前进程内完成宿主切换", DeveloperModeSwitchesHostInCurrentProcess),
        ("原生 WPF 服务操作复制诊断并打开采集目录", NativeWpfServiceActionsCopyDiagnosticsAndOpenCodex),
        ("设备向导使用原生 WPF 窗口", NativeWpfDeviceSetupWizardUsesNativeWindows),
        ("固件确认使用原生 WPF 窗口并完整显示文件信息", NativeWpfFirmwareConfirmationUsesNativeWindow),
        ("原生 WPF 设备向导复用扫描维护与配对运行时", NativeWpfDeviceSetupWizardUsesRuntimeActions),
        ("原生 WPF 天气编辑校验配置并保留已有密钥", NativeWpfWeatherEditorValidatesAndPreservesKey),
        ("天气设置使用原生 WPF 窗口", NativeWpfWeatherSettingsUsesNativeWindow),
        ("原生 WPF 呈现器接通服务页诊断与采集目录操作", NativeWpfPresenterWiresServiceActions),
        ("WPF 窗口关闭与最小化隐藏且显式退出才终止", WpfWindowLifecyclePolicyIsTrayFirst),
        ("桌面启动设置保持开机启动与静默启动联动规则", DesktopStartupSettingsControllerCoordinatesOptions),
        ("WPF 托盘宿主不依赖 WinForms 托盘组件", WpfTaskbarHostUsesNativeWpfNotifyIcon),
        ("WPF 托盘图标使用可解析的资源 URI", WpfTaskbarIconSourceHasAbsoluteResourceUri),
        ("WPF 控制中心不创建隐藏 WinForms 页面", WpfControlCenterDoesNotBuildHiddenLegacyPages),
        ("WPF 控制中心只分配 ElementHost 控件字段", WpfControlCenterAllocatesOnlyElementHostControlField),
        ("开发者入口必须在十秒内连续点击版本号十次", DeveloperModeUnlockRequiresTenClicks),
        ("恢复默认设置只清理 Solis 用户配置", SolisResetPreservesUpstreamSettingsAndFiles),
        ("传感器历史不会阻塞下次启动", PersistentSettingsDoNotRestoreOrSaveSensorHistory),
        ("实时传感器历史默认关闭且保留当前读数", LiveSensorHistoryIsDisabledByDefault),
        ("开发者绘图只在进入开发者模式后创建", DeveloperPlotIsCreatedOnlyWhenDeveloperModeOpens),
        ("SolisMonitor 首次启动迁移旧配置", SolisExecutableConfigMigratesOnce),
        ("安装版配置迁移到当前用户目录且不覆盖", SolisUserConfigMigrationPreservesExistingCopy),
        ("再次启动会通知已有实例", SecondLaunchSignalsExistingInstance),
        ("退出后首次重启可接管单实例", ImmediateRelaunchTakesOverExitingInstance),
        ("通知点击会请求已有实例打开服务状态页", NotificationActivationSignalsDiagnostics),
        ("诊断状态记录当前故障和最近正常时间", DiagnosticsTrackFaultAndLastNormalTime),
        ("后台采集隔离可恢复异常", BackgroundCollectionGuardIsolatesRecoverableFailure),
        ("后台采集故障后恢复执行", BackgroundCollectionGuardContinuesAfterFailure),
        ("后台采集传播严重异常", BackgroundCollectionGuardPropagatesFatalFailures),
        ("后台采集日志失败不阻断诊断", BackgroundCollectionGuardSurvivesLogFailure),
        ("运行时错误日志限制重复故障", RuntimeErrorLogRateLimitsDuplicateFailures),
        ("运行时错误日志不压制不同异常类型", RuntimeErrorLogWritesDifferentFailureTypesWithoutThrottling),
        ("运行时错误日志时间规范化为 UTC", RuntimeErrorLogWritesUtcTimestamp),
        ("运行时错误日志有界轮转并脱敏", RuntimeErrorLogRotatesBoundedRedactedRecords),
        ("运行时错误日志存储不可用可恢复", RuntimeErrorLogSurvivesUnavailableStorage),
        ("运行时错误日志封顶注入文件上限", RuntimeErrorLogCapsInjectedMaximumFileBytes),
        ("运行时错误日志丢弃预建超大当前文件", RuntimeErrorLogDiscardsPrebuiltOversizedCurrentFile),
        ("Solis 运行时启动幂等且释放后停止发布", SolisRuntimeLifecycleIsBounded),
        ("PC 与固件共享 schema 1 样例", SharedSchemaFixtureIsCompatible),
        ("设备 API 鉴权和协议兼容", DeviceApiResponseIsCompatible),
        ("设备 API 记录最近成功通信", DeviceApiRecordsLastSuccessfulCommunication),
        ("Codex 最后活动主任务指标", CodexLastActiveMainThreadIsRead),
        ("Codex 账户累计 Token 响应解析", CodexAccountLifetimeTokensAreParsed),
        ("Codex 5.3-Spark limit_id 解析", CodexSparkRateLimitIdIsParsed),
        ("Codex 额度映射与重置时间兼容", CodexQuotaMappingAndResetFormatsAreStable),
        ("Codex 周使用 Token 按主额度周期持久化", CodexWeeklyUsageFollowsMainQuotaCycle),
        ("Codex 周使用 Token 实时汇总本地任务", CodexWeeklyUsageUsesLocalSessionEventsImmediately),
        ("Codex 周使用 Token 忽略不完整事件", CodexWeeklyUsageIgnoresIncompleteTokenEvents),
        ("Codex 周使用 Token 排除子代理任务", CodexWeeklyUsageIgnoresSubagentSessions),
        ("Codex 周使用 Token 清理已移走任务", CodexWeeklyUsageRemovesMissingSessions),
        ("Codex 周使用 Token 以账户周期差值为准", CodexWeeklyUsagePrefersAccountDelta),
        ("Codex 账户周期差值为零时保留本地周 Token", CodexWeeklyUsageFallsBackToLocalWhenAccountDeltaIsZero),
        ("Codex 无关大行不会推高托管分配", CodexLargeIrrelevantLinesDoNotInflateManagedAllocations),
        ("Codex 脱敏内部格式样例", CodexSanitizedFixtureIsParsed),
        ("Codex 内部格式错误可诊断", CodexMalformedInternalFormatIsDiagnosed),
        ("Codex 非七天窗口不会误分类", CodexNonWeeklyWindowsAreIgnored),
        ("Codex 增量追加与截断恢复", CodexIncrementalAppendAndTruncationWorks),
        ("Codex 切换项目时周额度不回退", CodexWeeklyQuotaDoesNotRegressAcrossProjectSwitch),
        ("Codex 指标写入统一快照", SnapshotStorePublishesCodex),
        ("天气密钥仅从本地配置加载", QWeatherSettingsAreLocal),
        ("和风天气认证与数据解析", QWeatherForecastIsParsed),
        ("和风天气使用经纬度查询", QWeatherCoordinatesAreUsed),
        ("天气采集器释放自有 HTTP 客户端", QWeatherCollectorDisposesOwnedClient),
        ("和风天气代码均有图标映射", QWeatherIconMappingIsComplete),
        ("天气失败保留最近有效值并最终失效", WeatherCacheExpires),
        ("天气明确配置错误立即通知且防抖", WeatherImmediateFailureNotificationIsDebounced),
        ("天气网络故障连续三十分钟后通知", WeatherNetworkFailureNotificationIsDelayed),
        ("天气指标写入统一快照", SnapshotStorePublishesWeather),
        ("旧传感器 Web 服务查询参数兼容", LegacySensorQueryStringIsParsed),
        ("传感器树跨线程变更切回 UI 线程", TreeModelChangesAreMarshaledToUiThread),
        ("网卡热插拔覆盖控制中心隐藏与开发者状态", TreeNetworkHotplugSurvivesUiStateChanges),
        ("本机出口网卡真实采样", WindowsSourceReadsCurrentInterface)
    };

    int failedCount = 0;

    foreach ((string name, Action run) in tests)
    {
        try
        {
            run();
            Console.WriteLine($"通过：{name}");
        }
        catch (Exception exception)
        {
            failedCount++;
            Console.Error.WriteLine($"失败：{name}");
            Console.Error.WriteLine(exception);
        }
    }

    if (failedCount > 0)
    {
        Console.Error.WriteLine($"冒烟测试失败：{failedCount}/{tests.Length}");
        Environment.ExitCode = 1;
    }

    return;
    }
}
