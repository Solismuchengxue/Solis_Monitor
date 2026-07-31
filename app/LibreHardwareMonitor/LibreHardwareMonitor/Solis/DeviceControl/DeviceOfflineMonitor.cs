#nullable enable

using System;

namespace LibreHardwareMonitor.Solis.DeviceControl;

public sealed record DeviceOfflineNotification(
    string HostName,
    DateTimeOffset OfflineSince);

public sealed class DeviceOfflineMonitor
{
    private readonly TimeSpan _notificationDelay;
    private string? _lastHostName;
    private DateTimeOffset? _offlineSince;
    private DateTimeOffset? _maintenanceUntil;
    private bool _maintenanceObservedOffline;
    private bool _notificationSent;

    public DeviceOfflineMonitor(TimeSpan notificationDelay)
    {
        if (notificationDelay <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(notificationDelay));

        _notificationDelay = notificationDelay;
    }

    public void BeginMaintenance(
        DateTimeOffset now,
        TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(duration));

        _maintenanceUntil = now + duration;
        _maintenanceObservedOffline = false;
        _offlineSince = null;
        _notificationSent = false;
    }

    public bool IsMaintenanceActive(DateTimeOffset now) =>
        _maintenanceUntil.HasValue && now < _maintenanceUntil.Value;

    public void ForgetDevice()
    {
        _lastHostName = null;
        _offlineSince = null;
        _maintenanceUntil = null;
        _maintenanceObservedOffline = false;
        _notificationSent = false;
    }

    public DeviceOfflineNotification? Observe(
        DeviceDiscoveryState state,
        DateTimeOffset now,
        bool notificationsEnabled = true)
    {
        if (state == null)
            throw new ArgumentNullException(nameof(state));

        if (state.Device is not null)
        {
            _lastHostName = state.Device.HostName;
            if (_maintenanceObservedOffline)
            {
                _maintenanceUntil = null;
                _maintenanceObservedOffline = false;
            }
            _offlineSince = null;
            _notificationSent = false;
            return null;
        }

        if (IsMaintenanceActive(now))
        {
            _maintenanceObservedOffline = true;
            _offlineSince = null;
            _notificationSent = false;
            return null;
        }

        _maintenanceUntil = null;
        _maintenanceObservedOffline = false;

        if (!notificationsEnabled)
        {
            _offlineSince = null;
            _notificationSent = false;
            return null;
        }

        if (_lastHostName is null)
            return null;

        if (_offlineSince is null)
        {
            _offlineSince = now;
            return null;
        }

        if (_notificationSent || now - _offlineSince.Value < _notificationDelay)
            return null;

        _notificationSent = true;
        return new DeviceOfflineNotification(_lastHostName, _offlineSince.Value);
    }
}
