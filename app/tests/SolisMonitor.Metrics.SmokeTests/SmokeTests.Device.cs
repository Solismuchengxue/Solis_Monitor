internal static partial class SmokeTests
{
static void DeviceTrayPresentationUsesDiscoveredDevice()
{
    var state = new DeviceDiscoveryState(
        new DiscoveredDevice(
            "Solis_Monitor_A1B2",
            "1.0.0",
            "192.168.0.42",
            -57,
            true),
        false,
        null);

    DeviceTrayPresentation available = DeviceTrayPresentation.From(state);
    Equal("副屏：Solis_Monitor_A1B2 · 192.168.0.42", available.StatusText,
        "托盘副屏状态错误");
    Equal("http://192.168.0.42/", available.WebUiUrl, "副屏 WebUI 地址错误");
    True(available.CanOpenWebUi, "发现设备后应允许打开 WebUI");

    DeviceTrayPresentation scanning = DeviceTrayPresentation.From(
        new DeviceDiscoveryState(null, true, null));
    Equal("副屏：正在发现…", scanning.StatusText, "扫描状态错误");
    True(!scanning.CanOpenWebUi, "扫描期间不应启用 WebUI");

    DeviceTrayPresentation missing = DeviceTrayPresentation.From(
        new DeviceDiscoveryState(null, false, "NotFound"));
    Equal("副屏：未连接", missing.StatusText, "未连接状态错误");
    True(!missing.CanOpenWebUi, "未连接时不应启用 WebUI");
}

static void DeviceOfflineNotificationIsDebounced()
{
    var monitor = new DeviceOfflineMonitor(TimeSpan.FromMinutes(2));
    DateTimeOffset started = new(2026, 7, 23, 8, 0, 0, TimeSpan.FromHours(8));
    var offline = new DeviceDiscoveryState(null, false, "NotFound");
    var online = new DeviceDiscoveryState(
        new DiscoveredDevice(
            "Solis_Monitor_A1B2",
            "1.0.0",
            "192.168.0.42",
            -57,
            true),
        false,
        null);

    True(monitor.Observe(offline, started) is null,
        "本次运行尚未见过设备时不应通知");
    True(monitor.Observe(offline, started.AddMinutes(10)) is null,
        "应用启动时设备离线不应产生延迟误报");

    True(monitor.Observe(online, started.AddMinutes(11)) is null,
        "设备在线不应通知");
    True(monitor.Observe(offline, started.AddMinutes(12)) is null,
        "刚确认离线不应立即通知");
    True(monitor.Observe(offline, started.AddMinutes(13).AddSeconds(59)) is null,
        "离线不足两分钟不应通知");

    DeviceOfflineNotification? first =
        monitor.Observe(offline, started.AddMinutes(14));
    Equal("Solis_Monitor_A1B2", first?.HostName, "离线通知设备名错误");
    Equal(started.AddMinutes(12), first?.OfflineSince, "离线起始时间错误");
    True(monitor.Observe(offline, started.AddMinutes(20)) is null,
        "同一次持续离线不应重复通知");

    True(monitor.Observe(online, started.AddMinutes(21)) is null,
        "恢复连接不应通知");
    True(monitor.Observe(offline, started.AddMinutes(22)) is null,
        "第二次刚离线不应立即通知");
    True(monitor.Observe(
            offline,
            started.AddMinutes(23).AddSeconds(59),
            notificationsEnabled: false) is null,
        "首次引导期间不应发送副屏离线通知");
    True(monitor.Observe(offline, started.AddMinutes(24)) is null,
        "退出首次引导后应重新开始离线计时");
    True(monitor.Observe(offline, started.AddMinutes(26)) is not null,
        "恢复后再次连续离线两分钟应重新通知");
}

static void DeviceProvisioningMaintenanceSuppressesOfflineNotification()
{
    var monitor = new DeviceOfflineMonitor(TimeSpan.FromMinutes(2));
    DateTimeOffset started = new(2026, 7, 24, 9, 0, 0, TimeSpan.FromHours(8));
    var online = new DeviceDiscoveryState(
        new DiscoveredDevice(
            "Solis_Monitor_A1B2",
            "1.0.0",
            "192.168.0.42",
            -57,
            true),
        false,
        null);
    var offline = new DeviceDiscoveryState(null, false, "NotFound");

    monitor.Observe(online, started);
    monitor.BeginMaintenance(started.AddMinutes(1), TimeSpan.FromMinutes(10));
    True(monitor.IsMaintenanceActive(started.AddMinutes(10)),
        "十分钟维护窗口结束前应保持有效");
    True(monitor.Observe(offline, started.AddMinutes(10)) is null,
        "维护窗口内不应发送离线通知");

    True(monitor.Observe(offline, started.AddMinutes(11)) is null,
        "维护结束时应重新开始普通离线计时");
    True(!monitor.IsMaintenanceActive(started.AddMinutes(11)),
        "维护窗口到期后应自动结束");
    True(monitor.Observe(offline, started.AddMinutes(13)) is not null,
        "维护结束后持续离线两分钟应恢复通知");

    monitor.BeginMaintenance(started.AddMinutes(20), TimeSpan.FromMinutes(10));
    monitor.Observe(offline, started.AddMinutes(21));
    monitor.Observe(online, started.AddMinutes(22));
    True(!monitor.IsMaintenanceActive(started.AddMinutes(22)),
        "设备提前恢复连接时应立即结束维护窗口");
}

static void DeviceTokenMismatchIsMatchedAndDebounced()
{
    var tracker = new DeviceAuthorizationTracker();
    var monitor = new DeviceTokenMismatchMonitor();
    DateTimeOffset now = new(2026, 7, 23, 9, 0, 0, TimeSpan.FromHours(8));
    var discovery = new DeviceDiscoveryState(
        new DiscoveredDevice(
            "Solis_Monitor_A1B2",
            "1.0.0",
            "192.168.0.42",
            -57,
            true),
        false,
        null);

    tracker.Observe(IPAddress.Parse("192.168.0.99"), false, now);
    True(monitor.Observe(discovery, tracker.Current) is null,
        "未知局域网客户端的 401 不应归因到副屏");

    tracker.Observe(IPAddress.Parse("192.168.0.42"), false, now.AddSeconds(1));
    Equal("Solis_Monitor_A1B2", monitor.Observe(discovery, tracker.Current),
        "副屏令牌失配通知设备名错误");
    tracker.Observe(IPAddress.Parse("192.168.0.42"), false, now.AddSeconds(2));
    True(monitor.Observe(discovery, tracker.Current) is null,
        "持续失配不应重复通知");

    tracker.Observe(IPAddress.Parse("::ffff:192.168.0.42"), true, now.AddSeconds(3));
    True(monitor.Observe(discovery, tracker.Current) is null,
        "恢复鉴权成功时不应通知");
    tracker.Observe(IPAddress.Parse("192.168.0.42"), false, now.AddSeconds(4));
    Equal("Solis_Monitor_A1B2", monitor.Observe(discovery, tracker.Current),
        "恢复后再次失配应重新通知");
}

static void ClearingPairingResetsDeviceNotifications()
{
    var offlineMonitor = new DeviceOfflineMonitor(TimeSpan.FromMinutes(2));
    var mismatchMonitor = new DeviceTokenMismatchMonitor();
    DateTimeOffset now = new(2026, 7, 24, 16, 0, 0, TimeSpan.FromHours(8));
    var paired = new DeviceDiscoveryState(
        new DiscoveredDevice(
            "Solis_Monitor_A1B2",
            "1.0.0",
            "192.168.0.42",
            -57,
            true),
        false,
        null);
    var cleared = new DeviceDiscoveryState(null, false, "NotPaired");
    var unauthorized = new DeviceAuthorizationState(
        "192.168.0.42",
        false,
        now);

    offlineMonitor.Observe(paired, now);
    Equal("Solis_Monitor_A1B2", mismatchMonitor.Observe(paired, unauthorized),
        "测试前置条件没有产生令牌失配通知");

    offlineMonitor.ForgetDevice();
    mismatchMonitor.Reset();

    True(offlineMonitor.Observe(cleared, now.AddMinutes(10)) is null,
        "主动清除配对后不应继续发送离线通知");
    True(mismatchMonitor.Observe(cleared, unauthorized) is null,
        "主动清除配对后不应继续发送令牌失配通知");
}

static void DeviceIdentityResponseIsParsed()
{
    const string json =
        "{\"product\":\"Solis Monitor\",\"hostname\":\"Solis_Monitor_A1B2\"," +
        "\"firmware\":\"1.0.0\",\"ip\":\"192.168.0.42\",\"rssi\":-57,\"paired\":true}";

    True(DeviceDiscoveryProtocol.TryParse(json, out DiscoveredDevice? device),
        "合法设备身份响应未被识别");
    Equal("Solis_Monitor_A1B2", device?.HostName, "设备名解析错误");
    Equal("192.168.0.42", device?.IpAddress, "设备 IP 解析错误");
    Equal(-57, device?.Rssi, "设备 RSSI 解析错误");
    True(device?.Paired == true, "配对状态解析错误");
    True(!DeviceDiscoveryProtocol.TryParse(
            "{\"product\":\"Other\",\"hostname\":\"x\",\"firmware\":\"1\",\"ip\":\"192.168.0.2\",\"rssi\":null,\"paired\":false}",
            out _),
        "非 Solis 设备不应被自动发现");
}

static void DeviceDiscoveryCandidatesStayInLocalSubnet()
{
    IReadOnlyList<IPAddress> candidates = DeviceDiscoveryProtocol.BuildSubnetCandidates(
        IPAddress.Parse("192.168.0.27"),
        24);

    Equal(253, candidates.Count, "本地 /24 候选数量错误");
    True(candidates.Contains(IPAddress.Parse("192.168.0.1")), "未包含网关候选");
    True(candidates.Contains(IPAddress.Parse("192.168.0.254")), "未包含末尾主机候选");
    True(!candidates.Contains(IPAddress.Parse("192.168.0.27")), "不应扫描本机地址");
    True(!candidates.Contains(IPAddress.Parse("192.168.1.1")), "不应越过本地子网");

    IReadOnlyList<IPAddress> wideNetwork = DeviceDiscoveryProtocol.BuildSubnetCandidates(
        IPAddress.Parse("10.20.30.40"),
        16);
    Equal(253, wideNetwork.Count, "大子网应限制到本机所在 /24");
    True(wideNetwork.All(address => address.ToString().StartsWith("10.20.30.",
        StringComparison.Ordinal)), "大子网扫描越过本机 /24");
    True(DeviceDiscoveryProtocol.IsPrivateIpv4(IPAddress.Parse("192.168.0.27")),
        "私有地址识别错误");
    True(!DeviceDiscoveryProtocol.IsPrivateIpv4(IPAddress.Parse("203.0.113.27")),
        "自动发现不应扫描公网地址");
}

static void PairedDeviceProbeFailureDoesNotScanSubnet()
{
    string directory = Path.Combine(
        Path.GetTempPath(),
        $"SolisMonitor.PairedDiscovery-{Guid.NewGuid():N}");
    try
    {
        var store = new DeviceTokenStore(directory);
        store.MarkPaired("Solis_Monitor_A1B2", "192.168.0.42");
        var handler = new CountingDiscoveryHttpMessageHandler();
        using var httpClient = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        using var service = new DeviceDiscoveryService(store, httpClient);

        service.Start();
        True(
            handler.Started.Wait(TimeSpan.FromSeconds(5)),
            "已配对设备探测没有启动");
        True(
            SpinWait.SpinUntil(
                () => !service.Current.IsScanning,
                TimeSpan.FromSeconds(5)),
            "已配对设备探测没有完成");

        Equal(1, handler.RequestCount,
            "已配对设备探测失败后不应自动扫描整个子网");
        Equal("http://192.168.0.42/api/device", handler.FirstRequestUri,
            "自动探测没有只请求已保存的副屏 IP");
    }
    finally
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, true);
    }
}

