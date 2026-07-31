#nullable enable

namespace LibreHardwareMonitor.Solis.Network;

public sealed record NetworkCounterSnapshot(
    string InterfaceId,
    string InterfaceName,
    long BytesReceived,
    long BytesSent,
    long SpeedBitsPerSecond);

public readonly record struct NetworkCounterReadResult(
    NetworkCounterSnapshot? Snapshot,
    string? ErrorCategory);

public interface INetworkCounterSource
{
    NetworkCounterReadResult ReadSelected();
}

public readonly record struct NetworkThroughputReading(
    bool Available,
    double? DownloadMbps,
    double? UploadMbps,
    string? InterfaceId,
    string? InterfaceName,
    string? ErrorCategory);
