#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LibreHardwareMonitor.Solis.Codex;
using LibreHardwareMonitor.Solis.DeviceApi;
using LibreHardwareMonitor.Solis.DeviceControl;
using LibreHardwareMonitor.Solis.Diagnostics;
using LibreHardwareMonitor.Solis.Firmware;
using LibreHardwareMonitor.Solis.Hardware;
using LibreHardwareMonitor.Solis.Metrics;
using LibreHardwareMonitor.Solis.Network;
using LibreHardwareMonitor.Solis.Security;
using LibreHardwareMonitor.Solis.Weather;

namespace LibreHardwareMonitor.Solis;

public sealed class SolisRuntime : IDisposable
{
    private readonly object _lifecycleSync = new();
    private readonly CodexMetricsCollector _codexMetricsCollector;
    private readonly DeviceControlClient _deviceControlClient;
    private readonly DeviceDiscoveryService _deviceDiscoveryService;
    private readonly DeviceFirmwareUpdater _firmwareUpdater;
    private readonly DeviceMetricsServer _deviceMetricsServer;
    private readonly DeviceOfflineMonitor _deviceOfflineMonitor;
    private readonly DeviceTokenMismatchMonitor _deviceTokenMismatchMonitor;
    private readonly DeviceTokenStore _deviceTokenStore;
    private readonly BackgroundCollectionGuard _backgroundCollectionGuard;
    private readonly SolisDiagnosticsMonitor _diagnosticsMonitor;
    private readonly MetricsSnapshotStore _metricsSnapshotStore;
    private readonly NetworkThroughputCollector _networkThroughputCollector;
    private readonly WindowsNetworkCounterSource _networkCounterSource;
    private readonly WeatherFailureMonitor _weatherFailureMonitor;
    private readonly QWeatherSettingsStore _weatherSettingsStore;
    private QWeatherMetricsCollector _weatherMetricsCollector;
    private Timer? _metricsTimer;
    private Timer? _weatherTimer;
    private int _closing;
    private int _metricsUpdateRunning;
    private int _weatherUpdateRunning;
    private bool _started;

    public SolisRuntime(string? preferredNetworkInterfaceId = null)
        : this(
            preferredNetworkInterfaceId,
            null,
            null,
            "+",
            DeviceMetricsServer.DefaultPort)
    {
    }

    public SolisRuntime(
        string? preferredNetworkInterfaceId,
        string? settingsDirectory,
        string? codexRoot,
        string deviceApiListenerHost,
        int deviceApiListenerPort)
    {
        _metricsSnapshotStore = new MetricsSnapshotStore();
        _codexMetricsCollector = string.IsNullOrWhiteSpace(codexRoot)
            ? new CodexMetricsCollector(settingsDirectory)
            : new CodexMetricsCollector(
                codexRoot!,
                TimeSpan.FromMinutes(10));
        _deviceTokenStore = new DeviceTokenStore(settingsDirectory);
        var runtimeErrorLog = new RuntimeErrorLog(_deviceTokenStore.SettingsDirectory);
        _backgroundCollectionGuard = new BackgroundCollectionGuard(runtimeErrorLog.TryWrite);
        _firmwareUpdater = new DeviceFirmwareUpdater(_deviceTokenStore);
        _deviceControlClient = new DeviceControlClient(_deviceTokenStore);
        _diagnosticsMonitor = new SolisDiagnosticsMonitor();
        _weatherSettingsStore = new QWeatherSettingsStore(
            settingsDirectory);
        _weatherMetricsCollector = new QWeatherMetricsCollector(
            _weatherSettingsStore.Load());
        _weatherFailureMonitor = new WeatherFailureMonitor(
            TimeSpan.FromMinutes(30));
        _deviceDiscoveryService = new DeviceDiscoveryService(
            _deviceTokenStore);
        _deviceOfflineMonitor = new DeviceOfflineMonitor(
            TimeSpan.FromMinutes(2));
        _deviceTokenMismatchMonitor = new DeviceTokenMismatchMonitor();
        _deviceMetricsServer = new DeviceMetricsServer(
            () => _metricsSnapshotStore.Current,
            () => _deviceTokenStore.DeviceToken,
            deviceApiListenerHost,
            deviceApiListenerPort);
        _deviceMetricsServer.AuthorizationObserved +=
            OnDeviceAuthorizationObserved;
        _networkCounterSource = new WindowsNetworkCounterSource
        {
            PreferredInterfaceId = preferredNetworkInterfaceId ?? string.Empty
        };
        _networkThroughputCollector = new NetworkThroughputCollector(
            _networkCounterSource);
    }

    public event Action<DeviceAuthorizationState>? AuthorizationObserved;

    public event Action<string>? WeatherFailureObserved;

