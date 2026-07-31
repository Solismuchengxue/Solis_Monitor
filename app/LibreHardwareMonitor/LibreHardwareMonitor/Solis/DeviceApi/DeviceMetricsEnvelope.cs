#nullable enable

using System;
using System.Globalization;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using LibreHardwareMonitor.Solis.Metrics;

namespace LibreHardwareMonitor.Solis.DeviceApi;

public sealed record DeviceMetricsEnvelope(
    [property: JsonPropertyName("schema")] int Schema,
    [property: JsonPropertyName("sequence")] ulong Sequence,
    [property: JsonPropertyName("generated_at")] long GeneratedAt,
    [property: JsonPropertyName("system")] DeviceSystemMetrics System,
    [property: JsonPropertyName("codex")] DeviceCodexMetrics Codex,
    [property: JsonPropertyName("environment")] DeviceEnvironmentMetrics Environment,
    [property: JsonPropertyName("availability")] DeviceMetricsAvailability Availability)
{
    public const int MaximumPayloadBytes = 4096;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        PropertyNameCaseInsensitive = false
    };

    public static DeviceMetricsEnvelope FromSnapshot(SolisMetricsSnapshot snapshot, DateTimeOffset currentTime)
    {
        if (snapshot is null)
            throw new ArgumentNullException(nameof(snapshot));

        return new DeviceMetricsEnvelope(
            1,
            snapshot.Sequence,
            snapshot.GeneratedAtUnixSeconds,
            new DeviceSystemMetrics(
                currentTime.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture),
                snapshot.Cpu.Name ?? string.Empty,
                Value(snapshot.Cpu.UsagePercent),
                Value(snapshot.Cpu.TemperatureC),
                Value(snapshot.Cpu.ClockGhz),
                Value(snapshot.Cpu.PowerW),
                snapshot.Gpu.Name ?? string.Empty,
                Value(snapshot.Gpu.UsagePercent),
                Value(snapshot.Gpu.CoreTemperatureC),
                Value(snapshot.Gpu.CoreClockGhz),
                Value(snapshot.Gpu.PowerW),
                Value(snapshot.Gpu.MemoryUsagePercent),
                Value(snapshot.Gpu.MemoryUsedMb),
                Value(snapshot.Gpu.MemoryTotalMb),
                Value(snapshot.Gpu.MemoryTemperatureC),
                Value(snapshot.Memory.UsagePercent),
                Value(snapshot.Memory.TemperatureC),
                Value(snapshot.Memory.UsedGb),
                Value(snapshot.Memory.TotalGb),
                Value(snapshot.Fps),
                Value(snapshot.Storage.NvmeTemperatureC),
                Value(snapshot.Network.DownloadMbps),
                Value(snapshot.Network.UploadMbps),
                snapshot.Network.InterfaceName ?? string.Empty,
                snapshot.Storage.Devices
                    .Take(4)
                    .Select(device => new DeviceStorageMetrics(
                        device.Name,
                        Value(device.UsagePercent),
                        Value(device.TemperatureC)))
                    .ToArray()),
            new DeviceCodexMetrics(
                snapshot.Codex.Online,
                snapshot.Codex.ProjectName ?? string.Empty,
                snapshot.Codex.LastActiveTask ?? string.Empty,
                snapshot.Codex.Model ?? string.Empty,
                snapshot.Codex.ReasoningEffort ?? string.Empty,
                Value(snapshot.Codex.ContextUsedPercent),
                Value(snapshot.Codex.ContextUsedK),
                Value(snapshot.Codex.ContextWindowK),
                Value(snapshot.Codex.TotalTokens),
                Value(snapshot.Codex.WeeklyUsedTokens),
                Value(snapshot.Codex.WeeklyRemainingPercent),
                Value(snapshot.Codex.MainWeeklyRemainingPercent),
                snapshot.Codex.MainQuotaName ?? string.Empty,
                snapshot.Codex.MainQuotaResetAt ?? string.Empty,
                Value(snapshot.Codex.SparkWeeklyRemainingPercent),
                snapshot.Codex.SparkQuotaName ?? string.Empty,
                snapshot.Codex.SparkQuotaResetAt ?? string.Empty),
            new DeviceEnvironmentMetrics(
                snapshot.Weather.Location ?? string.Empty,
                snapshot.Weather.Description ?? string.Empty,
                snapshot.Weather.WindDirection ?? string.Empty,
                snapshot.Weather.WindScale ?? string.Empty,
                snapshot.Weather.IconIndex,
                Value(snapshot.Weather.OutdoorLowC),
                Value(snapshot.Weather.OutdoorHighC),
                null,
                null),
            new DeviceMetricsAvailability(
                new DeviceSystemAvailability(
                    snapshot.Cpu.UsagePercent.Available,
                    snapshot.Cpu.TemperatureC.Available,
                    snapshot.Cpu.ClockGhz.Available,
                    snapshot.Cpu.PowerW.Available,
                    snapshot.Gpu.UsagePercent.Available,
                    snapshot.Gpu.CoreTemperatureC.Available,
                    snapshot.Gpu.CoreClockGhz.Available,
                    snapshot.Gpu.PowerW.Available,
                    snapshot.Gpu.MemoryUsagePercent.Available,
                    snapshot.Gpu.MemoryUsedMb.Available,
                    snapshot.Gpu.MemoryTotalMb.Available,
                    snapshot.Gpu.MemoryTemperatureC.Available,
                    snapshot.Memory.UsagePercent.Available,
                    snapshot.Memory.TemperatureC.Available,
                    snapshot.Fps.Available,
                    snapshot.Storage.NvmeTemperatureC.Available,
                    snapshot.Network.DownloadMbps.Available,
                    snapshot.Network.UploadMbps.Available),
                new DeviceCodexAvailability(
                    !string.IsNullOrEmpty(snapshot.Codex.ProjectName),
                    snapshot.Codex.ContextUsedPercent.Available,
                    snapshot.Codex.ContextUsedK.Available,
                    snapshot.Codex.ContextWindowK.Available,
                    snapshot.Codex.TotalTokens.Available,
                    snapshot.Codex.WeeklyUsedTokens.Available,
                    snapshot.Codex.WeeklyRemainingPercent.Available,
                    snapshot.Codex.MainWeeklyRemainingPercent.Available,
                    !string.IsNullOrWhiteSpace(snapshot.Codex.MainQuotaName),
                    !string.IsNullOrWhiteSpace(snapshot.Codex.MainQuotaResetAt),
                    snapshot.Codex.SparkWeeklyRemainingPercent.Available,
                    !string.IsNullOrWhiteSpace(snapshot.Codex.SparkQuotaName),
                    !string.IsNullOrWhiteSpace(snapshot.Codex.SparkQuotaResetAt)),
                new DeviceEnvironmentAvailability(
                    !string.IsNullOrEmpty(snapshot.Weather.Description),
                    snapshot.Weather.OutdoorLowC.Available && snapshot.Weather.OutdoorHighC.Available,
                    false,
                    false,
                    !string.IsNullOrWhiteSpace(snapshot.Weather.WindDirection),
                    !string.IsNullOrWhiteSpace(snapshot.Weather.WindScale),
                    snapshot.Weather.IconIndex.HasValue)));
    }

    public byte[] Serialize() => JsonSerializer.SerializeToUtf8Bytes(this, JsonOptions);

    private static double? Value(MetricReading reading) => reading.Available ? reading.Value : null;
}