static void PairedDeviceCanBeRefreshedImmediatelyAfterRestart()
{
    string directory = Path.Combine(
        Path.GetTempPath(),
        $"SolisMonitor.PairedRecovery-{Guid.NewGuid():N}");
    try
    {
        var store = new DeviceTokenStore(directory);
        store.MarkPaired("Solis_Monitor_A1B2", "192.168.0.42");
        var handler = new RecoveringDiscoveryHttpMessageHandler();
        using var httpClient = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        using var service = new DeviceDiscoveryService(store, httpClient);

        True(
            !service.RefreshPairedDeviceAsync().GetAwaiter().GetResult(),
            "副屏仍离线时不应报告恢复成功");
        handler.Available = true;
        True(
            service.RefreshPairedDeviceAsync().GetAwaiter().GetResult(),
            "副屏恢复后没有立即刷新已配对设备");
        Equal(
            "Solis_Monitor_A1B2",
            service.Current.Device?.HostName,
            "快速恢复没有更新当前副屏");
        Equal(2, handler.RequestCount, "快速恢复不应扫描整个子网");
    }
    finally
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, true);
    }
}

static void DevicePairingCodeRequiresSixDigits()
{
    True(DevicePairingProtocol.IsValidCode("012345"), "前导零的六位配对码应有效");
    True(DevicePairingProtocol.IsValidCode("987654"), "六位数字配对码应有效");
    True(!DevicePairingProtocol.IsValidCode("12345"), "五位配对码不应有效");
    True(!DevicePairingProtocol.IsValidCode("1234567"), "七位配对码不应有效");
    True(!DevicePairingProtocol.IsValidCode("12A456"), "包含非数字的配对码不应有效");
    True(!DevicePairingProtocol.IsValidCode("１２３４５６"), "全角数字不应被作为协议配对码");
}