    public SolisMetricsSnapshot CurrentMetrics =>
        _metricsSnapshotStore.Current;

    public bool IsDeviceApiRunning => _deviceMetricsServer.IsRunning;

    public DateTimeOffset? LastDeviceCommunicationAt =>
        _deviceMetricsServer.LastSuccessfulCommunicationAt;

    public string DeviceApiErrorCategory =>
        _deviceMetricsServer.LastErrorCategory ?? string.Empty;

    public string DeviceTokenLastFour =>
        _deviceTokenStore.DeviceToken.Substring(DeviceToken.HexLength - 4);

    public DeviceDiscoveryState CurrentDevice =>
        _deviceDiscoveryService.Current;

    public IReadOnlyList<DiscoveredDevice> DiscoveryCandidates =>
        _deviceDiscoveryService.DiscoveryCandidates;

    public SolisDiagnosticsSnapshot Diagnostics =>
        _diagnosticsMonitor.Current;

    public string CodexSessionsRoot =>
        _codexMetricsCollector.SessionsRoot;

    public string SettingsDirectory =>
        _deviceTokenStore.SettingsDirectory;

    public string PreferredNetworkInterfaceId
    {
        get => _networkCounterSource.PreferredInterfaceId ?? string.Empty;
        set => _networkCounterSource.PreferredInterfaceId =
            value ?? string.Empty;
    }

    public QWeatherSettings WeatherSettings =>
        _weatherSettingsStore.Load();

    public void Start()
    {
        lock (_lifecycleSync)
        {
            if (Volatile.Read(ref _closing) != 0)
                throw new ObjectDisposedException(nameof(SolisRuntime));
            if (_started)
                return;

            _started = true;
            _deviceMetricsServer.Start();
            _deviceDiscoveryService.Start();
            _metricsTimer = new Timer(
                UpdateMetrics,
                null,
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(1));
            _weatherTimer = new Timer(
                UpdateWeather,
                null,
                TimeSpan.Zero,
                TimeSpan.FromMinutes(1));
        }
    }

    public void UpdateHardware(MappedHardwareMetrics hardwareMetrics) =>
        _metricsSnapshotStore.UpdateHardware(hardwareMetrics);

    public void ScanDevicesNow() =>
        _deviceDiscoveryService.ScanNow();

    public Task<bool> RefreshPairedDeviceAsync(
        CancellationToken cancellationToken = default) =>
        _deviceDiscoveryService.RefreshPairedDeviceAsync(cancellationToken);

    public Task<DevicePairingResult> PairDeviceAsync(
        DiscoveredDevice device,
        string code,
        CancellationToken cancellationToken = default) =>
        _deviceDiscoveryService.PairAsync(device, code, cancellationToken);

    public void BeginDeviceMaintenance(
        DateTimeOffset now,
        TimeSpan duration) =>
        _deviceOfflineMonitor.BeginMaintenance(now, duration);

    public void ClearPairing()
    {
        _deviceDiscoveryService.ClearPairing();
        _deviceOfflineMonitor.ForgetDevice();
        _deviceTokenMismatchMonitor.Reset();
    }

    public string? ObserveAuthorization(
        DeviceAuthorizationState authorization)
    {
        if (authorization.IsAuthorized &&
            authorization.RemoteAddress is not null)
        {
            _deviceDiscoveryService.ConfirmAuthorizedDevice(
                authorization.RemoteAddress);
        }

        return _deviceTokenMismatchMonitor.Observe(
            _deviceDiscoveryService.Current,
            authorization);
    }

    public DeviceOfflineNotification? ObserveDeviceStatus(
        DateTimeOffset now,
        bool notificationsEnabled)
    {
        SolisMetricsSnapshot snapshot = _metricsSnapshotStore.Current;
        bool metricsFresh = snapshot.GeneratedAtUnixSeconds > 0 &&
                            now.ToUnixTimeSeconds() -
                            snapshot.GeneratedAtUnixSeconds <= 5;
        DeviceDiscoveryState discovery = _deviceDiscoveryService.Current;
        _diagnosticsMonitor.ObserveDeviceApi(
            _deviceMetricsServer.IsRunning,
            metricsFresh,
            _deviceMetricsServer.LastErrorCategory,
            now);
        _diagnosticsMonitor.ObserveDevice(
            discovery,
            _deviceMetricsServer.AuthorizationState,
            now);
        return _deviceOfflineMonitor.Observe(
            discovery,
            now,
            notificationsEnabled);
    }

    public static WeatherMetricsReading TestWeatherSettings(
        QWeatherSettings settings)
    {
        using var collector = new QWeatherMetricsCollector(settings);
        return collector.Read(DateTimeOffset.UtcNow);
    }