public sealed record DeviceSystemMetrics(
    [property: JsonPropertyName("time")] string Time,
    [property: JsonPropertyName("cpu_name")] string CpuName,
    [property: JsonPropertyName("cpu_usage")] double? CpuUsage,
    [property: JsonPropertyName("cpu_temp_c")] double? CpuTempC,
    [property: JsonPropertyName("cpu_ghz")] double? CpuGhz,
    [property: JsonPropertyName("cpu_w")] double? CpuW,
    [property: JsonPropertyName("gpu_name")] string GpuName,
    [property: JsonPropertyName("gpu_usage")] double? GpuUsage,
    [property: JsonPropertyName("gpu_temp_c")] double? GpuTempC,
    [property: JsonPropertyName("gpu_ghz")] double? GpuGhz,
    [property: JsonPropertyName("gpu_w")] double? GpuW,
    [property: JsonPropertyName("gpu_memory_usage")] double? GpuMemoryUsage,
    [property: JsonPropertyName("gpu_memory_used_mb")] double? GpuMemoryUsedMb,
    [property: JsonPropertyName("gpu_memory_total_mb")] double? GpuMemoryTotalMb,
    [property: JsonPropertyName("gpu_memory_temp_c")] double? GpuMemoryTempC,
    [property: JsonPropertyName("memory_usage")] double? MemoryUsage,
    [property: JsonPropertyName("memory_temp_c")] double? MemoryTempC,
    [property: JsonPropertyName("memory_used_gb")] double? MemoryUsedGb,
    [property: JsonPropertyName("memory_total_gb")] double? MemoryTotalGb,
    [property: JsonPropertyName("fps")] double? Fps,
    [property: JsonPropertyName("nvme_temp_c")] double? NvmeTempC,
    [property: JsonPropertyName("download_mbps")] double? DownloadMbps,
    [property: JsonPropertyName("upload_mbps")] double? UploadMbps,
    [property: JsonPropertyName("network_name")] string NetworkName,
    [property: JsonPropertyName("storage_devices")] DeviceStorageMetrics[] StorageDevices);

public sealed record DeviceStorageMetrics(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("usage")] double? Usage,
    [property: JsonPropertyName("temp_c")] double? TempC);

