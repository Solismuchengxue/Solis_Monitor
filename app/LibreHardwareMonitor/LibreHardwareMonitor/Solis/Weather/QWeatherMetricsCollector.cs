#nullable enable

using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LibreHardwareMonitor.Solis.Weather;

public sealed class QWeatherMetricsCollector : IDisposable
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan MaximumCacheAge = TimeSpan.FromHours(3);
    private static readonly TimeSpan[] RetryIntervals =
    [
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromMinutes(30)
    ];

    private readonly object _sync = new();
    private readonly QWeatherSettings _settings;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private string? _locationId;
    private string? _resolvedLocationName;
    private WeatherMetricsReading _lastGood = WeatherMetricsReading.Empty("NotSampled");
    private DateTimeOffset? _lastSuccessAt;
    private DateTimeOffset _nextAttemptAt = DateTimeOffset.MinValue;
    private int _consecutiveFailures;
    private int _disposeState;
    private string? _lastError;

    public QWeatherMetricsCollector(QWeatherSettings settings, HttpClient? httpClient = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _ownsHttpClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        if (settings.Longitude.HasValue && settings.Latitude.HasValue)
        {
            _locationId = null;
            _resolvedLocationName = null;
        }
        else
        {
            _locationId = settings.LocationId;
            _resolvedLocationName = settings.Location;
        }
    }

    public WeatherMetricsReading Read(DateTimeOffset now)
    {
        string? configurationError = ValidateConfiguration();
        if (configurationError is not null)
            return WeatherMetricsReading.Empty(configurationError);

        lock (_sync)
        {
            if (now >= _nextAttemptAt)
                Refresh(now);

            if (_lastSuccessAt.HasValue && now - _lastSuccessAt.Value <= MaximumCacheAge)
                return _lastGood with { ErrorCategory = _lastError };

            return WeatherMetricsReading.Empty(_lastError ?? "NotSampled");
        }
    }

    private void Refresh(DateTimeOffset now)
    {
        try
        {
            string queryLocation = GetQueryLocation();
            (string description, string? windDirection, string? windScale, int? iconIndex) =
                ReadCurrentConditions(queryLocation);
            WeatherMetricsReading reading = ReadForecast(
                queryLocation,
                description,
                windDirection,
                windScale,
                iconIndex);
            _lastGood = reading;
            _lastSuccessAt = now;
            _lastError = null;
            _consecutiveFailures = 0;
            _nextAttemptAt = now + RefreshInterval;
        }
        catch (QWeatherException exception)
        {
            RecordFailure(now, exception.Category);
        }
        catch (HttpRequestException)
        {
            RecordFailure(now, "NetworkError");
        }
        catch (TaskCanceledException)
        {
            RecordFailure(now, "Timeout");
        }
        catch (JsonException)
        {
            RecordFailure(now, "InvalidJson");
        }
    }

    private void RecordFailure(DateTimeOffset now, string category)
    {
        _lastError = category;
        int retryIndex = Math.Min(_consecutiveFailures, RetryIntervals.Length - 1);
        _nextAttemptAt = now + RetryIntervals[retryIndex];
        _consecutiveFailures++;
    }

    private (string Id, string Name) ResolveLocation()
    {
        bool hasCoordinates = _settings.Longitude.HasValue && _settings.Latitude.HasValue;
        string lookupLocation = hasCoordinates
            ? FormatGeoCoordinates(_settings.Longitude!.Value, _settings.Latitude!.Value)
            : _settings.Location;
        using JsonDocument document = GetJson(
            $"/geo/v2/city/lookup?location={Uri.EscapeDataString(lookupLocation)}&number=1&lang=zh");
        JsonElement locations = RequiredArray(document.RootElement, "location", "LocationMissing");
        JsonElement? selected = hasCoordinates
            ? locations.EnumerateArray().Cast<JsonElement?>().FirstOrDefault()
            : locations.EnumerateArray()
                .Cast<JsonElement?>()
                .FirstOrDefault(item => string.Equals(
                    OptionalString(item!.Value, "name"),
                    _settings.Location,
                    StringComparison.Ordinal)) ??
              locations.EnumerateArray().Cast<JsonElement?>().FirstOrDefault();
        string? id = selected.HasValue ? OptionalString(selected.Value, "id") : null;
        string? name = selected.HasValue ? FormatDisplayLocation(selected.Value) : null;
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
            throw new QWeatherException("LocationMissing");

        return (id!, name!);
    }

    private static string? FormatDisplayLocation(JsonElement location)
    {
        string? name = OptionalString(location, "name");
        if (string.IsNullOrWhiteSpace(name))
            return null;

        string? country = OptionalString(location, "country");
        string? administrative1 = OptionalString(location, "adm1");
        string? administrative2 = OptionalString(location, "adm2");
        if (!string.Equals(country, "中国", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(administrative1) ||
            string.IsNullOrWhiteSpace(administrative2))
        {
            return name;
        }

        string province = TrimAdministrativeSuffix(
            administrative1!,
            "特别行政区",
            "维吾尔自治区",
            "壮族自治区",
            "回族自治区",
            "自治区",
            "省",
            "市");
        string city = TrimAdministrativeSuffix(
            administrative2!,
            "自治州",
            "地区",
            "盟",
            "市");
        return string.Join(
            "·",
            new[] { province, city }
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .Distinct(StringComparer.Ordinal));
    }

    private static string TrimAdministrativeSuffix(string value, params string[] suffixes)
    {
        foreach (string suffix in suffixes)
        {
            if (value.EndsWith(suffix, StringComparison.Ordinal) &&
                value.Length > suffix.Length)
            {
                return value.Substring(0, value.Length - suffix.Length);
            }
        }

        return value;
    }

    private string GetQueryLocation()
    {
        if (string.IsNullOrWhiteSpace(_locationId))
        {
            (string id, string name) = ResolveLocation();
            _locationId = id;
            _resolvedLocationName = name;
        }

        return _locationId!;
    }

    private static string FormatGeoCoordinates(double longitude, double latitude) =>
        string.Format(
            CultureInfo.InvariantCulture,
            "{0:0.##},{1:0.##}",
            longitude,
            latitude);

    private (string Description, string? WindDirection, string? WindScale, int? IconIndex)
        ReadCurrentConditions(
        string queryLocation)
    {
        using JsonDocument document = GetJson(
            $"/v7/weather/now?location={Uri.EscapeDataString(queryLocation)}&lang=zh");
        if (!document.RootElement.TryGetProperty("now", out JsonElement current) ||
            current.ValueKind != JsonValueKind.Object)
            throw new QWeatherException("CurrentWeatherMissing");

        string? description = OptionalString(current, "text");
        if (string.IsNullOrWhiteSpace(description))
            throw new QWeatherException("CurrentWeatherFieldsMissing");

        return (
            description!,
            OptionalString(current, "windDir"),
            OptionalString(current, "windScale"),
            MapWeatherIcon(OptionalString(current, "icon")));
    }

    private WeatherMetricsReading ReadForecast(
        string queryLocation,
        string description,
        string? windDirection,
        string? windScale,
        int? iconIndex)
    {
        using JsonDocument document = GetJson(
            $"/v7/weather/3d?location={Uri.EscapeDataString(queryLocation)}&lang=zh");
        JsonElement daily = RequiredArray(document.RootElement, "daily", "ForecastMissing");
        JsonElement? today = daily.EnumerateArray().Cast<JsonElement?>().FirstOrDefault();
        if (!today.HasValue)
            throw new QWeatherException("ForecastMissing");

        double? low = OptionalDouble(today.Value, "tempMin");
        double? high = OptionalDouble(today.Value, "tempMax");
        if (!low.HasValue || !high.HasValue)
            throw new QWeatherException("ForecastFieldsMissing");

        return new WeatherMetricsReading(
            true,
            _resolvedLocationName ?? _settings.Location,
            description,
            low,
            high,
            null,
            windDirection,
            windScale,
            iconIndex);
    }

    private static int? MapWeatherIcon(string? iconCode)
    {
        if (!int.TryParse(iconCode, NumberStyles.None, CultureInfo.InvariantCulture, out int code))
            return null;

        return code switch
        {
            100 => 0,
            >= 101 and <= 103 => 1,
            104 => 2,
            150 => 18,
            >= 151 and <= 153 => 19,
            >= 300 and <= 301 => 3,
            302 => 4,
            >= 303 and <= 304 => 5,
            313 or >= 404 and <= 406 => 6,
            305 or 309 or 314 => 7,
            306 or 315 or 399 => 8,
            307 or 316 => 9,
            310 or 317 => 10,
            311 => 11,
            308 or 312 or 318 => 12,
            >= 350 and <= 351 => 20,
            407 => 13,
            400 or 408 => 14,
            401 or 409 or 499 => 15,
            402 or 410 => 16,
            403 => 17,
            456 => 6,
            457 => 13,
            500 or 501 or 509 or 510 or 514 or 515 => 21,
            502 or >= 511 and <= 513 => 22,
            503 or 504 or 507 or 508 => 23,
            900 => 24,
            901 => 25,
            999 => 26,
            _ => null
        };
    }

    private JsonDocument GetJson(string pathAndQuery)
    {
        var uri = new Uri($"https://{_settings.ApiHost}{pathAndQuery}", UriKind.Absolute);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.TryAddWithoutValidation("X-QW-Api-Key", _settings.ApiKey);
        using HttpResponseMessage response = _httpClient.SendAsync(request).GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
            throw new QWeatherException($"HttpStatus{(int)response.StatusCode}");

        string json = ReadJsonContent(response);
        JsonDocument document = JsonDocument.Parse(json);
        if (!string.Equals(OptionalString(document.RootElement, "code"), "200", StringComparison.Ordinal))
        {
            document.Dispose();
            throw new QWeatherException("ApiRejected");
        }

        return document;
    }

    private static string ReadJsonContent(HttpResponseMessage response)
    {
        using Stream content = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult();
        Stream decoded = content;
        string? encoding = response.Content.Headers.ContentEncoding.FirstOrDefault();
        if (string.Equals(encoding, "gzip", StringComparison.OrdinalIgnoreCase))
            decoded = new GZipStream(content, CompressionMode.Decompress);
        else if (string.Equals(encoding, "deflate", StringComparison.OrdinalIgnoreCase))
            decoded = new DeflateStream(content, CompressionMode.Decompress);
        else if (!string.IsNullOrWhiteSpace(encoding))
            throw new QWeatherException("UnsupportedContentEncoding");

        using (decoded)
        using (var reader = new StreamReader(decoded, Encoding.UTF8, true))
            return reader.ReadToEnd();
    }

    private string? ValidateConfiguration()
    {
        if (!_settings.Enabled)
            return "Disabled";
        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
            return "ApiKeyMissing";
        if (_settings.Longitude.HasValue != _settings.Latitude.HasValue ||
            (_settings.Longitude.HasValue &&
             (_settings.Longitude.Value < -180 || _settings.Longitude.Value > 180 ||
              _settings.Latitude!.Value < -90 || _settings.Latitude.Value > 90)))
            return "CoordinatesInvalid";
        if (!_settings.Longitude.HasValue && string.IsNullOrWhiteSpace(_settings.Location))
            return "LocationMissing";
        if (string.IsNullOrWhiteSpace(_settings.ApiHost) ||
            _settings.ApiHost.IndexOfAny(['/', '\\', '?', '#', '@']) >= 0 ||
            !Uri.CheckHostName(_settings.ApiHost).Equals(UriHostNameType.Dns))
            return "ApiHostInvalid";

        return null;
    }

    private static JsonElement RequiredArray(JsonElement parent, string propertyName, string errorCategory)
    {
        if (!parent.TryGetProperty(propertyName, out JsonElement value) ||
            value.ValueKind != JsonValueKind.Array)
            throw new QWeatherException(errorCategory);

        return value;
    }

    private static string? OptionalString(JsonElement parent, string propertyName) =>
        parent.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static double? OptionalDouble(JsonElement parent, string propertyName)
    {
        string? value = OptionalString(parent, propertyName);
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) &&
               !double.IsNaN(parsed) && !double.IsInfinity(parsed)
            ? parsed
            : null;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
            return;

        if (_ownsHttpClient)
            _httpClient.Dispose();
    }

    private sealed class QWeatherException(string category) : Exception
    {
        public string Category { get; } = category;
    }
}
