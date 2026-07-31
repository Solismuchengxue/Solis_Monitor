internal static partial class SmokeTests
{
static void DefaultDesktopHostIsWpfAndLegacyRequiresExplicitArgument()
{
    Type? selectorType = typeof(StartupLaunchPolicy).Assembly.GetType(
        "LibreHardwareMonitor.Solis.Startup.DesktopHostSelector");
    True(selectorType is not null, "缺少桌面宿主选择器");

    System.Reflection.MethodInfo? selectMethod = selectorType!.GetMethod(
        "Select",
        System.Reflection.BindingFlags.Public |
        System.Reflection.BindingFlags.Static);
    True(selectMethod is not null, "桌面宿主选择器缺少公开 Select 方法");

    object? defaultMode = selectMethod!.Invoke(
        null,
        [Array.Empty<string>()]);
    Equal("Wpf", defaultMode?.ToString(),
        "默认启动必须使用原生 WPF 宿主");

    object? unrelatedArgumentMode = selectMethod.Invoke(
        null,
        [new[] { StartupLaunchPolicy.OpenDiagnosticsArgument }]);
    Equal("Wpf", unrelatedArgumentMode?.ToString(),
        "普通启动参数不得回退到旧 WinForms 界面");

    object? legacyMode = selectMethod.Invoke(
        null,
        [new[] { "--legacy-ui" }]);
    Equal("LegacyWinForms", legacyMode?.ToString(),
        "只有显式 --legacy-ui 才能进入旧 WinForms 界面");
}

static void DesktopHostLauncherRoutesOnlyExplicitLegacyModeToWinForms()
{
    Type? launcherType = typeof(StartupLaunchPolicy).Assembly.GetType(
        "LibreHardwareMonitor.Solis.Startup.DesktopHostLauncher");
    True(launcherType is not null, "缺少桌面宿主启动器");

    System.Reflection.MethodInfo? runMethod = launcherType!.GetMethod(
        "Run",
        System.Reflection.BindingFlags.Public |
        System.Reflection.BindingFlags.Static);
    True(runMethod is not null, "桌面宿主启动器缺少公开 Run 方法");

    int wpfRuns = 0;
    int legacyRuns = 0;
    Func<int> runWpf = () =>
    {
        wpfRuns++;
        return 17;
    };
    Func<int> runLegacy = () =>
    {
        legacyRuns++;
        return 29;
    };

    object? defaultResult = runMethod!.Invoke(
        null,
        [Array.Empty<string>(), runWpf, runLegacy]);
    Equal(17, (int)defaultResult!,
        "默认启动必须执行 WPF 宿主");
    Equal(1, wpfRuns,
        "默认启动应且只应执行一次 WPF 宿主");
    Equal(0, legacyRuns,
        "默认启动不得创建旧 WinForms 宿主");

    object? legacyResult = runMethod.Invoke(
        null,
        [new[] { "--legacy-ui" }, runWpf, runLegacy]);
    Equal(29, (int)legacyResult!,
        "显式旧界面参数必须执行 WinForms 宿主");
    Equal(1, wpfRuns,
        "显式旧界面参数不得再次执行 WPF 宿主");
    Equal(1, legacyRuns,
        "显式旧界面参数应且只应执行一次 WinForms 宿主");
}

static void WpfSingleInstanceRequestsAreMarshaledToDispatcher()
{
    Type? dispatcherType = typeof(StartupLaunchPolicy).Assembly.GetType(
        "LibreHardwareMonitor.UI.WpfViews.WpfSingleInstanceRequestDispatcher");
    True(dispatcherType is not null,
        "缺少 WPF 单实例请求调度器");

    var pending = new Queue<Action>();
    int showRequests = 0;
    int diagnosticsRequests = 0;
    object dispatcher = Activator.CreateInstance(
        dispatcherType!,
        (Action<Action>)(action => pending.Enqueue(action)),
        (Action)(() => showRequests++),
        (Action)(() => diagnosticsRequests++))!;

    dispatcherType!.GetMethod("RequestShowWindow")!.Invoke(dispatcher, null);
    dispatcherType.GetMethod("RequestDiagnostics")!.Invoke(dispatcher, null);

    Equal(2, pending.Count,
        "单实例请求必须先投递到 WPF Dispatcher");
    Equal(0, showRequests,
        "线程池回调不得直接操作 WPF 窗口");
    Equal(0, diagnosticsRequests,
        "线程池回调不得直接切换 WPF 页面");

    pending.Dequeue().Invoke();
    pending.Dequeue().Invoke();
    Equal(1, showRequests,
        "Dispatcher 应执行显示窗口请求");
    Equal(1, diagnosticsRequests,
        "Dispatcher 应执行打开服务页请求");
}

static void DesktopRuntimeLifecycleIsBounded()
{
    System.Reflection.Assembly assembly = typeof(StartupLaunchPolicy).Assembly;
    Type? optionsType = assembly.GetType(
        "LibreHardwareMonitor.Solis.Desktop.DesktopRuntimeOptions");
    Type? runtimeType = assembly.GetType(
        "LibreHardwareMonitor.Solis.Desktop.DesktopRuntime");
    True(optionsType is not null, "缺少桌面运行时选项");
    True(runtimeType is not null, "缺少桌面运行时");

    var events = new List<string>();
    object options = Activator.CreateInstance(
        optionsType!,
        (Action)(() => events.Add("start")),
        (Action)(() => events.Add("stop")),
        (Action)(() => events.Add("save")),
        (Action)(() => events.Add("dispose")))!;
    object runtime = Activator.CreateInstance(runtimeType!, options)!;

    System.Reflection.MethodInfo start = runtimeType!.GetMethod("Start")!;
    System.Reflection.MethodInfo stop = runtimeType.GetMethod("Stop")!;
    System.Reflection.MethodInfo save = runtimeType.GetMethod("Save")!;
    System.Reflection.MethodInfo dispose = runtimeType.GetMethod("Dispose")!;

    start.Invoke(runtime, null);
    start.Invoke(runtime, null);
    save.Invoke(runtime, null);
    stop.Invoke(runtime, null);
    stop.Invoke(runtime, null);
    dispose.Invoke(runtime, null);
    dispose.Invoke(runtime, null);

    Equal(
        "start,save,stop,dispose",
        string.Join(",", events),
        "桌面运行时必须限制重复启动、停止和释放，并保持保存顺序");
}

static void WpfBackendUsesOnlyDisplayedHardwareGroups()
{
    var computer = new LibreHardwareMonitor.Hardware.Computer();
    LibreHardwareMonitor.Solis.Desktop.SolisDesktopHardwareProfile.Apply(
        computer);

    True(computer.IsCpuEnabled, "WPF 后台必须采集 CPU");
    True(computer.IsGpuEnabled, "WPF 后台必须采集 GPU");
    True(computer.IsMemoryEnabled, "WPF 后台必须采集内存");
    True(computer.IsStorageEnabled, "WPF 后台必须采集存储");
    True(!computer.IsMotherboardEnabled, "WPF 后台不应加载主板传感器树");
    True(!computer.IsNetworkEnabled, "网络吞吐由 SolisRuntime 独立采集");
    True(!computer.IsControllerEnabled, "WPF 后台不应加载风扇控制器");
    True(!computer.IsPowerMonitorEnabled, "WPF 后台不应加载独立功率监控器");
    True(!computer.IsPsuEnabled, "WPF 后台不应加载电源传感器");
    True(!computer.IsBatteryEnabled, "WPF 后台不应加载电池传感器");
}

static void WpfBackendExposesReusableRuntime()
{
    Type? backendType = typeof(StartupLaunchPolicy).Assembly.GetType(
        "LibreHardwareMonitor.Solis.Desktop.SolisDesktopBackend");
    True(backendType is not null, "缺少原生 WPF 可复用后台");
    True(backendType!.GetMethod("Start") is not null,
        "WPF 后台缺少启动入口");
    True(backendType.GetMethod("Save") is not null,
        "WPF 后台缺少保存入口");
    True(backendType.GetMethod("Dispose") is not null,
        "WPF 后台缺少释放入口");
    True(backendType.GetProperty("Runtime")?.PropertyType ==
         typeof(LibreHardwareMonitor.Solis.SolisRuntime),
        "WPF 后台必须公开统一 SolisRuntime");
    True(backendType.GetProperty("Settings")?.PropertyType ==
         typeof(LibreHardwareMonitor.Utilities.PersistentSettings),
        "WPF 后台必须公开统一持久化设置");
}

static void WpfWindowLifecyclePolicyIsTrayFirst()
{
    System.Reflection.Assembly assembly =
        typeof(LibreHardwareMonitor.UI.TreeModel).Assembly;
    Type? requestType = assembly.GetType(
        "LibreHardwareMonitor.Solis.Desktop.DesktopWindowRequest");
    Type? actionType = assembly.GetType(
        "LibreHardwareMonitor.Solis.Desktop.DesktopWindowAction");
    Type? policyType = assembly.GetType(
        "LibreHardwareMonitor.Solis.Desktop.DesktopWindowPolicy");
    True(requestType is not null, "未找到 WPF 窗口生命周期请求");
    True(actionType is not null, "未找到 WPF 窗口生命周期动作");
    True(policyType is not null, "未找到 WPF 窗口生命周期策略");

    System.Reflection.MethodInfo? decide =
        policyType!.GetMethod("Decide");
    True(decide is not null, "未找到 WPF 窗口生命周期决策入口");

    object close = Enum.Parse(requestType!, "Close");
    object minimize = Enum.Parse(requestType!, "Minimize");
    object exit = Enum.Parse(requestType!, "Exit");
    Equal("Hide", decide!.Invoke(null, [close])!.ToString(),
        "关闭窗口应隐藏到托盘");
    Equal("Hide", decide.Invoke(null, [minimize])!.ToString(),
        "最小化窗口应隐藏到托盘");
    Equal("Shutdown", decide.Invoke(null, [exit])!.ToString(),
        "只有显式退出才应终止应用");
}

static void DesktopStartupSettingsControllerCoordinatesOptions()
{
    bool silentStartup = true;
    bool autoStart = true;
    var controller =
        new LibreHardwareMonitor.Solis.Desktop.DesktopStartupSettingsController(
            () => silentStartup,
            value => silentStartup = value,
            () => autoStart,
            value => autoStart = value);

    controller.SetAutoStart(false);
    True(!autoStart, "关闭开机启动必须立即关闭开机启动");
    True(!silentStartup, "关闭开机启动必须同时关闭静默启动");
    Equal("未启用开机启动", controller.GetSummary(),
        "关闭开机启动后的摘要错误");

    autoStart = true;
    silentStartup = true;
    controller.SetSilentStartup(false);
    True(autoStart, "关闭静默启动不得关闭开机启动");
    True(!silentStartup, "关闭静默启动必须立即生效");
    Equal("开机启动 · 显示控制台", controller.GetSummary(),
        "显示控制台启动摘要错误");

    controller.SetSilentStartup(true);
    Equal("开机启动 · 静默进入托盘", controller.GetSummary(),
        "静默启动摘要错误");
}

static void DesktopHardwareMetricsPumpPublishesOneCycle()
{
    var events = new List<string>();
    MappedHardwareMetrics expected = HardwareMetricMapper.Map(Snapshot(
        Sensor(
            SolisHardwareKind.Cpu,
            "/cpu/0",
            "Test CPU",
            SolisSensorKind.Load,
            "CPU Total",
            42)));
    MappedHardwareMetrics? published = null;
    var pump = new LibreHardwareMonitor.Solis.Desktop.DesktopHardwareMetricsPump(
        () => events.Add("refresh"),
        () =>
        {
            events.Add("map");
            return expected;
        },
        metrics =>
        {
            events.Add("publish");
            published = metrics;
        });

    True(pump.CollectOnce(), "首次硬件采集应执行");
    Equal("refresh,map,publish", string.Join(",", events),
        "硬件采集必须按刷新、映射、发布顺序执行");
    True(ReferenceEquals(expected, published),
        "硬件采集必须发布本轮完整映射结果");
}

static void SilentStartupOnlyAffectsWindowsStartup()
{
    True(!StartupLaunchPolicy.ShouldStartHidden(Array.Empty<string>(), silentStartupEnabled: true),
        "手动启动不应被静默启动设置隐藏");
    True(StartupLaunchPolicy.ShouldStartHidden(
            [StartupLaunchPolicy.WindowsStartupArgument],
            silentStartupEnabled: true),
        "Windows 开机启动且启用静默时应隐藏");
    True(!StartupLaunchPolicy.ShouldStartHidden(
            [StartupLaunchPolicy.WindowsStartupArgument],
            silentStartupEnabled: false),
        "关闭静默启动后，Windows 开机启动也应显示控制台");
}

static void DeveloperModeUnlockRequiresTenClicks()
{
    var tracker = new DeveloperModeUnlockTracker();
    DateTimeOffset started = new(
        2026, 7, 24, 20, 0, 0, TimeSpan.FromHours(8));

    for (int index = 0; index < 9; index++)
    {
        True(!tracker.RegisterClick(started.AddSeconds(index)),
            "前九次点击不应解锁开发者入口");
    }
    True(tracker.RegisterClick(started.AddSeconds(9)),
        "十秒内第十次点击应解锁开发者入口");

    tracker.Reset();
    True(!tracker.RegisterClick(started), "重置后的首次点击不应解锁");
    True(!tracker.RegisterClick(started.AddSeconds(10).AddMilliseconds(1)),
        "超过十秒的点击应重新开始计数");
}

static void SolisResetPreservesUpstreamSettingsAndFiles()
{
    var settings = new LibreHardwareMonitor.Utilities.PersistentSettings();
    settings.SetValue("solis.developerMode.unlocked", true);
    settings.SetValue("solis.network.interfaceId", "adapter");
    settings.SetValue("startMinMenuItem", true);
    settings.SetValue("theme", "dark");
    settings.SetValue("sensor.cpu.enabled", true);

    SolisSettingsResetter.ClearPersistentSettings(settings);

    True(!settings.Contains("solis.developerMode.unlocked"),
        "开发者解锁状态应被清理");
    True(!settings.Contains("solis.network.interfaceId"),
        "Solis 网卡选择应被清理");
    True(!settings.Contains("startMinMenuItem"),
        "静默启动设置应被清理");
    Equal("dark", settings.GetValue("theme", string.Empty),
        "上游主题设置必须保留");
    True(settings.GetValue("sensor.cpu.enabled", false),
        "上游传感器设置必须保留");

    SolisSettingsResetter.RequestDevicePageAfterRestart(settings);
    True(SolisSettingsResetter.ConsumeDevicePageAfterRestart(settings),
        "恢复默认设置后首次重启应请求打开设备页");
    True(!SolisSettingsResetter.ConsumeDevicePageAfterRestart(settings),
        "设备页重启请求只能消费一次");

    string directory = Path.Combine(
        Path.GetTempPath(),
        $"SolisMonitor.ResetTests.{Guid.NewGuid():N}");
    try
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "settings.json"), "{}");
        File.WriteAllText(Path.Combine(directory, "weather.json"), "{}");
        Directory.CreateDirectory(Path.Combine(directory, "Notifications"));
        File.WriteAllText(
            Path.Combine(directory, "Notifications", "pending.json"),
            "{}");
        Directory.CreateDirectory(Path.Combine(directory, "Logs"));
        File.WriteAllText(Path.Combine(directory, "Logs", "keep.log"), "keep");
        Directory.CreateDirectory(Path.Combine(directory, "Firmware"));
        File.WriteAllText(
            Path.Combine(directory, "Firmware", "keep.bin"),
            "keep");

        SolisSettingsResetter.ClearLocalData(directory);

        True(!File.Exists(Path.Combine(directory, "settings.json")),
            "设备配对设置应被清理");
        True(!File.Exists(Path.Combine(directory, "weather.json")),
            "天气设置应被清理");
        True(!Directory.Exists(Path.Combine(directory, "Notifications")),
            "通知临时状态应被清理");
        True(File.Exists(Path.Combine(directory, "Logs", "keep.log")),
            "日志必须保留");
        True(File.Exists(Path.Combine(directory, "Firmware", "keep.bin")),
            "固件文件必须保留");
    }
    finally
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, true);
    }
}

