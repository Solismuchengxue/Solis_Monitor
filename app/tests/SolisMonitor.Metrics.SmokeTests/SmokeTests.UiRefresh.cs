using System.Reflection;
using System.Windows.Controls;
using System.Windows.Threading;
using LibreHardwareMonitor.Solis.Desktop;
using LibreHardwareMonitor.UI.WpfViews;

internal static partial class SmokeTests
{
    // These tests catch rendering before the visibility gate, stopped coordination,
    // lost async results, delayed first frames and duplicate event subscriptions.
    static void HiddenPresenterKeepsBackgroundObservation() => UiRefreshTest(f =>
    {
        f.Visible = false;
        f.SetSentinels();
        for (int i = 0; i < 60; i++)
            f.Presenter.Refresh();
        f.AssertSentinels();
        True(f.Runtime.Diagnostics.DeviceApi.State != DiagnosticCheckState.Checking,
            "隐藏后设备 API 诊断观察停止");
    });

    static void PresenterVisibleRefreshRestoresLatestState() => UiRefreshTest(f =>
    {
        f.Visible = false;
        f.SetSentinels();
        f.Presenter.Refresh();
        f.Visible = true;
        f.Presenter.Refresh();
        Equal("尚未发现副屏", GetNamedText(f.View, "DeviceStateText"), "恢复后设备状态未更新");
        True(GetNamedText(f.View, "StartupSummaryText") != "sentinel", "恢复后启动页未更新");
        True(GetNamedText(f.View.ServiceView, "ApiStatus") != "sentinel", "恢复后服务页未更新");
    });

    static void HiddenPresenterRetainsAsyncSettings() => UiRefreshTest(f =>
    {
        var response = new TaskCompletionSource<HttpResponseMessage>();
        f.InstallControl(_ => response.Task);
        f.Pair();
        f.Presenter.Refresh();
        Equal(1, f.Requests, "设置读取没有开始");
        f.Visible = false;
        f.SetSentinels();
        response.SetResult(UiSettingsResponse(37));
        f.PumpUntil(() => !f.Get<bool>("_settingsLoading"));
        f.AssertSentinels();
        Equal(37, f.Get<DeviceDisplaySettings>("_deviceSettings").BrightnessPercent,
            "隐藏期间读取结果丢失");
        f.Visible = true;
        f.Presenter.Refresh();
        Equal("37%", GetNamedText(f.View, "BrightnessValueText"), "恢复后没有呈现读取结果");
    });

    static void HiddenPresenterRetriesSettingsAndResetsUnpaired() => UiRefreshTest(f =>
    {
        f.InstallControl(_ => Task.FromResult(f.Requests == 1
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            : UiSettingsResponse(42)));
        f.Pair();
        f.Visible = false;
        f.SetSentinels();
        f.Presenter.Refresh();
        f.PumpUntil(() => !f.Get<bool>("_settingsLoading"));
        f.Presenter.Refresh();
        f.PumpUntil(() => f.GetNullable<DeviceDisplaySettings>("_deviceSettings") is not null);
        Equal(2, f.Requests, "隐藏期间读取失败没有重试");
        f.Presenter.Refresh();
        Equal(2, f.Requests, "已读取设置仍重复请求");
        f.AssertSentinels();
        f.SetDiscovery(new DeviceDiscoveryState(null, false, "NotPaired"));
        f.Presenter.Refresh();
        True(f.GetNullable<DeviceDisplaySettings>("_deviceSettings") is null,
            "隐藏期间解除配对未清除旧设置");
    });

