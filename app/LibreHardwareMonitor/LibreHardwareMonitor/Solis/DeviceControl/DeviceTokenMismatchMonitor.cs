#nullable enable

using System;
using LibreHardwareMonitor.Solis.DeviceApi;

namespace LibreHardwareMonitor.Solis.DeviceControl;

public sealed class DeviceTokenMismatchMonitor
{
    private string? _notifiedAddress;

    public void Reset() => _notifiedAddress = null;

    public string? Observe(
        DeviceDiscoveryState discovery,
        DeviceAuthorizationState authorization)
    {
        if (discovery == null)
            throw new ArgumentNullException(nameof(discovery));
        if (authorization == null)
            throw new ArgumentNullException(nameof(authorization));

        DiscoveredDevice? device = discovery.Device;
        if (device is null ||
            !string.Equals(
                device.IpAddress,
                authorization.RemoteAddress,
                StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        if (authorization.IsAuthorized)
        {
            _notifiedAddress = null;
            return null;
        }

        if (string.Equals(
                _notifiedAddress,
                authorization.RemoteAddress,
                StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        _notifiedAddress = authorization.RemoteAddress;
        return device.HostName;
    }
}