static void PersistentSettingsDoNotRestoreOrSaveSensorHistory()
{
    string directory = Path.Combine(
        Path.GetTempPath(),
        $"SolisMonitor.SensorHistoryTests.{Guid.NewGuid():N}");
    try
    {
        Directory.CreateDirectory(directory);
        string source = Path.Combine(directory, "source.config");
        string saved = Path.Combine(directory, "saved.config");
        File.WriteAllText(
            source,
            """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <appSettings>
                <add key="/intelcpu/0/load/0/values" value="history" />
                <add key="solis.network.interfaceId" value="adapter" />
              </appSettings>
            </configuration>
            """);

        var settings = new LibreHardwareMonitor.Utilities.PersistentSettings();
        settings.Load(source);

        True(!settings.Contains("/intelcpu/0/load/0/values"),
            "启动时不应恢复传感器历史");
        Equal("adapter",
            settings.GetValue("solis.network.interfaceId", string.Empty),
            "清理历史时必须保留普通设置");

        settings.SetValue("/gpu-nvidia/0/temperature/0/values", "history");
        settings.Save(saved);

        var reloaded = new LibreHardwareMonitor.Utilities.PersistentSettings();
        reloaded.Load(saved);
        True(!reloaded.Contains("/gpu-nvidia/0/temperature/0/values"),
            "保存配置时不应持久化传感器历史");
        Equal("adapter",
            reloaded.GetValue("solis.network.interfaceId", string.Empty),
            "保存配置时必须保留普通设置");
    }
    finally
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, true);
    }
}

