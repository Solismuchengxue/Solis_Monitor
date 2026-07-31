#nullable enable

using System;
using System.Globalization;
using LibreHardwareMonitor.Solis.Weather;

namespace LibreHardwareMonitor.UI.WpfViews;

public sealed class SolisWeatherSettingsEditor
{
    private readonly QWeatherSettings _existing;
    private readonly Func<QWeatherSettings, WeatherMetricsReading> _testSettings;
    private readonly Action<QWeatherSettings, WeatherMetricsReading> _saveSettings;

    public SolisWeatherSettingsEditor(
        QWeatherSettings existing,
        Func<QWeatherSettings, WeatherMetricsReading> testSettings,
        Action<QWeatherSettings, WeatherMetricsReading> saveSettings)
    {
        _existing = existing ?? throw new ArgumentNullException(nameof(existing));
        _testSettings = testSettings ??
            throw new ArgumentNullException(nameof(testSettings));
        _saveSettings = saveSettings ??
            throw new ArgumentNullException(nameof(saveSettings));
    }

    public string FormatCoordinates()
    {
        if (!_existing.Longitude.HasValue || !_existing.Latitude.HasValue)
            return string.Empty;

        return string.Format(
            CultureInfo.InvariantCulture,
            "{0:R},{1:R}",
            _existing.Longitude.Value,
            _existing.Latitude.Value);
    }

    public bool TryCreateSettings(
        string apiHost,
        string apiKeyInput,
        string coordinates,
        out QWeatherSettings? settings,
        out string error)
    {
        settings = null;
        string host = apiHost.Trim();
        string apiKey = string.IsNullOrWhiteSpace(apiKeyInput)
            ? _existing.ApiKey
            : apiKeyInput.Trim();
        if (string.IsNullOrWhiteSpace(host) ||
            host.IndexOfAny(['/', '\\', '?', '#', '@']) >= 0 ||
            Uri.CheckHostName(host) != UriHostNameType.Dns)
        {
            error = "API Host 只填写专属域名，不要包含 https:// 或路径。";
            return false;
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            error = "请输入 API Key。";
            return false;
        }

        string[] coordinateParts = coordinates.Trim().Split(
            [',', '，'],
            StringSplitOptions.RemoveEmptyEntries);
        if (coordinateParts.Length != 2 ||
            !TryCoordinate(coordinateParts[0], -180, 180, out double longitude) ||
            !TryCoordinate(coordinateParts[1], -90, 90, out double latitude))
        {
            error = "经纬度格式或范围无效；请按“121.51,38.84”填写，经度在前。";
            return false;
        }

        settings = new QWeatherSettings(
            true,
            host,
            apiKey,
            string.Empty,
            null,
            longitude,
            latitude);
        error = string.Empty;
        return true;
    }

    public WeatherMetricsReading Test(QWeatherSettings settings) =>
        _testSettings(settings);

    public void Save(
        QWeatherSettings settings,
        WeatherMetricsReading reading) =>
        _saveSettings(settings, reading);

    public static string DescribeError(string? category) => category switch
    {
        "ApiKeyMissing" => "缺少 API Key",
        "ApiHostInvalid" => "API Host 无效",
        "CoordinatesInvalid" => "经纬度无效",
        "ApiRejected" => "API Key、Host 或权限被服务拒绝",
        "NetworkError" => "网络连接失败",
        "Timeout" => "请求超时",
        "InvalidJson" => "服务返回了无法解析的数据",
        _ when category?.StartsWith("HttpStatus", StringComparison.Ordinal) == true =>
            $"服务返回 HTTP {category.Substring("HttpStatus".Length)}",
        _ => category ?? "未知错误"
    };

    private static bool TryCoordinate(
        string value,
        double minimum,
        double maximum,
        out double result) =>
        double.TryParse(
            value.Trim(),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out result) &&
        !double.IsNaN(result) &&
        !double.IsInfinity(result) &&
        result >= minimum &&
        result <= maximum;
}
