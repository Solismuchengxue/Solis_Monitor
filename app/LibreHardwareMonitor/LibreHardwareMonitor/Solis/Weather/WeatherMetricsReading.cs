#nullable enable

namespace LibreHardwareMonitor.Solis.Weather;

public sealed record WeatherMetricsReading(
    bool Available,
    string? Location,
    string? Description,
    double? OutdoorLowC,
    double? OutdoorHighC,
    string? ErrorCategory,
    string? WindDirection = null,
    string? WindScale = null,
    int? IconIndex = null)
{
    public static WeatherMetricsReading Empty(string errorCategory) => new(
        false,
        null,
        null,
        null,
        null,
        errorCategory,
        null,
        null,
        null);
}