static void LiveSensorHistoryIsDisabledByDefault()
{
    var settings = new LibreHardwareMonitor.Utilities.PersistentSettings();
    var hardware = new SensorHistoryTestHardware(settings);
    Type sensorType = typeof(LibreHardwareMonitor.Hardware.Hardware).Assembly.GetType(
        "LibreHardwareMonitor.Hardware.Sensor",
        throwOnError: true)!;
    object sensorInstance = Activator.CreateInstance(
        sensorType,
        System.Reflection.BindingFlags.Instance |
        System.Reflection.BindingFlags.Public |
        System.Reflection.BindingFlags.NonPublic,
        binder: null,
        args:
        [
            "Test Load",
            0,
            LibreHardwareMonitor.Hardware.SensorType.Load,
            hardware,
            settings
        ],
        culture: null)!;
    var sensor = (LibreHardwareMonitor.Hardware.ISensor)sensorInstance;
    System.Reflection.PropertyInfo valueProperty = sensorType.GetProperty(
        nameof(LibreHardwareMonitor.Hardware.ISensor.Value))!;

    valueProperty.SetValue(sensorInstance, 41F);
    valueProperty.SetValue(sensorInstance, 42F);
    valueProperty.SetValue(sensorInstance, 43F);
    valueProperty.SetValue(sensorInstance, 44F);

    Near(44D, sensor.Value, "关闭历史后仍应保留最新传感器读数");
    True(!sensor.Values.Any(), "实时传感器历史默认应为空");
}