static void DeviceDisplaySettingsRoundTrip()
{
    const string json =
        "{\"brightness\":75,\"night_enabled\":true,\"night_start\":1410," +
        "\"night_end\":450,\"utc_offset\":480}";
    True(
        DeviceControlProtocol.TryParseSettings(
            json,
            out DeviceDisplaySettings? settings),
        "合法显示设置响应未被识别");
    Equal(75, settings?.BrightnessPercent, "亮度解析错误");
    True(settings?.NightEnabled == true, "夜间计划状态解析错误");
    Equal(1410, settings?.NightStartMinute, "夜间开始时间解析错误");
    Equal(450, settings?.NightEndMinute, "夜间结束时间解析错误");
    Equal(480, settings?.UtcOffsetMinutes, "UTC 偏移解析错误");

    True(
        !DeviceControlProtocol.TryParseSettings(
            "{\"brightness\":0,\"night_enabled\":true,\"night_start\":1," +
            "\"night_end\":2,\"utc_offset\":0}",
            out _),
        "越界亮度不应被接受");

    string directory = Path.Combine(
        Path.GetTempPath(),
        $"SolisMonitor.DeviceControl-{Guid.NewGuid():N}");
    try
    {
        var store = new DeviceTokenStore(directory);
        store.MarkPaired("Solis_Monitor_A1B2", "192.168.0.42");
        var handler = new DeviceControlHttpMessageHandler(json);
        using var httpClient = new HttpClient(handler);
        using var client = new DeviceControlClient(store, httpClient);

        DeviceControlResult loaded = client.LoadAsync()
            .GetAwaiter().GetResult();
        True(loaded.Success, loaded.Message);
        Equal(75, loaded.Settings?.BrightnessPercent,
            "控制客户端未解析当前亮度");

        var changed = new DeviceDisplaySettings(
            80, false, 23 * 60 + 30, 7 * 60 + 30, 480);
        DeviceControlResult saved = client.SaveAsync(changed)
            .GetAwaiter().GetResult();
        True(saved.Success, saved.Message);
        DeviceControlResult restarted = client.RestartAsync()
            .GetAwaiter().GetResult();
        True(restarted.Success, restarted.Message);
        Equal(3, handler.RequestCount, "设备控制请求数量错误");
        Equal(store.DeviceToken, handler.AuthorizationToken,
            "设备控制请求未使用当前配对令牌");
        True(handler.AllRequestsCloseConnection,
            "设备控制请求必须关闭连接，避免副屏重启后复用失效的 TCP 会话");
        True(
            handler.SavedBody?.Contains(
                "brightness=80",
                StringComparison.Ordinal) == true,
            "保存请求没有包含亮度");
    }
    finally
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, true);
    }
}

