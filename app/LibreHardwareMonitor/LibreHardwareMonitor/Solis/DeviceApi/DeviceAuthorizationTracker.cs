#nullable enable

using System;
using System.Net;

namespace LibreHardwareMonitor.Solis.DeviceApi;

public sealed record DeviceAuthorizationState(
    string? RemoteAddress,
    bool IsAuthorized,
    DateTimeOffset ObservedAt);

public sealed class DeviceAuthorizationTracker
{
    private readonly object _sync = new();
    private DeviceAuthorizationState _current = new(null, true, DateTimeOffset.MinValue);

    public DeviceAuthorizationState Current
    {
        get
        {
            lock (_sync)
                return _current;
        }
    }

    public void Observe(IPAddress remoteAddress, bool isAuthorized, DateTimeOffset now)
    {
        if (remoteAddress == null)
            throw new ArgumentNullException(nameof(remoteAddress));

        IPAddress normalized = remoteAddress.IsIPv4MappedToIPv6
            ? remoteAddress.MapToIPv4()
            : remoteAddress;
        lock (_sync)
            _current = new DeviceAuthorizationState(normalized.ToString(), isAuthorized, now);
    }
}
