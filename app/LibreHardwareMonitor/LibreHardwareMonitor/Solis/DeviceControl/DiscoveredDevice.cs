#nullable enable

namespace LibreHardwareMonitor.Solis.DeviceControl;

public sealed record DiscoveredDevice(
    string HostName,
    string FirmwareVersion,
    string IpAddress,
    int? Rssi,
    bool Paired,
    bool PairingActive = false);

public sealed record DeviceDiscoveryState(
    DiscoveredDevice? Device,
    bool IsScanning,
    string? ErrorCategory);

public sealed record DevicePairingResult(
    bool Success,
    string? ErrorMessage = null);
