#nullable enable

using System;
using System.Globalization;
using System.Text;
using LibreHardwareMonitor.Solis.Codex;
using LibreHardwareMonitor.Solis.DeviceApi;
using LibreHardwareMonitor.Solis.DeviceControl;
using LibreHardwareMonitor.Solis.Weather;

namespace LibreHardwareMonitor.Solis.Diagnostics;

public enum DiagnosticCheckState
{
    Checking,
    Normal,
    Fault
}

public enum DiagnosticSource
{
    None,
    DeviceApi,
    Device,
    Codex,
    Weather
}

public sealed record DiagnosticCheck(
    DiagnosticCheckState State,
    string Status,
    string? ErrorCategory,
    DateTimeOffset? LastNormalAt)
{
    public static DiagnosticCheck Initial(string status) =>
        new(DiagnosticCheckState.Checking, status, null, null);
}

public sealed record SolisDiagnosticsSnapshot(
    DiagnosticCheck DeviceApi,
    DiagnosticCheck Device,
    DiagnosticCheck Codex,
    DiagnosticCheck Weather,
    DiagnosticSource CurrentFault,
    string Summary,
    DateTimeOffset UpdatedAt)
{
    public static SolisDiagnosticsSnapshot Initial { get; } = new(
        DiagnosticCheck.Initial("正在启动"),
        DiagnosticCheck.Initial("正在扫描"),
        DiagnosticCheck.Initial("等待采集"),
        DiagnosticCheck.Initial("等待采集"),
        DiagnosticSource.None,
        "正在完成自动检查",
        DateTimeOffset.MinValue);
}

public sealed class SolisDiagnosticsMonitor
{
    private readonly object _sync = new();
    private DiagnosticCheck _api = SolisDiagnosticsSnapshot.Initial.DeviceApi;
    private DiagnosticCheck _device = SolisDiagnosticsSnapshot.Initial.Device;
    private DiagnosticCheck _codex = SolisDiagnosticsSnapshot.Initial.Codex;
    private DiagnosticCheck _weather = SolisDiagnosticsSnapshot.Initial.Weather;
    private DateTimeOffset _updatedAt = DateTimeOffset.MinValue;

    public SolisDiagnosticsSnapshot Current
    {
        get
        {
            lock (_sync)
                return CreateSnapshot();
        }
    }

    public void ObserveDeviceApi(
        bool running,
        bool metricsFresh,
        string? errorCategory,
        DateTimeOffset now)
    {
        lock (_sync)
        {
            _api = running && metricsFresh
                ? Normal(_api, "运行正常", now)
                : Fault(
                    _api,
                    running ? "指标快照未更新" : "设备 API 未运行",
                    errorCategory ?? (running ? "MetricsStale" : "ApiNotRunning"));
            _updatedAt = now;
        }
    }

    public void ObserveDevice(
        DeviceDiscoveryState discovery,
        DeviceAuthorizationState authorization,
        DateTimeOffset now)
    {
        if (discovery is null)
            throw new ArgumentNullException(nameof(discovery));
        if (authorization is null)
            throw new ArgumentNullException(nameof(authorization));

        lock (_sync)
        {
            bool tokenMismatch =
                !authorization.IsAuthorized &&
                authorization.ObservedAt != DateTimeOffset.MinValue &&
                discovery.Device is not null &&
                string.Equals(
                    discovery.Device.IpAddress,
                    authorization.RemoteAddress,
                    StringComparison.OrdinalIgnoreCase);

            if (tokenMismatch)
            {
                _device = Fault(_device, "设备令牌不匹配", "TokenMismatch");
            }
            else if (discovery.Device is DiscoveredDevice device)
            {
                _device = Normal(_device, $"已连接 {device.HostName}", now);
            }
            else if (discovery.IsScanning ||
                     discovery.ErrorCategory is null or "NotScanned")
            {
                _device = Checking(_device, "正在扫描");
            }
            else
            {
                _device = Fault(
                    _device,
                    DeviceFailureMessage(discovery.ErrorCategory),
                    discovery.ErrorCategory);
            }

            _updatedAt = now;
        }
    }

    public void ObserveCodex(CodexMetricsReading reading, DateTimeOffset now)
    {
        if (reading is null)
            throw new ArgumentNullException(nameof(reading));

        lock (_sync)
        {
            _codex = string.IsNullOrWhiteSpace(reading.ErrorCategory)
                ? Normal(_codex, reading.Online ? "采集活跃" : "采集正常 · 当前不活跃", now)
                : Fault(
                    _codex,
                    CodexFailureMessage(reading.ErrorCategory),
                    reading.ErrorCategory);
            _updatedAt = now;
        }
    }

    public void ObserveWeather(WeatherMetricsReading reading, DateTimeOffset now)
    {
        if (reading is null)
            throw new ArgumentNullException(nameof(reading));

        lock (_sync)
        {
            if (!string.IsNullOrWhiteSpace(reading.ErrorCategory) &&
                reading.ErrorCategory != "NotSampled")
            {
                _weather = Fault(
                    _weather,
                    WeatherFailureMessage(reading.ErrorCategory),
                    reading.ErrorCategory);
            }
            else if (reading.Available)
            {
                _weather = Normal(_weather, "天气 API 正常", now);
            }
            else
            {
                _weather = Checking(_weather, "等待天气数据");
            }

            _updatedAt = now;
        }
    }

