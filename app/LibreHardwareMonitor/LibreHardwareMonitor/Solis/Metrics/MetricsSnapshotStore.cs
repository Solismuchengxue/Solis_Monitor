#nullable enable

using System;
using System.Linq;
using System.Threading;
using LibreHardwareMonitor.Solis.Codex;
using LibreHardwareMonitor.Solis.Hardware;
using LibreHardwareMonitor.Solis.Network;
using LibreHardwareMonitor.Solis.Weather;

namespace LibreHardwareMonitor.Solis.Metrics;

public sealed class MetricsSnapshotStore
{
    private readonly object _sync = new();
    private SolisMetricsSnapshot _current = SolisMetricsSnapshot.Empty;
    private SolisMetricsSnapshot _pending = SolisMetricsSnapshot.Empty;

    public SolisMetricsSnapshot Current => Volatile.Read(ref _current);

    public void UpdateHardware(MappedHardwareMetrics metrics)
    {
        if (metrics is null)
            throw new ArgumentNullException(nameof(metrics));

        lock (_sync)
        {
            _pending = _pending with
            {
                Cpu = new CpuMetrics(
                    MetricReading.From(metrics.CpuUsagePercent),
                    MetricReading.From(metrics.CpuTemperatureC),
                    MetricReading.From(metrics.CpuClockGhz),
                    MetricReading.From(metrics.CpuPowerW),
                    metrics.CpuName),
                Gpu = new GpuMetrics(
                    MetricReading.From(metrics.GpuUsagePercent),
                    MetricReading.From(metrics.GpuCoreTemperatureC),
                    MetricReading.From(metrics.GpuCoreClockGhz),
                    MetricReading.From(metrics.GpuPowerW),
                    MetricReading.From(metrics.GpuMemoryUsagePercent),
                    MetricReading.From(metrics.GpuMemoryUsedMb),
                    MetricReading.From(metrics.GpuMemoryTotalMb),
                    MetricReading.From(metrics.GpuMemoryTemperatureC),
                    metrics.GpuName),
                Memory = new MemoryMetrics(
                    MetricReading.From(metrics.MemoryUsagePercent),
                    MetricReading.From(metrics.MemoryTemperatureC),
                    MetricReading.From(metrics.MemoryUsedGb),
                    MetricReading.From(metrics.MemoryTotalGb)),
                Storage = new StorageMetrics(
                    MetricReading.From(metrics.NvmeTemperatureC),
                    (metrics.StorageDevices ?? Array.Empty<MappedStorageDevice>())
                        .Select(device => new StorageDeviceMetrics(
                            device.Id,
                            device.Name,
                            MetricReading.From(device.UsagePercent),
                            MetricReading.From(device.TemperatureC)))
                        .ToArray()),
                Fps = MetricReading.From(metrics.Fps)
            };
        }
    }

    public void UpdateWeather(WeatherMetricsReading reading)
    {
        if (reading is null)
            throw new ArgumentNullException(nameof(reading));

        lock (_sync)
        {
            WeatherMetrics weather = new(
                reading.Location,
                reading.Description,
                MetricReading.From(reading.OutdoorLowC),
                MetricReading.From(reading.OutdoorHighC),
                reading.WindDirection,
                reading.WindScale,
                reading.IconIndex);
            _pending = _pending with
            {
                Weather = weather
            };
            Volatile.Write(ref _current, _current with { Weather = weather });
        }
    }

    public void Publish(
        NetworkThroughputReading network,
        CodexMetricsReading codex,
        DateTimeOffset generatedAt)
    {
        if (codex is null)
            throw new ArgumentNullException(nameof(codex));

        lock (_sync)
        {
            _pending = _pending with
            {
                Network = MapNetwork(network),
                Codex = MapCodex(codex)
            };
            PublishPending(generatedAt);
        }
    }

    private void PublishPending(DateTimeOffset generatedAt)
    {
        _pending = _pending with
        {
            Sequence = _current.Sequence + 1,
            GeneratedAtUnixSeconds = generatedAt.ToUnixTimeSeconds()
        };
        Volatile.Write(ref _current, _pending);
    }

    private static NetworkMetrics MapNetwork(NetworkThroughputReading reading) => new(
        reading.InterfaceId,
        reading.InterfaceName,
        MetricReading.From(reading.DownloadMbps),
        MetricReading.From(reading.UploadMbps),
        reading.ErrorCategory);

    private static CodexMetrics MapCodex(CodexMetricsReading reading) => new(
        reading.Online,
        reading.LastActiveTask,
        reading.ProjectName,
        reading.Model,
        reading.ReasoningEffort,
        MetricReading.From(reading.ContextUsedPercent),
        MetricReading.From(reading.ContextUsedK),
        MetricReading.From(reading.ContextWindowK),
        MetricReading.From(reading.TotalTokens),
        MetricReading.From(reading.WeeklyUsedTokens),
        MetricReading.From(reading.WeeklyRemainingPercent),
        MetricReading.From(reading.MainQuota?.RemainingPercent),
        reading.MainQuota?.Name,
        reading.MainQuota?.ResetAtLocal,
        MetricReading.From(reading.SparkQuota?.RemainingPercent),
        reading.SparkQuota?.Name,
        reading.SparkQuota?.ResetAtLocal);
}
