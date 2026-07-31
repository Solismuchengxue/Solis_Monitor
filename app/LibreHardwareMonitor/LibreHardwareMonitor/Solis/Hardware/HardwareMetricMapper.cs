#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace LibreHardwareMonitor.Solis.Hardware;

public static class HardwareMetricMapper
{
    private static readonly string[] GpuPowerNames =
    {
        "GPU Package",
        "GPU Board Power",
        "GPU Power"
    };

    public static MappedHardwareMetrics Map(
        HardwareSnapshot snapshot,
        string? preferredGpuId = null,
        string? preferredNvmeId = null)
    {
        if (snapshot is null)
            throw new ArgumentNullException(nameof(snapshot));

        HardwareGroup[] groups = GroupSensors(snapshot).ToArray();
        HardwareGroup? cpu = groups.FirstOrDefault(group => group.Kind == SolisHardwareKind.Cpu);
        HardwareGroup? gpu = SelectGpu(groups, preferredGpuId);
        HardwareGroup[] memoryGroups = groups
            .Where(group => group.Kind == SolisHardwareKind.Memory)
            .ToArray();
        HardwareGroup[] nvmeGroups = groups
            .Where(group => group.Kind == SolisHardwareKind.Storage && IsNvme(group.Id))
            .ToArray();
        HardwareGroup[] storageGroups = groups
            .Where(group => group.Kind == SolisHardwareKind.Storage)
            .ToArray();
        HardwareGroup? selectedNvme = SelectNvme(nvmeGroups, preferredNvmeId);

        HardwareGroup? totalMemory = memoryGroups.FirstOrDefault(group =>
            group.Id.Equals("/ram", StringComparison.OrdinalIgnoreCase));
        double? memoryUsedGb = PreferredAverage(
            totalMemory, SolisSensorKind.Data, IsNonNegative, "Memory Used");
        double? memoryAvailableGb = PreferredAverage(
            totalMemory, SolisSensorKind.Data, IsNonNegative, "Memory Available");
        double? memoryTotalGb = memoryUsedGb.HasValue && memoryAvailableGb.HasValue
            ? memoryUsedGb.Value + memoryAvailableGb.Value
            : null;
        MappedStorageDevice[] storageDevices = storageGroups
            .Select(group => new MappedStorageDevice(
                group.Id,
                group.Name,
                PreferredAverage(group, SolisSensorKind.Load, IsLoad, "Used Space"),
                StorageTemperature(group)))
            .ToArray();

        double? memoryUsedMb = PreferredValue(gpu, SolisSensorKind.SmallData,
            "GPU Memory Used", "D3D Dedicated Memory Used");
        double? memoryTotalMb = PreferredValue(gpu, SolisSensorKind.SmallData,
            "GPU Memory Total", "D3D Dedicated Memory Total");
        double? memoryUsagePercent = PreferredValue(gpu, SolisSensorKind.Load, "GPU Memory");
        if (!memoryUsagePercent.HasValue && memoryUsedMb.HasValue && memoryTotalMb > 0)
            memoryUsagePercent = memoryUsedMb.Value / memoryTotalMb.Value * 100D;

        return new MappedHardwareMetrics(
            CpuUsagePercent: PreferredAverage(cpu, SolisSensorKind.Load, IsLoad,
                "CPU Total", "CPU Core"),
            CpuTemperatureC: CpuTemperature(cpu),
            CpuClockGhz: Divide(CpuClock(cpu), 1000D),
            CpuPowerW: PreferredAverage(cpu, SolisSensorKind.Power, IsPower,
                "CPU Package", "CPU Total"),
            GpuUsagePercent: PreferredAverage(gpu, SolisSensorKind.Load, IsLoad, "GPU Core"),
            GpuCoreTemperatureC: PreferredAverage(gpu, SolisSensorKind.Temperature, IsTemperature, "GPU Core"),
            GpuCoreClockGhz: Divide(PreferredAverage(gpu, SolisSensorKind.Clock, IsClock, "GPU Core"), 1000D),
            GpuPowerW: PreferredAverage(gpu, SolisSensorKind.Power, IsPower, GpuPowerNames),
            GpuMemoryUsagePercent: Validate(memoryUsagePercent, IsLoad),
            GpuMemoryUsedMb: Validate(memoryUsedMb, IsNonNegative),
            GpuMemoryTotalMb: Validate(memoryTotalMb, IsPositive),
            GpuMemoryTemperatureC: PreferredAverage(gpu, SolisSensorKind.Temperature, IsTemperature,
                "GPU Memory Junction", "GPU Memory"),
            MemoryUsagePercent: MemoryUsage(memoryGroups),
            MemoryTemperatureC: MemoryTemperature(memoryGroups),
            NvmeTemperatureC: NvmeTemperature(selectedNvme),
            Fps: PreferredAverage(gpu, SolisSensorKind.Factor, IsFps, "Fullscreen FPS"),
            CpuName: cpu?.Name,
            GpuName: gpu?.Name,
            SelectedGpuId: gpu?.Id,
            SelectedNvmeId: selectedNvme?.Id,
            NvmeNames: nvmeGroups.Select(group => group.Name).ToArray(),
            MemoryUsedGb: Validate(memoryUsedGb, IsNonNegative),
            MemoryTotalGb: Validate(memoryTotalGb, IsPositive),
            StorageDevices: storageDevices);
    }