    public void SaveWeatherSettings(
        QWeatherSettings settings,
        WeatherMetricsReading testedReading)
    {
        _weatherSettingsStore.Save(settings);
        var collector = new QWeatherMetricsCollector(settings);
        QWeatherMetricsCollector previous = Interlocked.Exchange(
            ref _weatherMetricsCollector,
            collector);
        previous.Dispose();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        _metricsSnapshotStore.UpdateWeather(testedReading);
        _diagnosticsMonitor.ObserveWeather(testedReading, now);
        _weatherFailureMonitor.Observe(testedReading, now);
    }

    public bool RestartDeviceApi()
    {
        _deviceMetricsServer.Stop();
        return _deviceMetricsServer.Start();
    }

    public Task<FirmwareUpdateResult> UpdateFirmwareAsync(
        string path,
        IProgress<FirmwareUpdateProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        _firmwareUpdater.UpdateAsync(path, progress, cancellationToken);

    public Task<DeviceControlResult> LoadDeviceSettingsAsync(
        CancellationToken cancellationToken = default) =>
        _deviceControlClient.LoadAsync(cancellationToken);

    public Task<DeviceControlResult> SaveDeviceSettingsAsync(
        DeviceDisplaySettings settings,
        CancellationToken cancellationToken = default) =>
        _deviceControlClient.SaveAsync(settings, cancellationToken);

    public Task<DeviceControlResult> RestartDeviceAsync(
        CancellationToken cancellationToken = default) =>
        _deviceControlClient.RestartAsync(cancellationToken);

    private void OnDeviceAuthorizationObserved(
        DeviceAuthorizationState authorization) =>
        AuthorizationObserved?.Invoke(authorization);

    private void UpdateMetrics(object? state)
    {
        if (Volatile.Read(ref _closing) != 0 ||
            Interlocked.Exchange(ref _metricsUpdateRunning, 1) != 0)
        {
            return;
        }

        try
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            _backgroundCollectionGuard.Execute(
                BackgroundCollectionModule.Metrics,
                now,
                () =>
                {
                    NetworkThroughputReading networkReading =
                        _networkThroughputCollector.Read(Stopwatch.GetTimestamp());
                    CodexMetricsReading codexReading = _codexMetricsCollector.Read(now);
                    if (Volatile.Read(ref _closing) != 0)
                        return;

                    _diagnosticsMonitor.ObserveCodex(codexReading, now);
                    _metricsSnapshotStore.Publish(networkReading, codexReading, now);
                },
                _ => { });
        }
        finally
        {
            Volatile.Write(ref _metricsUpdateRunning, 0);
        }
    }

    private void UpdateWeather(object? state)
    {
        if (Volatile.Read(ref _closing) != 0 ||
            Interlocked.Exchange(ref _weatherUpdateRunning, 1) != 0)
        {
            return;
        }

        try
        {
            QWeatherMetricsCollector collector =
                Volatile.Read(ref _weatherMetricsCollector);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            _backgroundCollectionGuard.Execute(
                BackgroundCollectionModule.Weather,
                now,
                () =>
                {
                    WeatherMetricsReading reading = collector.Read(now);
                    if (Volatile.Read(ref _closing) != 0 ||
                        !ReferenceEquals(
                            collector,
                            Volatile.Read(ref _weatherMetricsCollector)))
                    {
                        return;
                    }

                    _metricsSnapshotStore.UpdateWeather(reading);
                    _diagnosticsMonitor.ObserveWeather(reading, now);
                    WeatherFailureNotification? notification =
                        _weatherFailureMonitor.Observe(reading, now);
                    if (notification is not null)
                        WeatherFailureObserved?.Invoke(notification.Message);
                },
                _ =>
                {
                    if (Volatile.Read(ref _closing) == 0 &&
                        ReferenceEquals(
                            collector,
                            Volatile.Read(ref _weatherMetricsCollector)))
                    {
                        _diagnosticsMonitor.ObserveWeatherCollectionFailure(now);
                    }
                });
        }
        finally
        {
            Volatile.Write(ref _weatherUpdateRunning, 0);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _closing, 1) != 0)
            return;

        Timer? metricsTimer;
        Timer? weatherTimer;
        lock (_lifecycleSync)
        {
            metricsTimer = _metricsTimer;
            weatherTimer = _weatherTimer;
            _metricsTimer = null;
            _weatherTimer = null;
        }

        metricsTimer?.Dispose();
        weatherTimer?.Dispose();
        _deviceMetricsServer.AuthorizationObserved -=
            OnDeviceAuthorizationObserved;
        _weatherMetricsCollector.Dispose();
        _deviceDiscoveryService.Dispose();
        _firmwareUpdater.Dispose();
        _deviceControlClient.Dispose();
        _deviceMetricsServer.Dispose();
    }
}
