internal static partial class SmokeTests
{
static void DiagnosticsTrackFaultAndLastNormalTime()
{
    var monitor = new SolisDiagnosticsMonitor();
    DateTimeOffset healthyAt = DateTimeOffset.Parse("2026-07-23T12:00:00Z");

    monitor.ObserveWeather(
        new WeatherMetricsReading(true, "大连", "多云", 24, 29, null),
        healthyAt);
    monitor.ObserveWeather(
        new WeatherMetricsReading(true, "大连", "多云", 24, 29, "NetworkError"),
        healthyAt.AddMinutes(5));

    SolisDiagnosticsSnapshot failed = monitor.Current;
    Equal(DiagnosticCheckState.Fault, failed.Weather.State, "天气网络错误应成为当前故障");
    Equal(healthyAt, failed.Weather.LastNormalAt, "故障后应保留最近正常时间");
    True(failed.Summary.Contains("天气", StringComparison.Ordinal),
        "总体诊断应指出天气故障");

    monitor.ObserveWeather(
        new WeatherMetricsReading(true, "大连", "晴", 25, 30, null),
        healthyAt.AddMinutes(10));

    SolisDiagnosticsSnapshot recovered = monitor.Current;
    Equal(DiagnosticCheckState.Normal, recovered.Weather.State, "天气恢复后应回到正常");
    Equal(healthyAt.AddMinutes(10), recovered.Weather.LastNormalAt,
        "恢复后应更新最近正常时间");
}

static void DiagnosticsRecoverFromWeatherCollectionFailure()
{
    var monitor = new SolisDiagnosticsMonitor();
    DateTimeOffset healthyAt = DateTimeOffset.Parse("2026-08-08T12:00:00Z");
    monitor.ObserveWeather(
        new WeatherMetricsReading(true, "大连", "晴", 25, 30, null), healthyAt);

    monitor.ObserveWeatherCollectionFailure(healthyAt.AddMinutes(1));
    SolisDiagnosticsSnapshot failed = monitor.Current;
    Equal(DiagnosticCheckState.Fault, failed.Weather.State,
        "天气后台异常没有进入诊断故障状态");
    Equal("BackgroundCollectionError", failed.Weather.ErrorCategory,
        "天气后台异常类别不稳定");
    Equal(healthyAt, failed.Weather.LastNormalAt,
        "天气后台异常丢失最近正常时间");

    monitor.ObserveWeather(
        new WeatherMetricsReading(true, "大连", "晴", 26, 31, null),
        healthyAt.AddMinutes(2));
    Equal(DiagnosticCheckState.Normal, monitor.Current.Weather.State,
        "天气采集恢复后诊断没有自动恢复");
}
}
