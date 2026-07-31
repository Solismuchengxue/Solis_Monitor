#nullable enable

using System;
using System.Collections.Generic;
using LibreHardwareMonitor.Hardware;

namespace LibreHardwareMonitor.Solis.Hardware;

public static class InProcessHardwareSnapshotReader
{
    public static HardwareSnapshot Read(IComputer computer, DateTimeOffset capturedAt)
    {
        if (computer is null)
            throw new ArgumentNullException(nameof(computer));

        var sensors = new List<RawHardwareSensor>();
        var errors = new List<string>();

        foreach (IHardware hardware in computer.Hardware)
            CollectRecursively(hardware, sensors, errors);

        return new HardwareSnapshot(capturedAt, sensors, errors);
    }

    private static void CollectRecursively(
        IHardware hardware,
        List<RawHardwareSensor> sensors,
        List<string> errors)
    {
        try
        {
            SolisHardwareKind hardwareKind = Map(hardware.HardwareType);
            foreach (ISensor sensor in hardware.Sensors)
            {
                SolisSensorKind sensorKind = Map(sensor.SensorType);
                if (sensorKind == SolisSensorKind.Unknown)
                    continue;

                sensors.Add(new RawHardwareSensor(
                    hardwareKind,
                    hardware.Identifier.ToString(),
                    hardware.Name,
                    sensorKind,
                    sensor.Name,
                    Normalize(sensor.Value)));
            }
        }
        catch (Exception exception)
        {
            errors.Add(exception.GetType().Name);
        }

        try
        {
            foreach (IHardware subHardware in hardware.SubHardware)
                CollectRecursively(subHardware, sensors, errors);
        }
        catch (Exception exception)
        {
            errors.Add(exception.GetType().Name);
        }
    }

    internal static SolisHardwareKind Map(HardwareType input) => input switch
    {
        HardwareType.Cpu => SolisHardwareKind.Cpu,
        HardwareType.GpuNvidia => SolisHardwareKind.GpuNvidia,
        HardwareType.GpuAmd => SolisHardwareKind.GpuAmd,
        HardwareType.GpuIntel => SolisHardwareKind.GpuIntel,
        HardwareType.Memory => SolisHardwareKind.Memory,
        HardwareType.Storage => SolisHardwareKind.Storage,
        _ => SolisHardwareKind.Unknown
    };

    internal static SolisSensorKind Map(SensorType input) => input switch
    {
        SensorType.Load => SolisSensorKind.Load,
        SensorType.Temperature => SolisSensorKind.Temperature,
        SensorType.Clock => SolisSensorKind.Clock,
        SensorType.Power => SolisSensorKind.Power,
        SensorType.Data => SolisSensorKind.Data,
        SensorType.SmallData => SolisSensorKind.SmallData,
        SensorType.Factor => SolisSensorKind.Factor,
        _ => SolisSensorKind.Unknown
    };

    internal static double? Normalize(float? input)
    {
        if (!input.HasValue || float.IsNaN(input.Value) || float.IsInfinity(input.Value))
            return null;

        return input.Value;
    }
}