    private SolisDiagnosticsSnapshot CreateSnapshot()
    {
        (DiagnosticSource source, DiagnosticCheck? check) = FirstFault();
        bool checking =
            _api.State == DiagnosticCheckState.Checking ||
            _device.State == DiagnosticCheckState.Checking ||
            _codex.State == DiagnosticCheckState.Checking ||
            _weather.State == DiagnosticCheckState.Checking;
        string summary = check is not null
            ? $"当前故障：{SourceName(source)} · {check.Status}"
            : checking
                ? "正在完成自动检查"
                : "未发现当前故障";
        return new SolisDiagnosticsSnapshot(
            _api,
            _device,
            _codex,
            _weather,
            source,
            summary,
            _updatedAt);
    }

    private (DiagnosticSource Source, DiagnosticCheck? Check) FirstFault()
    {
        if (_api.State == DiagnosticCheckState.Fault)
            return (DiagnosticSource.DeviceApi, _api);
        if (_device.State == DiagnosticCheckState.Fault)
            return (DiagnosticSource.Device, _device);
        if (_weather.State == DiagnosticCheckState.Fault)
            return (DiagnosticSource.Weather, _weather);
        if (_codex.State == DiagnosticCheckState.Fault)
            return (DiagnosticSource.Codex, _codex);
        return (DiagnosticSource.None, null);
    }

    private static DiagnosticCheck Normal(
        DiagnosticCheck previous,
        string status,
        DateTimeOffset now) =>
        new(DiagnosticCheckState.Normal, status, null, now);

    private static DiagnosticCheck Fault(
        DiagnosticCheck previous,
        string status,
        string? errorCategory) =>
        new(DiagnosticCheckState.Fault, status, errorCategory, previous.LastNormalAt);

    private static DiagnosticCheck Checking(
        DiagnosticCheck previous,
        string status) =>
        new(DiagnosticCheckState.Checking, status, null, previous.LastNormalAt);

    private static string DeviceFailureMessage(string? category) => category switch
    {
        "MultipleDevices" => "发现多个副屏",
        "ScanFailed" => "局域网扫描失败",
        _ => "未发现副屏"
    };

    private static string CodexFailureMessage(string? category) => category switch
    {
        "SessionsNotFound" => "找不到 Codex 会话目录",
        "SessionMetadataInvalid" => "Codex 会话元数据异常",
        "TokenCountInvalidJson" => "Codex 计数数据格式异常",
        "TokenCountFieldsMissing" => "Codex 计数字段缺失",
        _ => "Codex 采集异常"
    };

    private static string WeatherFailureMessage(string? category) => category switch
    {
        "ApiKeyMissing" or "HttpStatus401" => "API Key 或认证失败",
        "HttpStatus402" or "HttpStatus403" or "HttpStatus429" => "权限或额度受限",
        "ApiHostInvalid" or "HttpStatus404" => "API Host 无效",
        "NetworkError" => "网络或 DNS 失败",
        "Timeout" => "天气请求超时",
        _ when category?.StartsWith("HttpStatus5", StringComparison.Ordinal) == true =>
            "天气服务暂时不可用",
        _ => "天气 API 异常"
    };

    private static string SourceName(DiagnosticSource source) => source switch
    {
        DiagnosticSource.DeviceApi => "PC 指标服务",
        DiagnosticSource.Device => "副屏连接",
        DiagnosticSource.Codex => "Codex 采集",
        DiagnosticSource.Weather => "天气 API",
        _ => "未知"
    };
}

public static class SolisDiagnosticsReport
{
    public static string Create(
        SolisDiagnosticsSnapshot snapshot,
        string applicationVersion,
        DateTimeOffset now)
    {
        if (snapshot is null)
            throw new ArgumentNullException(nameof(snapshot));

        var report = new StringBuilder();
        report.AppendLine("Solis Monitor 诊断信息");
        report.AppendLine($"生成时间：{now.ToLocalTime():yyyy-MM-dd HH:mm:ss zzz}");
        report.AppendLine($"程序版本：{applicationVersion}");
        report.AppendLine($"总体状态：{snapshot.Summary}");
        AppendCheck(report, "PC 指标服务", snapshot.DeviceApi);
        AppendCheck(report, "副屏连接", snapshot.Device);
        AppendCheck(report, "Codex 采集", snapshot.Codex);
        AppendCheck(report, "天气 API", snapshot.Weather);
        report.AppendLine("敏感信息：API Key、Wi-Fi 密码和完整设备令牌未包含");
        return report.ToString();
    }

    private static void AppendCheck(
        StringBuilder report,
        string name,
        DiagnosticCheck check)
    {
        string lastNormal = check.LastNormalAt.HasValue
            ? check.LastNormalAt.Value.ToLocalTime()
                .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
            : "--";
        string error = string.IsNullOrWhiteSpace(check.ErrorCategory)
            ? "--"
            : check.ErrorCategory!;
        report.AppendLine(
            $"{name}：{check.Status}；最近正常：{lastNormal}；错误类别：{error}");
    }
}