public sealed record DeviceCodexMetrics(
    [property: JsonPropertyName("online")] bool Online,
    [property: JsonPropertyName("project")] string Project,
    [property: JsonPropertyName("task")] string Task,
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("reasoning_effort")] string ReasoningEffort,
    [property: JsonPropertyName("context_used")] double? ContextUsed,
    [property: JsonPropertyName("context_used_k")] double? ContextUsedK,
    [property: JsonPropertyName("context_window_k")] double? ContextWindowK,
    [property: JsonPropertyName("total_tokens")] double? TotalTokens,
    [property: JsonPropertyName("weekly_used_tokens")] double? WeeklyUsedTokens,
    [property: JsonPropertyName("weekly_remaining")] double? WeeklyRemaining,
    [property: JsonPropertyName("main_weekly_remaining")] double? MainWeeklyRemaining,
    [property: JsonPropertyName("main_quota_name")] string MainQuotaName,
    [property: JsonPropertyName("main_quota_reset_at")] string MainQuotaResetAt,
    [property: JsonPropertyName("spark_weekly_remaining")] double? SparkWeeklyRemaining,
    [property: JsonPropertyName("spark_quota_name")] string SparkQuotaName,
    [property: JsonPropertyName("spark_quota_reset_at")] string SparkQuotaResetAt);

public sealed record DeviceEnvironmentMetrics(
    [property: JsonPropertyName("location")] string Location,
    [property: JsonPropertyName("weather")] string Weather,
    [property: JsonPropertyName("wind_direction")] string WindDirection,
    [property: JsonPropertyName("wind_scale")] string WindScale,
    [property: JsonPropertyName("weather_icon")] int? WeatherIcon,
    [property: JsonPropertyName("outdoor_low_c")] double? OutdoorLowC,
    [property: JsonPropertyName("outdoor_high_c")] double? OutdoorHighC,
    [property: JsonPropertyName("indoor_temp_c")] double? IndoorTempC,
    [property: JsonPropertyName("humidity")] double? Humidity);

public sealed record DeviceMetricsAvailability(
    [property: JsonPropertyName("system")] DeviceSystemAvailability System,
    [property: JsonPropertyName("codex")] DeviceCodexAvailability Codex,
    [property: JsonPropertyName("environment")] DeviceEnvironmentAvailability Environment);

public sealed record DeviceSystemAvailability(
    [property: JsonPropertyName("cpu_usage")] bool CpuUsage,
    [property: JsonPropertyName("cpu_temp_c")] bool CpuTempC,
    [property: JsonPropertyName("cpu_ghz")] bool CpuGhz,
    [property: JsonPropertyName("cpu_w")] bool CpuW,
    [property: JsonPropertyName("gpu_usage")] bool GpuUsage,
    [property: JsonPropertyName("gpu_temp_c")] bool GpuTempC,
    [property: JsonPropertyName("gpu_ghz")] bool GpuGhz,
    [property: JsonPropertyName("gpu_w")] bool GpuW,
    [property: JsonPropertyName("gpu_memory_usage")] bool GpuMemoryUsage,
    [property: JsonPropertyName("gpu_memory_used_mb")] bool GpuMemoryUsedMb,
    [property: JsonPropertyName("gpu_memory_total_mb")] bool GpuMemoryTotalMb,
    [property: JsonPropertyName("gpu_memory_temp_c")] bool GpuMemoryTempC,
    [property: JsonPropertyName("memory_usage")] bool MemoryUsage,
    [property: JsonPropertyName("memory_temp_c")] bool MemoryTempC,
    [property: JsonPropertyName("fps")] bool Fps,
    [property: JsonPropertyName("nvme_temp_c")] bool NvmeTempC,
    [property: JsonPropertyName("download_mbps")] bool DownloadMbps,
    [property: JsonPropertyName("upload_mbps")] bool UploadMbps);

public sealed record DeviceCodexAvailability(
    [property: JsonPropertyName("project")] bool Project,
    [property: JsonPropertyName("context_used")] bool ContextUsed,
    [property: JsonPropertyName("context_used_k")] bool ContextUsedK,
    [property: JsonPropertyName("context_window_k")] bool ContextWindowK,
    [property: JsonPropertyName("total_tokens")] bool TotalTokens,
    [property: JsonPropertyName("weekly_used_tokens")] bool WeeklyUsedTokens,
    [property: JsonPropertyName("weekly_remaining")] bool WeeklyRemaining,
    [property: JsonPropertyName("main_weekly_remaining")] bool MainWeeklyRemaining,
    [property: JsonPropertyName("main_quota_name")] bool MainQuotaName,
    [property: JsonPropertyName("main_quota_reset_at")] bool MainQuotaResetAt,
    [property: JsonPropertyName("spark_weekly_remaining")] bool SparkWeeklyRemaining,
    [property: JsonPropertyName("spark_quota_name")] bool SparkQuotaName,
    [property: JsonPropertyName("spark_quota_reset_at")] bool SparkQuotaResetAt);

public sealed record DeviceEnvironmentAvailability(
    [property: JsonPropertyName("weather")] bool Weather,
    [property: JsonPropertyName("outdoor_range")] bool OutdoorRange,
    [property: JsonPropertyName("indoor_temp_c")] bool IndoorTempC,
    [property: JsonPropertyName("humidity")] bool Humidity,
    [property: JsonPropertyName("wind_direction")] bool WindDirection,
    [property: JsonPropertyName("wind_scale")] bool WindScale,
    [property: JsonPropertyName("weather_icon")] bool WeatherIcon);