    static void HiddenPresenterKeepsSaveAndRestartResults() => UiRefreshTest(f =>
    {
        var pending = new Queue<TaskCompletionSource<HttpResponseMessage>>();
        f.InstallControl(_ =>
        {
            var response = new TaskCompletionSource<HttpResponseMessage>();
            pending.Enqueue(response);
            return response.Task;
        });
        f.Pair();
        f.Presenter.Refresh();
        pending.Dequeue().SetResult(UiSettingsResponse(30));
        f.PumpUntil(() => !f.Get<bool>("_settingsLoading"));
        f.Visible = false;
        f.SetSentinels();
        var settings = new DeviceDisplaySettings(45, true, 1320, 420, 480);
        Task save = f.InvokeTask("SaveNightBacklightAsync", settings);
        pending.Dequeue().SetResult(new HttpResponseMessage(HttpStatusCode.OK));
        f.PumpUntil(() => save.IsCompleted);
        save.GetAwaiter().GetResult();
        Equal(settings, f.Get<DeviceDisplaySettings>("_deviceSettings"), "夜间设置结果丢失");
        Task brightness = f.InvokeTask("SaveBrightnessAfterDelayAsync", settings, CancellationToken.None);
        f.PumpUntil(() => pending.Count > 0);
        pending.Dequeue().SetResult(new HttpResponseMessage(HttpStatusCode.OK));
        f.PumpUntil(() => brightness.IsCompleted);
        brightness.GetAwaiter().GetResult();
        Task restart = f.InvokeTask("RestartDeviceAsync");
        True(f.Get<bool>("_restartPending"), "隐藏期间重启状态没有开始");
        // Failure is intentional: no subsequent device discovery/network poll is started.
        pending.Dequeue().SetResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        f.PumpUntil(() => restart.IsCompleted);
        restart.GetAwaiter().GetResult();
        True(!f.Get<bool>("_restartPending"), "隐藏期间重启状态未结束");
        Equal(4, f.Requests, "初次读取、两次保存或重启重复执行");
        Equal(2, f.Results, "夜间保存或重启完成结果丢失");
        f.AssertSentinels();
        f.Visible = true;
        f.Presenter.Refresh();
        Equal("45%", GetNamedText(f.View, "BrightnessValueText"), "保存结果未在恢复后显示");
        True((bool)((System.Windows.Controls.Button)f.View.FindName("RestartDeviceButton")).IsEnabled,
            "恢复后重启按钮没有解除忙状态");
    });

    static void PresenterVisibilityCyclesKeepSingleSubscription() => UiRefreshTest(f =>
    {
        for (int i = 0; i < 50; i++)
        {
            f.Visible = false;
            f.Presenter.Refresh();
            f.Visible = true;
            f.Presenter.Refresh();
        }
        RaiseServiceEvent(f.View.ServiceView, "EditWeatherRequested");
        Equal(1, f.WeatherEdits, "显隐后事件重复订阅");
        f.Presenter.Dispose();
        f.Presenter.Dispose();
        ((Delegate?)typeof(SolisServiceView).GetField("EditWeatherRequested",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(f.View.ServiceView))
            ?.DynamicInvoke(f.View.ServiceView, EventArgs.Empty);
        Equal(1, f.WeatherEdits, "释放后仍处理视图事件");
    });

    static void DisposedPresenterIgnoresPendingSettings() => UiRefreshTest(f =>
    {
        var response = new TaskCompletionSource<HttpResponseMessage>();
        f.InstallControl(_ => response.Task);
        f.Pair();
        f.Presenter.Refresh();
        f.SetSentinels();
        f.Presenter.Dispose();
        response.SetResult(UiSettingsResponse(27));
        f.PumpUntil(() => !f.Get<bool>("_settingsLoading"));
        f.AssertSentinels();
        True(f.GetNullable<DeviceDisplaySettings>("_deviceSettings") is null,
            "释放后仍写入读取结果");
    });

    static void HostShowImmediatelyRefreshesWithoutStartingBackend() => UiRefreshTest(f =>
    {
        // Test the real ShowWindow entry without constructing the hardware backend/tray.
        Type type = typeof(SolisWpfDesktopHost);
        object host = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(type);
        var window = new SolisMainWindow(f.View);
        var presenter = UiCreatePresenter(f.Runtime, f.View, f.Startup, f.Actions,
            () => window.IsVisible && window.WindowState != System.Windows.WindowState.Minimized);
        UiSetField(host, "_window", window);
        UiSetField(host, "_presenter", presenter);
        try
        {
            for (int i = 0; i < 3; i++)
            {
                window.Hide();
                f.SetSentinels();
                type.GetMethod("ShowWindow")!.Invoke(host, null);
                Equal("尚未发现副屏", GetNamedText(f.View, "DeviceStateText"),
                    "ShowWindow 返回时还在等待下一次计时器刷新");
            }
            window.WindowState = System.Windows.WindowState.Minimized;
            f.SetSentinels();
            type.GetMethod("ShowWindow")!.Invoke(host, null);
            Equal(System.Windows.WindowState.Normal, window.WindowState, "没有恢复窗口");
            Equal("尚未发现副屏", GetNamedText(f.View, "DeviceStateText"), "最小化恢复未立即刷新");
        }
        finally
        {
            presenter.Dispose();
            window.Close();
        }
    });

    static void HiddenPresenterReducesSteadyRefreshAllocations() => UiRefreshTest(f =>
    {
        long Measure(bool visible)
        {
            f.Visible = visible;
            for (int i = 0; i < 100; i++) f.Presenter.Refresh();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 500; i++) f.Presenter.Refresh();
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }
        long visible = Measure(true);
        long hidden = Measure(false);
        Console.WriteLine($"隐藏刷新分配：visible={visible} hidden={hidden} bytes/500 cycles");
        True(visible > 0 && hidden <= visible * 0.8,
            $"隐藏刷新分配降幅不足 20%：visible={visible}, hidden={hidden}");
    });