static void DeviceTokenPersists()
{
    string directory = Path.Combine(Path.GetTempPath(), $"SolisMonitor.TokenTest-{Guid.NewGuid():N}");
    try
    {
        var first = new DeviceTokenStore(directory);
        var second = new DeviceTokenStore(directory);
        True(DeviceToken.IsValid(first.DeviceToken), "生成的设备令牌格式无效");
        Equal(first.DeviceToken, second.DeviceToken, "设备令牌未持久化复用");
        True(DeviceToken.IsAuthorized($"Bearer {first.DeviceToken}", second.DeviceToken),
            "持久化令牌无法通过 Bearer 鉴权");
    }
    finally
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, true);
    }
}

static void DevicePairingCanBeClearedLocally()
{
    string directory = Path.Combine(
        Path.GetTempPath(),
        $"SolisMonitor.PairingTest-{Guid.NewGuid():N}");
    try
    {
        var store = new DeviceTokenStore(directory);
        string originalToken = store.DeviceToken;
        store.MarkPaired("Solis_Monitor_A1B2", "192.168.0.42");

        True(store.MatchesPairedDevice("Solis_Monitor_A1B2", "192.168.0.42"),
            "配对成功后没有保存副屏身份");

        store.ClearPairing();
        True(store.DeviceToken != originalToken, "清除配对后必须轮换设备令牌");
        True(!store.MatchesPairedDevice("Solis_Monitor_A1B2", "192.168.0.42"),
            "清除配对后仍保留副屏身份");

        var reloaded = new DeviceTokenStore(directory);
        Equal(store.DeviceToken, reloaded.DeviceToken, "新令牌没有持久化");
        True(!reloaded.LegacyPairingDiscoveryAllowed,
            "明确清除配对后不得再次采用旧版自动认领逻辑");
        True(!reloaded.MatchesPairedDevice("Solis_Monitor_A1B2", "192.168.0.42"),
            "重启后恢复了已经清除的副屏身份");
    }
    finally
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, true);
    }
}

static void DeviceDiscoveryShutdownWaitsForActiveScan()
{
    string directory = Path.Combine(
        Path.GetTempPath(),
        $"SolisMonitor.DiscoveryShutdown-{Guid.NewGuid():N}");
    try
    {
        var store = new DeviceTokenStore(directory);
        var handler = new BlockingDiscoveryHttpMessageHandler();
        using var httpClient = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        var service = new DeviceDiscoveryService(store, httpClient);

        service.Start();
        True(
            handler.Started.Wait(TimeSpan.FromSeconds(5)),
            "设备发现扫描没有启动");

        service.Dispose();
        Equal(0, handler.ActiveRequestCount,
            "释放返回后仍有设备发现请求在运行");

        service.Dispose();
    }
    finally
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, true);
    }
}
}
