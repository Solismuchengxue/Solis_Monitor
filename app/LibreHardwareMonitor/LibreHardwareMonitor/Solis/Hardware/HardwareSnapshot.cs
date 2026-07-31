#nullable enable

using System;
using System.Collections.Generic;

namespace LibreHardwareMonitor.Solis.Hardware;

public enum SolisHardwareKind
{
    Cpu,
    GpuNvidia,
    GpuAmd,
    GpuIntel,
    Memory,
    Storage,
    Unknown
}

public enum SolisSensorKind
{
    Load,
    Temperature,
    Clock,
    Power,
    Data,
    SmallData,
    Factor,
    Unknown
}

public sealed record RawHardwareSensor(
    SolisHardwareKind HardwareKind,
    string HardwareId,
    string HardwareName,
    SolisSensorKind SensorKind,
    string SensorName,
    double? Value);

public sealed record HardwareSnapshot(
    DateTimeOffset CapturedAt,
    IReadOnlyList<RawHardwareSensor> Sensors,
    IReadOnlyList<string> ErrorCategories);

public sealed record MappedStorageDevice(
    string Id,
    string Name,
    double? UsagePercent,
    double? TemperatureC);

public sealed record MappedHardwareMetrics(
    double? CpuUsagePercent,
    double? CpuTemperatureC,
    double? CpuClockGhz,
    double? CpuPowerW,
    double? GpuUsagePercent,
    double? GpuCoreTemperatureC,
    double? GpuCoreClockGhz,
    double? GpuPowerW,
    double? GpuMemoryUsagePercent,
    double? GpuMemoryUsedMb,
    double? GpuMemoryTotalMb,
    double? GpuMemoryTemperatureC,
    double? MemoryUsagePercent,
    double? MemoryTemperatureC,
    double? NvmeTemperatureC,
    double? Fps,
    string? CpuName,
    string? GpuName,
    string? SelectedGpuId,
    string? SelectedNvmeId,
    IReadOnlyList<string> NvmeNames,
    double? MemoryUsedGb = null,
    double? MemoryTotalGb = null,
    IReadOnlyList<MappedStorageDevice>? StorageDevices = null);
