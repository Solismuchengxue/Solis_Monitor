#nullable enable

using System;

namespace LibreHardwareMonitor.Solis.Metrics;

public readonly record struct MetricReading(double? Value, bool Available)
{
    public static MetricReading Unavailable => new(null, false);

    public static MetricReading From(double? value) =>
        value is double number && !double.IsNaN(number) && !double.IsInfinity(number)
            ? new MetricReading(number, true)
            : Unavailable;
}
