#nullable enable

using System;
using System.Diagnostics;

namespace LibreHardwareMonitor.Solis.Network;

public sealed class NetworkThroughputCollector(INetworkCounterSource source)
{
    private const double BitsPerMegabit = 1_000_000D;
    private const double MaximumFallbackMbps = 10_000D;
    private const double MaximumLinkSpeedMultiplier = 1.2D;
    private NetworkCounterSnapshot? _baseline;
    private long _baselineTimestamp;

    public NetworkThroughputReading Read(long timestamp)
    {
        NetworkCounterReadResult sourceResult = source.ReadSelected();
        NetworkCounterSnapshot? current = sourceResult.Snapshot;
        if (current is null)
        {
            ClearBaseline();
            return Unavailable(null, null, sourceResult.ErrorCategory ?? "NoEligibleInterface");
        }

        if (_baseline is null)
        {
            SetBaseline(current, timestamp);
            return Unavailable(current.InterfaceId, current.InterfaceName, "FirstSample");
        }

        if (!string.Equals(current.InterfaceId, _baseline.InterfaceId, StringComparison.OrdinalIgnoreCase))
        {
            SetBaseline(current, timestamp);
            return Unavailable(current.InterfaceId, current.InterfaceName, "InterfaceChanged");
        }

        if (timestamp <= _baselineTimestamp)
        {
            SetBaseline(current, timestamp);
            return Unavailable(current.InterfaceId, current.InterfaceName, "NonIncreasingTimestamp");
        }

        if (current.BytesReceived < _baseline.BytesReceived || current.BytesSent < _baseline.BytesSent)
        {
            SetBaseline(current, timestamp);
            return Unavailable(current.InterfaceId, current.InterfaceName, "CounterReset");
        }

        long receivedDelta = current.BytesReceived - _baseline.BytesReceived;
        long sentDelta = current.BytesSent - _baseline.BytesSent;
        double elapsedSeconds = (timestamp - _baselineTimestamp) / (double)Stopwatch.Frequency;
        double downloadMbps = receivedDelta * 8D / elapsedSeconds / BitsPerMegabit;
        double uploadMbps = sentDelta * 8D / elapsedSeconds / BitsPerMegabit;

        if (!IsPlausible(downloadMbps, current.SpeedBitsPerSecond) ||
            !IsPlausible(uploadMbps, current.SpeedBitsPerSecond))
        {
            SetBaseline(current, timestamp);
            return Unavailable(current.InterfaceId, current.InterfaceName, "ImplausibleRate");
        }

        SetBaseline(current, timestamp);
        return new NetworkThroughputReading(
            true,
            downloadMbps,
            uploadMbps,
            current.InterfaceId,
            current.InterfaceName,
            null);
    }

    private static bool IsPlausible(double megabitsPerSecond, long speedBitsPerSecond)
    {
        if (double.IsNaN(megabitsPerSecond) || double.IsInfinity(megabitsPerSecond))
            return false;

        double maximumMbps = speedBitsPerSecond > 0
            ? speedBitsPerSecond / BitsPerMegabit * MaximumLinkSpeedMultiplier
            : MaximumFallbackMbps;
        return megabitsPerSecond <= maximumMbps;
    }

    private void SetBaseline(NetworkCounterSnapshot snapshot, long timestamp)
    {
        _baseline = snapshot;
        _baselineTimestamp = timestamp;
    }

    private void ClearBaseline()
    {
        _baseline = null;
        _baselineTimestamp = 0;
    }

    private static NetworkThroughputReading Unavailable(
        string? interfaceId,
        string? interfaceName,
        string errorCategory) =>
        new(false, null, null, interfaceId, interfaceName, errorCategory);
}