static void DeveloperPlotIsCreatedOnlyWhenDeveloperModeOpens()
{
    Type mainFormType = typeof(LibreHardwareMonitor.UI.MainForm);
    Type plotPanelType = mainFormType.Assembly.GetType(
        "LibreHardwareMonitor.UI.PlotPanel",
        throwOnError: true)!;
    System.Reflection.ConstructorInfo mainFormConstructor =
        mainFormType.GetConstructor([typeof(string[])])!;
    System.Reflection.ConstructorInfo plotPanelConstructor =
        plotPanelType.GetConstructors(
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic).Single();
    System.Reflection.MethodInfo initializePlot =
        mainFormType.GetMethod(
            "InitializePlotForm",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic)!;
    System.Reflection.MethodInfo? ensurePlot =
        mainFormType.GetMethod(
            "EnsureDeveloperPlotInitialized",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic);
    System.Reflection.MethodInfo setDeveloperMode =
        mainFormType.GetMethod(
            "SetDeveloperMode",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic)!;

    True(!MethodBodyContainsMetadataToken(
            mainFormConstructor,
            plotPanelConstructor.MetadataToken),
        "普通启动构造函数不应创建开发者绘图面板");
    True(!MethodBodyContainsMetadataToken(
            mainFormConstructor,
            initializePlot.MetadataToken),
        "普通启动构造函数不应创建开发者绘图窗体");
    True(ensurePlot is not null,
        "应提供开发者绘图懒加载入口");
    True(MethodBodyContainsMetadataToken(
            setDeveloperMode,
            ensurePlot!.MetadataToken),
        "进入开发者模式时应触发绘图懒加载");
    True(MethodBodyContainsMetadataToken(
            ensurePlot,
            plotPanelConstructor.MetadataToken),
        "绘图懒加载入口应负责创建绘图面板");
}