    static HttpResponseMessage UiSettingsResponse(int brightness) => new(HttpStatusCode.OK)
    {
        Content = new StringContent($$"""{"brightness":{{brightness}},"night_enabled":false,"night_start":1320,"night_end":420,"utc_offset":480}""")
    };

    static SolisDesktopPresenter UiCreatePresenter(SolisRuntime runtime, SolisControlCenterView view,
        DesktopStartupSettingsController startup, SolisDesktopServiceActions actions, Func<bool> visible)
    {
        ConstructorInfo? constructor = typeof(SolisDesktopPresenter).GetConstructor(
            [typeof(SolisRuntime), typeof(SolisControlCenterView), typeof(DesktopStartupSettingsController),
             typeof(SolisDesktopServiceActions), typeof(Func<bool>)]);
        // Old implementation still runs real behavior so RED is not merely a missing API.
        return constructor is null ? new SolisDesktopPresenter(runtime, view, startup, actions)
            : (SolisDesktopPresenter)constructor.Invoke([runtime, view, startup, actions, visible]);
    }

    static void UiSetField(object target, string name, object? value) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);

    static void UiRefreshTest(Action<UiRefreshFixture> test)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { using var fixture = new UiRefreshFixture(); test(fixture); }
            catch (Exception exception) { failure = UnwrapInvocationException(exception); }
        });
        thread.IsBackground = true;
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        True(thread.Join(TimeSpan.FromSeconds(20)), "隐藏刷新测试超时");
        if (failure is not null) throw new InvalidOperationException("隐藏刷新测试失败", failure);
    }

    sealed class UiRefreshFixture : IDisposable
    {
        readonly string directory = Path.Combine(Path.GetTempPath(), $"SolisMonitor.UiRefresh-{Guid.NewGuid():N}");
        HttpClient? client;
        public bool Visible = true;
        public int Requests;
        public int Results;
        public int WeatherEdits;
        public SolisRuntime Runtime { get; }
        public SolisControlCenterView View { get; } = new();
        public DesktopStartupSettingsController Startup { get; } = new(() => false, _ => { }, () => false, _ => { });
        public SolisDesktopServiceActions Actions { get; }
        public SolisDesktopPresenter Presenter { get; }

        public UiRefreshFixture()
        {
            // Existing view styles share a static Freezable across STA tests. Freeze
            // it on its owning thread; this is fixture setup, not a product fix.
            var navigationBrush = (System.Windows.Media.Brush)typeof(SolisControlCenterView)
                .GetField("SelectedNavigationBrush", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            if (navigationBrush.CanFreeze) navigationBrush.Freeze();
            Directory.CreateDirectory(Path.Combine(directory, "codex"));
            Runtime = new SolisRuntime(string.Empty, directory, Path.Combine(directory, "codex"), "127.0.0.1", 54321);
            Actions = new SolisDesktopServiceActions(() => Runtime.Diagnostics, () => "test",
                () => DateTimeOffset.Now, () => Runtime.CodexSessionsRoot, _ => { }, _ => { },
                () => WeatherEdits++, () => { }, () => { },
                showResult: (_, _, _) => Results++);
            Presenter = UiCreatePresenter(Runtime, View, Startup, Actions, () => Visible);
        }

        public void SetSentinels()
        {
            ((TextBlock)View.FindName("DeviceStateText")).Text = "sentinel";
            ((TextBlock)View.FindName("StartupSummaryText")).Text = "sentinel";
            ((TextBlock)View.ServiceView.FindName("ApiStatus")).Text = "sentinel";
        }

        public void AssertSentinels()
        {
            Equal("sentinel", GetNamedText(View, "DeviceStateText"), "隐藏时设备页仍被渲染");
            Equal("sentinel", GetNamedText(View, "StartupSummaryText"), "隐藏时启动页仍被渲染");
            Equal("sentinel", GetNamedText(View.ServiceView, "ApiStatus"), "隐藏时服务页仍被渲染");
        }

        public void SetDiscovery(DeviceDiscoveryState state)
        {
            var discovery = GetPrivateField<DeviceDiscoveryService>(typeof(SolisRuntime), Runtime, "_deviceDiscoveryService");
            UiSetField(discovery, "_current", state);
        }

        public void Pair()
        {
            GetPrivateField<DeviceTokenStore>(typeof(SolisRuntime), Runtime, "_deviceTokenStore")
                .MarkPaired("test-screen", "127.0.0.1");
            SetDiscovery(new DeviceDiscoveryState(new DiscoveredDevice("test-screen", "test", "127.0.0.1", -40, true), false, null));
        }

        public void InstallControl(Func<HttpRequestMessage, Task<HttpResponseMessage>> send)
        {
            GetPrivateField<DeviceControlClient>(typeof(SolisRuntime), Runtime, "_deviceControlClient").Dispose();
            client = new HttpClient(new UiRefreshHandler(request => { Requests++; return send(request); }));
            var token = GetPrivateField<DeviceTokenStore>(typeof(SolisRuntime), Runtime, "_deviceTokenStore");
            UiSetField(Runtime, "_deviceControlClient", new DeviceControlClient(token, client));
        }

        public T Get<T>(string name) => GetPrivateField<T>(typeof(SolisDesktopPresenter), Presenter, name);
        public T? GetNullable<T>(string name) where T : class =>
            (T?)typeof(SolisDesktopPresenter).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Presenter);
        public Task InvokeTask(string name, params object[] args) =>
            (Task)typeof(SolisDesktopPresenter).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Presenter, args)!;
        public void PumpUntil(Func<bool> done)
        {
            var timeout = Stopwatch.StartNew();
            while (!done() && timeout.Elapsed < TimeSpan.FromSeconds(5))
            {
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
                Thread.Sleep(1);
            }
            True(done(), "异步操作没有在测试期限内完成");
        }
        public void Dispose()
        {
            Presenter.Dispose();
            Runtime.Dispose();
            client?.Dispose();
            Directory.Delete(directory, true);
        }
    }

    sealed class UiRefreshHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request);
    }
}
