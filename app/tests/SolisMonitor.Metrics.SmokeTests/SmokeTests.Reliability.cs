internal static partial class SmokeTests
{
static void BackgroundCollectionGuardIsolatesRecoverableFailure()
{
    BackgroundCollectionFailure? written = null;
    BackgroundCollectionFailure? observed = null;
    var guard = new BackgroundCollectionGuard((failure, _) => written = failure);

    bool completed = guard.Execute(
        BackgroundCollectionModule.Metrics,
        DateTimeOffset.Parse("2026-08-08T12:00:00Z"),
        () => throw new InvalidOperationException("secret-path"),
        failure => observed = failure);

    True(!completed, "普通采集异常不应被报告为成功");
    True(written is not null, "普通采集异常没有写入脱敏记录");
    Equal(written, observed, "日志和诊断收到的故障记录不一致");
    Equal(typeof(InvalidOperationException).FullName!, written!.ExceptionType,
        "故障记录没有保存异常类型");
}

static void BackgroundCollectionGuardContinuesAfterFailure()
{
    var guard = new BackgroundCollectionGuard((_, _) => { });
    int successfulRuns = 0;

    guard.Execute(BackgroundCollectionModule.Metrics, DateTimeOffset.UtcNow,
        () => throw new InvalidOperationException(), _ => { });
    bool completed = guard.Execute(BackgroundCollectionModule.Metrics,
        DateTimeOffset.UtcNow.AddSeconds(1), () => successfulRuns++, _ => { });

    True(completed, "故障后的下一次采集没有恢复执行");
    Equal(1, successfulRuns, "恢复周期没有执行一次完整采集");
}

static void BackgroundCollectionGuardKeepsLastCompleteMetricsSnapshot()
{
    var store = new MetricsSnapshotStore();
    store.Publish(
        new NetworkThroughputReading(true, 12.5, 3.25, "wifi", "Wi-Fi", null),
        new CodexMetricsReading(
            true, "Solis_Monitor", 25, 12.5, 128, 4, null, null, null),
        DateTimeOffset.FromUnixTimeSeconds(100));
    SolisMetricsSnapshot initial = store.Current;
    var guard = new BackgroundCollectionGuard((_, _) => { });

    guard.Execute(
        BackgroundCollectionModule.Metrics,
        DateTimeOffset.FromUnixTimeSeconds(101),
        () => throw new InvalidOperationException("second cycle failed"),
        _ => { });

    SolisMetricsSnapshot afterFailure = store.Current;
    Equal(initial.Sequence, afterFailure.Sequence,
        "失败指标周期改变了快照序号");
    Equal(initial.Network.DownloadMbps.Value, afterFailure.Network.DownloadMbps.Value,
        "失败指标周期改变了最后完整网络值");
    Equal(initial.Codex.LastActiveTask, afterFailure.Codex.LastActiveTask,
        "失败指标周期改变了最后完整 Codex 值");
}

static void ReplacedWeatherCollectorFailureDoesNotOverrideHealthyDiagnostics()
{
    string root = Path.Combine(Path.GetTempPath(), $"SolisMonitor.WeatherRace-{Guid.NewGuid():N}");
    var handler = new BlockingFailureHttpMessageHandler();
    HttpClient? attemptedClient = null;
    HttpClient? replacementClient = null;
    QWeatherMetricsCollector? attemptedCollector = null;
    QWeatherMetricsCollector? replacementCollector = null;
    SolisRuntime? runtime = null;
    Task? update = null;
    try
    {
        Directory.CreateDirectory(root);
        attemptedClient = new HttpClient(handler);
        replacementClient = new HttpClient(new ThrowingHttpMessageHandler());
        attemptedCollector = CreateWeatherCollector(attemptedClient);
        replacementCollector = CreateWeatherCollector(replacementClient);
        runtime = CreateReliabilityRuntime(root);
        QWeatherMetricsCollector original = ReplaceWeatherCollector(runtime, attemptedCollector);
        original.Dispose();
        SolisDiagnosticsMonitor diagnostics = GetRuntimeDiagnostics(runtime);
        diagnostics.ObserveWeather(
            HealthyWeatherReading(), DateTimeOffset.Parse("2026-08-08T12:00:00Z"));

        update = Task.Run(() => InvokeWeatherUpdate(runtime));
        True(handler.Entered.Wait(TimeSpan.FromSeconds(5)), "旧天气采集没有进入可控失败点");
        ReplaceWeatherCollector(runtime, replacementCollector);
        diagnostics.ObserveWeather(
            HealthyWeatherReading(), DateTimeOffset.Parse("2026-08-08T12:01:00Z"));
        handler.Release.Set();
        True(update.Wait(TimeSpan.FromSeconds(5)), "旧天气采集失败没有结束");

        Equal(DiagnosticCheckState.Normal, runtime.Diagnostics.Weather.State,
            "已替换 collector 的失败覆盖了新 collector 的健康诊断");
    }
    finally
    {
        handler.Release.Set();
        update?.Wait(TimeSpan.FromSeconds(5));
        runtime?.Dispose();
        attemptedCollector?.Dispose();
        replacementCollector?.Dispose();
        attemptedClient?.Dispose();
        replacementClient?.Dispose();
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}

static void ClosingWeatherCollectorFailureDoesNotOverrideHealthyDiagnostics()
{
    string root = Path.Combine(Path.GetTempPath(), $"SolisMonitor.WeatherClosing-{Guid.NewGuid():N}");
    var handler = new BlockingFailureHttpMessageHandler();
    HttpClient? attemptedClient = null;
    QWeatherMetricsCollector? attemptedCollector = null;
    SolisRuntime? runtime = null;
    Task? update = null;
    try
    {
        Directory.CreateDirectory(root);
        attemptedClient = new HttpClient(handler);
        attemptedCollector = CreateWeatherCollector(attemptedClient);
        runtime = CreateReliabilityRuntime(root);
        QWeatherMetricsCollector original = ReplaceWeatherCollector(runtime, attemptedCollector);
        original.Dispose();
        SolisDiagnosticsMonitor diagnostics = GetRuntimeDiagnostics(runtime);
        diagnostics.ObserveWeather(
            HealthyWeatherReading(), DateTimeOffset.Parse("2026-08-08T12:00:00Z"));

        update = Task.Run(() => InvokeWeatherUpdate(runtime));
        True(handler.Entered.Wait(TimeSpan.FromSeconds(5)), "关闭中的天气采集没有进入可控失败点");
        runtime.Dispose();
        handler.Release.Set();
        True(update.Wait(TimeSpan.FromSeconds(5)), "关闭中的天气采集失败没有结束");

        Equal(DiagnosticCheckState.Normal, runtime.Diagnostics.Weather.State,
            "关闭中的 collector 失败更新了天气诊断");
    }
    finally
    {
        handler.Release.Set();
        update?.Wait(TimeSpan.FromSeconds(5));
        runtime?.Dispose();
        attemptedCollector?.Dispose();
        attemptedClient?.Dispose();
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}

static void CurrentWeatherCollectorFailureUpdatesDiagnostics()
{
    string root = Path.Combine(Path.GetTempPath(), $"SolisMonitor.WeatherFailure-{Guid.NewGuid():N}");
    HttpClient? attemptedClient = null;
    QWeatherMetricsCollector? attemptedCollector = null;
    SolisRuntime? runtime = null;
    try
    {
        Directory.CreateDirectory(root);
        attemptedClient = new HttpClient(new ThrowingHttpMessageHandler());
        attemptedCollector = CreateWeatherCollector(attemptedClient);
        runtime = CreateReliabilityRuntime(root);
        QWeatherMetricsCollector original = ReplaceWeatherCollector(runtime, attemptedCollector);
        original.Dispose();
        GetRuntimeDiagnostics(runtime).ObserveWeather(
            HealthyWeatherReading(), DateTimeOffset.Parse("2026-08-08T12:00:00Z"));

        InvokeWeatherUpdate(runtime);

        Equal(DiagnosticCheckState.Fault, runtime.Diagnostics.Weather.State,
            "当前 collector 的真实失败没有更新天气诊断");
        Equal("BackgroundCollectionError", runtime.Diagnostics.Weather.ErrorCategory,
            "当前 collector 的真实失败类别错误");
    }
    finally
    {
        runtime?.Dispose();
        attemptedCollector?.Dispose();
        attemptedClient?.Dispose();
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}

static void BackgroundCollectionGuardPropagatesFatalFailures()
{
    var guard = new BackgroundCollectionGuard((_, _) => { });
    foreach (Exception fatal in new Exception[]
             { new OutOfMemoryException(), new AccessViolationException() })
    {
        bool propagated = false;
        try
        {
            guard.Execute(BackgroundCollectionModule.Weather,
                DateTimeOffset.UtcNow, () => throw fatal, _ => { });
        }
        catch (Exception exception)
        {
            propagated = ReferenceEquals(fatal, exception);
        }

        True(propagated, $"严重异常被错误隔离：{fatal.GetType().Name}");
    }
}

static void BackgroundCollectionGuardSurvivesLogFailure()
{
    int diagnostics = 0;
    var guard = new BackgroundCollectionGuard((_, _) => throw new IOException());

    bool completed = guard.Execute(BackgroundCollectionModule.Weather,
        DateTimeOffset.UtcNow,
        () => throw new InvalidOperationException(),
        _ => diagnostics++);

    True(!completed, "采集异常不应返回成功");
    Equal(1, diagnostics, "日志失败阻断了诊断故障回调");
}

static void RuntimeErrorLogRateLimitsDuplicateFailures()
{
    string root = Path.Combine(Path.GetTempPath(), $"SolisMonitor.Log-{Guid.NewGuid():N}");
    try
    {
        var log = new RuntimeErrorLog(root, maximumFileBytes: 512,
            minimumInterval: TimeSpan.FromMinutes(5));
        var failure = new BackgroundCollectionFailure(
            BackgroundCollectionModule.Metrics,
            typeof(InvalidOperationException).FullName!,
            unchecked((int)0x80131509));
        DateTimeOffset now = DateTimeOffset.Parse("2026-08-08T12:00:00Z");

        log.TryWrite(failure, now);
        log.TryWrite(failure, now.AddMinutes(4));
        log.TryWrite(failure, now.AddMinutes(5));

        Equal(2, File.ReadAllLines(log.LogPath).Length,
            "五分钟内重复故障没有被限流");
    }
    finally
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}

static void RuntimeErrorLogWritesDifferentFailureTypesWithoutThrottling()
{
    string root = Path.Combine(Path.GetTempPath(), $"SolisMonitor.Log-{Guid.NewGuid():N}");
    try
    {
        var log = new RuntimeErrorLog(root, minimumInterval: TimeSpan.FromMinutes(5));
        DateTimeOffset now = DateTimeOffset.Parse("2026-08-08T12:00:00Z");

        log.TryWrite(new BackgroundCollectionFailure(
            BackgroundCollectionModule.Metrics,
            typeof(IOException).FullName!,
            unchecked((int)0x80131620)), now);
        log.TryWrite(new BackgroundCollectionFailure(
            BackgroundCollectionModule.Metrics,
            typeof(InvalidOperationException).FullName!,
            unchecked((int)0x80131509)), now);

        Equal(2, File.ReadAllLines(log.LogPath).Length,
            "同模块的不同异常类型被错误限流");
    }
    finally
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}

static void RuntimeErrorLogWritesUtcTimestamp()
{
    string root = Path.Combine(Path.GetTempPath(), $"SolisMonitor.Log-{Guid.NewGuid():N}");
    try
    {
        var log = new RuntimeErrorLog(root);
        log.TryWrite(new BackgroundCollectionFailure(
            BackgroundCollectionModule.Metrics,
            typeof(InvalidOperationException).FullName!,
            unchecked((int)0x80131509)),
            DateTimeOffset.Parse("2026-08-08T20:00:00+08:00"));

        True(File.ReadAllText(log.LogPath).StartsWith("2026-08-08T12:00:00.0000000+00:00 ",
            StringComparison.Ordinal), "运行时日志时间没有规范化为 UTC");
    }
    finally
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}

static void RuntimeErrorLogRotatesBoundedRedactedRecords()
{
    string root = Path.Combine(Path.GetTempPath(), $"SolisMonitor.Log-{Guid.NewGuid():N}");
    try
    {
        var log = new RuntimeErrorLog(root, maximumFileBytes: 512,
            minimumInterval: TimeSpan.FromMinutes(5));
        var failure = new BackgroundCollectionFailure(
            BackgroundCollectionModule.Weather,
            "secret-path\r\n at unsafe-stack-frame",
            unchecked((int)0x80131509));
        DateTimeOffset now = DateTimeOffset.Parse("2026-08-08T12:00:00Z");

        for (int index = 0; index < 20 && !File.Exists(log.LogPath + ".1"); index++)
        {
            log.TryWrite(failure, now.AddMinutes(index * 6));
        }

        True(File.Exists(log.LogPath), "当前运行时日志不存在");
        True(File.Exists(log.LogPath + ".1"), "运行时日志没有轮转唯一备份");
        True(new FileInfo(log.LogPath).Length <= 512, "当前日志超过测试硬上限");
        True(new FileInfo(log.LogPath + ".1").Length <= 512, "备份日志超过测试硬上限");
        string combined = File.ReadAllText(log.LogPath) + File.ReadAllText(log.LogPath + ".1");
        True(!combined.Contains("secret-path", StringComparison.Ordinal), "日志泄露异常消息");
        True(!combined.Contains(" at ", StringComparison.Ordinal), "日志泄露异常堆栈");
    }
    finally
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}

static void RuntimeErrorLogSurvivesUnavailableStorage()
{
    string root = Path.Combine(Path.GetTempPath(), $"SolisMonitor.Log-{Guid.NewGuid():N}");
    try
    {
        File.WriteAllText(root, "not-a-directory");
        var log = new RuntimeErrorLog(root);
        var failure = new BackgroundCollectionFailure(
            BackgroundCollectionModule.Metrics,
            typeof(InvalidOperationException).FullName!,
            unchecked((int)0x80131509));

        log.TryWrite(failure, DateTimeOffset.Parse("2026-08-08T12:00:00Z"));
    }
    finally
    {
        if (File.Exists(root)) File.Delete(root);
    }
}

static void RuntimeErrorLogCapsInjectedMaximumFileBytes()
{
    string root = Path.Combine(Path.GetTempPath(), $"SolisMonitor.Log-{Guid.NewGuid():N}");
    try
    {
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "runtime-errors.log");
        File.WriteAllBytes(path, new byte[524288]);
        var log = new RuntimeErrorLog(root, maximumFileBytes: 600000);

        log.TryWrite(new BackgroundCollectionFailure(
            BackgroundCollectionModule.Metrics,
            typeof(InvalidOperationException).FullName!,
            unchecked((int)0x80131509)), DateTimeOffset.Parse("2026-08-08T12:00:00Z"));

        True(new FileInfo(log.LogPath).Length <= 524288,
            "注入的大于硬上限的值允许当前日志超限");
        True(new FileInfo(log.LogPath + ".1").Length <= 524288,
            "注入的大于硬上限的值允许备份日志超限");
    }
    finally
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}

static void RuntimeErrorLogDiscardsPrebuiltOversizedCurrentFile()
{
    string root = Path.Combine(Path.GetTempPath(), $"SolisMonitor.Log-{Guid.NewGuid():N}");
    try
    {
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "runtime-errors.log");
        File.WriteAllBytes(path, new byte[524289]);
        var log = new RuntimeErrorLog(root, maximumFileBytes: 512);

        log.TryWrite(new BackgroundCollectionFailure(
            BackgroundCollectionModule.Metrics,
            typeof(InvalidOperationException).FullName!,
            unchecked((int)0x80131509)), DateTimeOffset.Parse("2026-08-08T12:00:00Z"));

        True(new FileInfo(log.LogPath).Length <= 512, "预建超大当前日志未被限制");
        True(!File.Exists(log.LogPath + ".1") || new FileInfo(log.LogPath + ".1").Length <= 512,
            "预建超大当前日志被保留为超限备份");
    }
    finally
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}

static SolisRuntime CreateReliabilityRuntime(string root)
{
    string codexRoot = Path.Combine(root, "codex");
    Directory.CreateDirectory(codexRoot);
    return new SolisRuntime(string.Empty, root, codexRoot, "127.0.0.1", 50123);
}

static QWeatherMetricsCollector CreateWeatherCollector(HttpClient client) =>
    new(
        new QWeatherSettings(
            true,
            "md3h2ew6qe.re.qweatherapi.com",
            "test-secret",
            "大连",
            "101070201"),
        client);

static QWeatherMetricsCollector ReplaceWeatherCollector(
    SolisRuntime runtime,
    QWeatherMetricsCollector replacement)
{
    System.Reflection.FieldInfo field = typeof(SolisRuntime).GetField(
        "_weatherMetricsCollector",
        System.Reflection.BindingFlags.Instance |
        System.Reflection.BindingFlags.NonPublic)!;
    var previous = (QWeatherMetricsCollector)field.GetValue(runtime)!;
    field.SetValue(runtime, replacement);
    return previous;
}

static SolisDiagnosticsMonitor GetRuntimeDiagnostics(SolisRuntime runtime) =>
    (SolisDiagnosticsMonitor)typeof(SolisRuntime).GetField(
        "_diagnosticsMonitor",
        System.Reflection.BindingFlags.Instance |
        System.Reflection.BindingFlags.NonPublic)!.GetValue(runtime)!;

static void InvokeWeatherUpdate(SolisRuntime runtime) =>
    typeof(SolisRuntime).GetMethod(
        "UpdateWeather",
        System.Reflection.BindingFlags.Instance |
        System.Reflection.BindingFlags.NonPublic)!.Invoke(runtime, new object?[] { null });

static WeatherMetricsReading HealthyWeatherReading() =>
    new(true, "大连", "晴", 25, 30, null);

private sealed class BlockingFailureHttpMessageHandler : HttpMessageHandler
{
    public ManualResetEventSlim Entered { get; } = new(false);
    public ManualResetEventSlim Release { get; } = new(false);

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Entered.Set();
        Release.Wait();
        throw new InvalidOperationException("controlled weather failure");
    }

    protected override void Dispose(bool disposing)
    {
    }
}

private sealed class ThrowingHttpMessageHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException("controlled weather failure");
}
}