static bool MethodBodyContainsMetadataToken(
    System.Reflection.MethodBase method,
    int metadataToken)
{
    byte[] body = method.GetMethodBody()?.GetILAsByteArray() ??
        Array.Empty<byte>();
    byte[] token = BitConverter.GetBytes(metadataToken);
    return body.AsSpan().IndexOf(token) >= 0;
}

private sealed class SensorHistoryTestHardware(
    LibreHardwareMonitor.Hardware.ISettings settings)
    : LibreHardwareMonitor.Hardware.Hardware(
        "Sensor History Test",
        new LibreHardwareMonitor.Hardware.Identifier("sensor-history-test"),
        settings)
{
    public override LibreHardwareMonitor.Hardware.HardwareType HardwareType =>
        LibreHardwareMonitor.Hardware.HardwareType.Cpu;

    public override void Update()
    {
    }
}

static void SolisExecutableConfigMigratesOnce()
{
    string directory = Path.Combine(
        Path.GetTempPath(),
        $"SolisMonitor.MigrationTests.{Guid.NewGuid():N}");
    try
    {
        Directory.CreateDirectory(directory);
        string legacy = Path.Combine(directory, "LibreHardwareMonitor.config");
        string current = Path.Combine(directory, "SolisMonitor.config");
        File.WriteAllText(legacy, "legacy");

        True(SolisSettingsMigration.CopyLegacyExecutableConfig(current, legacy),
            "首次启动应复制旧配置");
        Equal("legacy", File.ReadAllText(current),
            "迁移内容必须保持不变");

        File.WriteAllText(current, "current");
        True(!SolisSettingsMigration.CopyLegacyExecutableConfig(current, legacy),
            "已有新配置时不得再次覆盖");
        Equal("current", File.ReadAllText(current),
            "新配置必须优先保留");
    }
    finally
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, true);
    }
}

