#nullable enable

using System;
using System.Collections.Generic;

namespace LibreHardwareMonitor.Solis.Metrics;

public sealed record CpuMetrics(
    MetricReading UsagePercent,
    MetricReading TemperatureC,
    MetricReading ClockGhz,
    MetricReading PowerW,
    string? Name = null)
{
    public static CpuMetrics Empty { get; } = new(
        MetricReading.Unavailable,
        MetricReading.Unavailable,
        MetricReading.Unavailable,
        MetricReading.Unavailable);
}

public sealed record GpuMetrics(
    MetricReading UsagePercent,
    MetricReading CoreTemperatureC,
    MetricReading CoreClockGhz,
    MetricReading PowerW,
    MetricReading MemoryUsagePercent,
    MetricReading MemoryUsedMb,
    MetricReading MemoryTotalMb,
    MetricReading MemoryTemperatureC,
    string? Name = null)
{
    public static GpuMetrics Empty { get; } = new(
        MetricReading.Unavailable,
        MetricReading.Unavailable,
        MetricReading.Unavailable,
        MetricReading.Unavailable,
        MetricReading.Unavailable,
        MetricReading.Unavailable,
        MetricReading.Unavailable,
        MetricReading.Unavailable);
}

public sealed record MemoryMetrics(
    MetricReading UsagePercent,
    MetricReading TemperatureC,
    MetricReading UsedGb,
    MetricReading TotalGb)
{
    public static MemoryMetrics Empty { get; } = new(
        MetricReading.Unavailable,
        MetricReading.Unavailable,
        MetricReading.Unavailable,
        MetricReading.Unavailable);
}

public sealed record StorageDeviceMetrics(
    string Id,
    string Name,
    MetricReading UsagePercent,
    MetricReading TemperatureC);

public sealed record StorageMetrics(
    MetricReading NvmeTemperatureC,
    IReadOnlyList<StorageDeviceMetrics> Devices)
{
    public static StorageMetrics Empty { get; } = new(
        MetricReading.Unavailable,
        Array.Empty<StorageDeviceMetrics>());
}

public sealed record NetworkMetrics(
    string? InterfaceId,
    string? InterfaceName,
    MetricReading DownloadMbps,
    MetricReading UploadMbps,
    string? ErrorCategory)
{
    public static NetworkMetrics Empty { get; } = new(
        null,
        null,
        MetricReading.Unavailable,
        MetricReading.Unavailable,
        "NotSampled");
}

public sealed record CodexMetrics(
    bool Online,
    string? LastActiveTask,
    string? ProjectName,
    string? Model,
    string? ReasoningEffort,
    MetricReading ContextUsedPercent,
    MetricReading ContextUsedK,
    MetricReading ContextWindowK,
    MetricReading TotalTokens,
    MetricReading WeeklyUsedTokens,
    MetricReading WeeklyRemainingPercent,
    MetricReading MainWeeklyRemainingPercent,
    string? MainQuotaName,
    string? MainQuotaResetAt,
    MetricReading SparkWeeklyRemainingPercent,
    string? SparkQuotaName,
    string? SparkQuotaResetAt)
{
    public static CodexMetrics Empty { get; } = new(
        false,
        null,
        null,
        null,
        null,
        MetricReading.Unavailable,
        MetricReading.Unavailable,
        MetricReading.Unavailable,
        MetricReading.Unavailable,
        MetricReading.Unavailable,
        MetricReading.Unavailable,
        MetricReading.Unavailable,
        null,
        null,
        MetricReading.Unavailable,
        null,
        null);
}

public sealed record WeatherMetrics(
    string? Location,
    string? Description,
    MetricReading OutdoorLowC,
    MetricReading OutdoorHighC,
    string? WindDirection = null,
    string? WindScale = null,
    int? IconIndex = null)
{
    public static WeatherMetrics Empty { get; } = new(
        null,
        null,
        MetricReading.Unavailable,
        MetricReading.Unavailable);
}

public sealed record SolisMetricsSnapshot(
    ulong Sequence,
    long GeneratedAtUnixSeconds,
    CpuMetrics Cpu,
    GpuMetrics Gpu,
    MemoryMetrics Memory,
    StorageMetrics Storage,
    MetricReading Fps,
    NetworkMetrics Network,
    CodexMetrics Codex,
    WeatherMetrics Weather)
{
    public static SolisMetricsSnapshot Empty { get; } = new(
        0,
        0,
        CpuMetrics.Empty,
        GpuMetrics.Empty,
        MemoryMetrics.Empty,
        StorageMetrics.Empty,
        MetricReading.Unavailable,
        NetworkMetrics.Empty,
        CodexMetrics.Empty,
        WeatherMetrics.Empty);
}