    private static IEnumerable<HardwareGroup> GroupSensors(HardwareSnapshot snapshot) => snapshot.Sensors
        .GroupBy(sensor => sensor.HardwareId, StringComparer.Ordinal)
        .Select(group => new HardwareGroup(
            group.Key,
            group.First().HardwareKind,
            group.First().HardwareName,
            group.ToArray()))
        .OrderBy(group => group.Id, StringComparer.Ordinal);

    private static HardwareGroup? SelectGpu(IEnumerable<HardwareGroup> groups, string? preferredGpuId)
    {
        HardwareGroup[] candidates = groups
            .Where(group => IsGpu(group.Kind) && !IsControlledVirtualGpu(group.Name))
            .ToArray();

        HardwareGroup? preferred = FindById(candidates, preferredGpuId);
        return preferred ?? candidates
            .OrderBy(group => GpuPriority(group.Kind))
            .ThenBy(group => group.Id, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static HardwareGroup? SelectNvme(HardwareGroup[] candidates, string? preferredNvmeId)
    {
        HardwareGroup? preferred = FindById(candidates, preferredNvmeId);
        if (preferred is not null)
            return preferred;

        return candidates
            .Select(group => new { Group = group, Temperature = NvmeTemperature(group) })
            .Where(candidate => candidate.Temperature.HasValue)
            .OrderByDescending(candidate => candidate.Temperature)
            .ThenBy(candidate => candidate.Group.Id, StringComparer.Ordinal)
            .Select(candidate => candidate.Group)
            .FirstOrDefault();
    }

    private static HardwareGroup? FindById(IEnumerable<HardwareGroup> groups, string? preferredId)
    {
        if (string.IsNullOrWhiteSpace(preferredId))
            return null;

        return groups.FirstOrDefault(group => group.Id.Equals(preferredId, StringComparison.Ordinal));
    }

    private static bool IsGpu(SolisHardwareKind kind) =>
        kind == SolisHardwareKind.GpuNvidia ||
        kind == SolisHardwareKind.GpuAmd ||
        kind == SolisHardwareKind.GpuIntel;

    private static int GpuPriority(SolisHardwareKind kind) => kind switch
    {
        SolisHardwareKind.GpuNvidia => 0,
        SolisHardwareKind.GpuAmd => 1,
        SolisHardwareKind.GpuIntel => 2,
        _ => 3
    };

    private static bool IsControlledVirtualGpu(string name) =>
        NormalizeName(name).Equals("Honor Virtual Display", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeName(string name) => string.Join(" ",
        name.Normalize(System.Text.NormalizationForm.FormKC)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static bool IsNvme(string hardwareId) =>
        hardwareId.StartsWith("/nvme/", StringComparison.OrdinalIgnoreCase);

    private static double? CpuTemperature(HardwareGroup? cpu)
    {
        double? package = PreferredAverage(cpu, SolisSensorKind.Temperature, IsTemperature, "CPU Package");
        if (package.HasValue)
            return package;

        double[] coreTemperatures = PreferredValues(
            cpu,
            SolisSensorKind.Temperature,
            IsTemperature,
            "CPU Core").ToArray();
        return coreTemperatures.Length == 0 ? null : coreTemperatures.Max();
    }

    private static double? CpuClock(HardwareGroup? cpu)
    {
        double? reportedAverage = PreferredAverage(
            cpu,
            SolisSensorKind.Clock,
            IsClock,
            "Cores (Average)");
        if (reportedAverage.HasValue)
            return reportedAverage;

        double[] coreClocks = cpu?.Sensors
            .Where(sensor =>
                sensor.SensorKind == SolisSensorKind.Clock &&
                IsCoreClockName(sensor.SensorName) &&
                IsClock(sensor.Value))
            .Select(sensor => sensor.Value!.Value)
            .ToArray() ?? Array.Empty<double>();
        if (coreClocks.Length > 0)
            return coreClocks.Average();

        double[] fallbackClocks = cpu?.Sensors
            .Where(sensor =>
                sensor.SensorKind == SolisSensorKind.Clock &&
                !sensor.SensorName.Equals("Bus Speed", StringComparison.OrdinalIgnoreCase) &&
                sensor.SensorName.IndexOf("Effective", StringComparison.OrdinalIgnoreCase) < 0 &&
                IsClock(sensor.Value))
            .Select(sensor => sensor.Value!.Value)
            .ToArray() ?? Array.Empty<double>();
        return fallbackClocks.Length == 0 ? null : fallbackClocks.Average();
    }

    private static bool IsCoreClockName(string name) =>
        name.StartsWith("CPU Core", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("P-Core", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("E-Core", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("Core #", StringComparison.OrdinalIgnoreCase);

    private static double? MemoryUsage(IEnumerable<HardwareGroup> groups)
    {
        HardwareGroup? totalMemory = groups.FirstOrDefault(group =>
            group.Id.Equals("/ram", StringComparison.OrdinalIgnoreCase));
        return PreferredAverage(totalMemory, SolisSensorKind.Load, IsLoad, "Memory");
    }

    private static double? MemoryTemperature(IEnumerable<HardwareGroup> groups)
    {
        double[] values = groups
            .SelectMany(group => group.Sensors)
            .Where(sensor =>
                sensor.SensorKind == SolisSensorKind.Temperature &&
                sensor.SensorName.StartsWith("DIMM #", StringComparison.OrdinalIgnoreCase) &&
                IsTemperature(sensor.Value))
            .Select(sensor => sensor.Value!.Value)
            .ToArray();
        return values.Length == 0 ? null : values.Max();
    }

    private static double? NvmeTemperature(HardwareGroup? nvme)
        => StorageTemperature(nvme);

    private static double? StorageTemperature(HardwareGroup? storage)
    {
        double? composite = PreferredAverage(
            storage,
            SolisSensorKind.Temperature,
            IsStorageTemperature,
            "Composite Temperature");
        if (composite.HasValue)
            return composite;

        double[] values = storage?.Sensors
            .Where(sensor =>
                sensor.SensorKind == SolisSensorKind.Temperature &&
                sensor.SensorName.StartsWith("Temperature", StringComparison.OrdinalIgnoreCase) &&
                IsStorageTemperature(sensor.Value))
            .Select(sensor => sensor.Value!.Value)
            .ToArray() ?? Array.Empty<double>();
        return values.Length == 0 ? null : values.Max();
    }

    private static double? PreferredValue(
        HardwareGroup? group,
        SolisSensorKind kind,
        params string[] names) => PreferredAverage(group, kind, IsNonNegative, names);

    private static double? PreferredAverage(
        HardwareGroup? group,
        SolisSensorKind kind,
        Func<double?, bool> isValid,
        params string[] names)
    {
        double[] values = PreferredValues(group, kind, isValid, names).ToArray();
        return values.Length == 0 ? null : values.Average();
    }

    private static IEnumerable<double> PreferredValues(
        HardwareGroup? group,
        SolisSensorKind kind,
        Func<double?, bool> isValid,
        params string[] names)
    {
        if (group is null)
            return Array.Empty<double>();

        foreach (string name in names)
        {
            double[] exact = group.Sensors
                .Where(sensor =>
                    sensor.SensorKind == kind &&
                    sensor.SensorName.Equals(name, StringComparison.OrdinalIgnoreCase) &&
                    isValid(sensor.Value))
                .Select(sensor => sensor.Value!.Value)
                .ToArray();
            if (exact.Length > 0)
                return exact;

            double[] prefixed = group.Sensors
                .Where(sensor =>
                    sensor.SensorKind == kind &&
                    sensor.SensorName.StartsWith(name, StringComparison.OrdinalIgnoreCase) &&
                    isValid(sensor.Value))
                .Select(sensor => sensor.Value!.Value)
                .ToArray();
            if (prefixed.Length > 0)
                return prefixed;
        }

        return Array.Empty<double>();
    }

    private static double? Divide(double? value, double divisor) => value.HasValue ? value.Value / divisor : null;

    private static double? Validate(double? value, Func<double?, bool> isValid) => isValid(value) ? value : null;

    private static bool IsLoad(double? value) => IsInRange(value, 0, 100);
    private static bool IsTemperature(double? value) => IsInRange(value, -20, 150);
    private static bool IsStorageTemperature(double? value) => IsInRange(value, 1, 150);
    private static bool IsClock(double? value) => IsInRange(value, 1, 10_000);
    private static bool IsPower(double? value) => IsInRange(value, 0, 2_000);
    private static bool IsFps(double? value) => IsInRange(value, 0, 10_000);
    private static bool IsNonNegative(double? value) => IsInRange(value, 0, double.MaxValue);
    private static bool IsPositive(double? value) => IsInRange(value, double.Epsilon, double.MaxValue);

    private static bool IsInRange(double? value, double minimum, double maximum) =>
        value.HasValue &&
        !double.IsNaN(value.Value) &&
        !double.IsInfinity(value.Value) &&
        value.Value >= minimum &&
        value.Value <= maximum;

    private sealed record HardwareGroup(
        string Id,
        SolisHardwareKind Kind,
        string Name,
        IReadOnlyList<RawHardwareSensor> Sensors);
}