static void SolisUserConfigMigrationPreservesExistingCopy()
{
    string directory = Path.Combine(
        Path.GetTempPath(),
        $"SolisMonitor.UserConfigMigrationTests.{Guid.NewGuid():N}");
    try
    {
        string executableDirectory = Path.Combine(directory, "app");
        string userDirectory = Path.Combine(directory, "user");
        Directory.CreateDirectory(executableDirectory);

        string executableConfig =
            Path.Combine(executableDirectory, "SolisMonitor.config");
        string legacyConfig =
            Path.Combine(executableDirectory, "LibreHardwareMonitor.config");
        string userConfig =
            Path.Combine(userDirectory, "SolisMonitor.config");
        File.WriteAllText(executableConfig, "portable-current");
        File.WriteAllText(executableConfig + ".backup", "portable-backup");
        File.WriteAllText(legacyConfig, "legacy");

        True(SolisSettingsMigration.CopyExecutableConfigToUserDirectory(
                userConfig,
                executableConfig,
                legacyConfig),
            "首次安装版启动应把便携配置复制到用户目录");
        Equal("portable-current", File.ReadAllText(userConfig),
            "应优先迁移 SolisMonitor.config");
        Equal("portable-backup", File.ReadAllText(userConfig + ".backup"),
            "配置备份也必须随迁移保留");

        File.WriteAllText(userConfig, "user-current");
        True(!SolisSettingsMigration.CopyExecutableConfigToUserDirectory(
                userConfig,
                executableConfig,
                legacyConfig),
            "用户目录已有配置时不得再次覆盖");
        Equal("user-current", File.ReadAllText(userConfig),
            "升级后必须保留用户目录中的现有配置");

        File.Delete(userConfig);
        File.Delete(userConfig + ".backup");
        File.Delete(executableConfig);
        True(SolisSettingsMigration.CopyExecutableConfigToUserDirectory(
                userConfig,
                executableConfig,
                legacyConfig),
            "没有新版便携配置时应兼容旧 LibreHardwareMonitor.config");
        Equal("legacy", File.ReadAllText(userConfig),
            "旧上游配置迁移内容必须保持不变");
    }
    finally
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, true);
    }
}

