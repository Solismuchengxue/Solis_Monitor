#nullable enable

using System;

namespace LibreHardwareMonitor.Solis.Weather;

public sealed record WeatherFailureNotification(string Message);

public sealed class WeatherFailureMonitor
{
    private readonly TimeSpan _transientFailureDelay;
    private FailureKind _currentKind;
    private DateTimeOffset? _failureSince;
    private bool _notified;

    public WeatherFailureMonitor(TimeSpan transientFailureDelay)
    {
        if (transientFailureDelay <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(transientFailureDelay));

        _transientFailureDelay = transientFailureDelay;
    }

    public WeatherFailureNotification? Observe(
        WeatherMetricsReading reading,
        DateTimeOffset now)
    {
        if (reading is null)
            throw new ArgumentNullException(nameof(reading));

        (FailureKind kind, string? message) = Classify(reading.ErrorCategory);
        if (kind == FailureKind.None)
        {
            Reset();
            return null;
        }

        if (kind != _currentKind)
        {
            _currentKind = kind;
            _failureSince = now;
            _notified = false;
        }

        if (_notified)
            return null;

        if (kind == FailureKind.Transient &&
            (!_failureSince.HasValue || now - _failureSince.Value < _transientFailureDelay))
        {
            return null;
        }

        _notified = true;
        return new WeatherFailureNotification(message!);
    }

    private static (FailureKind Kind, string? Message) Classify(string? category)
    {
        if (string.IsNullOrWhiteSpace(category))
            return (FailureKind.None, null);

        return category switch
        {
            "ApiKeyMissing" or "HttpStatus401" =>
                (FailureKind.Immediate,
                    "天气 API Key 无效或认证失败，请打开“天气”页检查配置。"),
            "HttpStatus402" or "HttpStatus403" or "HttpStatus429" =>
                (FailureKind.Immediate,
                    "天气 API 权限不足或额度受限，请检查和风天气项目。"),
            "ApiHostInvalid" or "HttpStatus404" =>
                (FailureKind.Immediate,
                    "天气 API Host 或接口地址无效，请打开“天气”页检查配置。"),
            "HttpStatus400" or "ApiRejected" =>
                (FailureKind.Immediate,
                    "天气 API 拒绝了请求，请检查 Host、API Key 和位置配置。"),
            "NetworkError" or "Timeout" =>
                (FailureKind.Transient,
                    "天气服务已连续 30 分钟无法联网，请检查 DNS 和网络连接。"),
            _ when category!.StartsWith("HttpStatus5", StringComparison.Ordinal) =>
                (FailureKind.Transient,
                    "天气服务已连续 30 分钟无法联网，请检查 DNS 和网络连接。"),
            _ => (FailureKind.None, null)
        };
    }

    private void Reset()
    {
        _currentKind = FailureKind.None;
        _failureSince = null;
        _notified = false;
    }

    private enum FailureKind
    {
        None,
        Immediate,
        Transient
    }
}