static void SecondLaunchSignalsExistingInstance()
{
    string instanceName = $"SolisMonitor.SmokeTests.{Guid.NewGuid():N}";
    using var primary = new SingleInstanceCoordinator(instanceName);
    using var secondary = new SingleInstanceCoordinator(instanceName);
    using var signal = new ManualResetEventSlim();

    True(primary.IsPrimary, "第一个协调器应成为主实例");
    True(!secondary.IsPrimary, "第二个协调器不应成为主实例");

    RegisteredWaitHandle registration = primary.RegisterShowWindowRequest(signal.Set);
    try
    {
        secondary.SignalPrimaryInstance();
        True(signal.Wait(TimeSpan.FromSeconds(2)), "第二次启动未通知已有实例");
    }
    finally
    {
        registration.Unregister(null);
    }
}

static void ImmediateRelaunchTakesOverExitingInstance()
{
    string instanceName = $"SolisMonitor.SmokeTests.{Guid.NewGuid():N}";
    using var primaryReady = new ManualResetEventSlim();
    using var releasePrimary = new ManualResetEventSlim();
    Exception? primaryFailure = null;
    var primaryThread = new Thread(() =>
    {
        try
        {
            using var primary = new SingleInstanceCoordinator(instanceName);
            True(primary.IsPrimary, "第一个协调器应成为主实例");
            primaryReady.Set();
            releasePrimary.Wait(TimeSpan.FromSeconds(5));
        }
        catch (Exception exception)
        {
            primaryFailure = exception;
            primaryReady.Set();
        }
    });
    primaryThread.Start();
    True(primaryReady.Wait(TimeSpan.FromSeconds(2)), "主实例启动测试超时");
    if (primaryFailure is not null)
        throw new InvalidOperationException("主实例启动失败", primaryFailure);

    using var secondary = new SingleInstanceCoordinator(instanceName);
    True(!secondary.IsPrimary, "旧实例退出前新实例不应立即成为主实例");
    releasePrimary.Set();
    True(
        secondary.TryBecomePrimary(TimeSpan.FromSeconds(2)),
        "旧实例退出后首次重启未接管单实例互斥量");
    True(secondary.IsPrimary, "接管后协调器没有成为主实例");
    True(primaryThread.Join(TimeSpan.FromSeconds(2)), "旧实例退出测试超时");
    if (primaryFailure is not null)
        throw new InvalidOperationException("旧实例退出失败", primaryFailure);
}

static void NotificationActivationSignalsDiagnostics()
{
    string instanceName = $"SolisMonitor.SmokeTests.{Guid.NewGuid():N}";
    using var primary = new SingleInstanceCoordinator(instanceName);
    using var secondary = new SingleInstanceCoordinator(instanceName);
    using var signal = new ManualResetEventSlim();

    RegisteredWaitHandle registration = primary.RegisterDiagnosticsRequest(signal.Set);
    try
    {
        secondary.SignalDiagnosticsRequest();
        True(signal.Wait(TimeSpan.FromSeconds(2)), "通知点击未请求已有实例打开诊断页");
    }
    finally
    {
        registration.Unregister(null);
    }
}
}
