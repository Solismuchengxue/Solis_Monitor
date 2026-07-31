#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using LibreHardwareMonitor.Solis;
using LibreHardwareMonitor.Solis.Desktop;
using LibreHardwareMonitor.Solis.DeviceApi;
using LibreHardwareMonitor.Solis.DeviceControl;
using LibreHardwareMonitor.Solis.Diagnostics;
using LibreHardwareMonitor.Solis.Startup;

namespace LibreHardwareMonitor.UI.WpfViews;

public sealed class SolisDesktopPresenter : IDisposable
{
    private readonly SolisRuntime _runtime;
    private readonly DesktopStartupSettingsController _startupSettings;
    private readonly SolisDesktopServiceActions _serviceActions;
    private readonly SolisControlCenterView _view;
    private readonly DeveloperModeUnlockTracker _developerUnlockTracker = new();
    private CancellationTokenSource? _brightnessSaveCancellation;
    private DeviceDisplaySettings? _deviceSettings;
    private string? _settingsDeviceIp;
    private bool _settingsLoading;
    private bool _restartPending;
    private bool _disposed;

    public SolisDesktopPresenter(
        SolisRuntime runtime,
        SolisControlCenterView view,
        DesktopStartupSettingsController startupSettings,
        SolisDesktopServiceActions serviceActions)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _view = view ?? throw new ArgumentNullException(nameof(view));
        _startupSettings = startupSettings ??
            throw new ArgumentNullException(nameof(startupSettings));
        _serviceActions = serviceActions ??
            throw new ArgumentNullException(nameof(serviceActions));

        _view.SilentStartupChanged += OnSilentStartupChanged;
        _view.AutoStartChanged += OnAutoStartChanged;
        _view.DeviceWizardRequested += OnDeviceWizardRequested;
        _view.ClearPairingRequested += OnClearPairingRequested;
        _view.BrightnessChanged += OnBrightnessChanged;
        _view.NightBacklightRequested += OnNightBacklightRequested;
        _view.RestartDeviceRequested += OnRestartDeviceRequested;
        _view.FirmwareSelectRequested += OnFirmwareSelectRequested;
        _view.VersionClicked += OnVersionClicked;
        _view.DeveloperRequested += OnDeveloperRequested;
        _view.DisableDeveloperRequested += OnDisableDeveloperRequested;
        _view.ServiceView.RestartRequested += OnRestartRequested;
        _view.ServiceView.CopyDiagnosticsRequested +=
            OnCopyDiagnosticsRequested;
        _view.ServiceView.OpenCodexRequested += OnOpenCodexRequested;
        _view.ServiceView.EditWeatherRequested += OnEditWeatherRequested;
        _view.SetVersion(_serviceActions.ApplicationVersion);
        _view.SetDeveloperVisible(false);
    }

    public void Refresh()
    {
        ThrowIfDisposed();
        _runtime.ObserveDeviceStatus(DateTimeOffset.Now, false);
        UpdateDevice();
        UpdateStartup();
        UpdateService();
        EnsureDeviceSettingsLoaded();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _view.SilentStartupChanged -= OnSilentStartupChanged;
        _view.AutoStartChanged -= OnAutoStartChanged;
        _view.DeviceWizardRequested -= OnDeviceWizardRequested;
        _view.ClearPairingRequested -= OnClearPairingRequested;
        _view.BrightnessChanged -= OnBrightnessChanged;
        _view.NightBacklightRequested -= OnNightBacklightRequested;
        _view.RestartDeviceRequested -= OnRestartDeviceRequested;
        _view.FirmwareSelectRequested -= OnFirmwareSelectRequested;
        _view.VersionClicked -= OnVersionClicked;
        _view.DeveloperRequested -= OnDeveloperRequested;
        _view.DisableDeveloperRequested -= OnDisableDeveloperRequested;
        _view.ServiceView.RestartRequested -= OnRestartRequested;
        _view.ServiceView.CopyDiagnosticsRequested -=
            OnCopyDiagnosticsRequested;
        _view.ServiceView.OpenCodexRequested -= OnOpenCodexRequested;
        _view.ServiceView.EditWeatherRequested -= OnEditWeatherRequested;
        _brightnessSaveCancellation?.Cancel();
        _brightnessSaveCancellation?.Dispose();
        _disposed = true;
    }

    private void UpdateDevice()
    {
        DeviceDiscoveryState discovery = _runtime.CurrentDevice;
        DiscoveredDevice? device = discovery.Device;
        string deviceState = device is null
            ? discovery.IsScanning
                ? "正在扫描副屏"
                : "尚未发现副屏"
            : BuildDeviceDetail(device);
        string pairingStatus = device is null
            ? "尚未配对"
            : device.Paired
                ? "已连接 · 已配对"
                : "已发现 · 未配对";
        bool settingsAvailable = device?.Paired == true &&
                                 _deviceSettings is not null;
        DeviceDisplaySettings settings = _deviceSettings ??
            new DeviceDisplaySettings(
                DeviceDisplaySettings.MaximumBrightness,
                false,
                0,
                0,
                0);

        _view.UpdateDevice(new SolisDeviceViewState(
            deviceState,
            pairingStatus,
            device?.Paired == true,
            settingsAvailable,
            device?.Paired == true && !_restartPending,
            settings.BrightnessPercent,
            settings.NightEnabled,
            settingsAvailable
                ? BuildNightSummary(settings)
                : device?.Paired == true
                    ? "正在读取设置"
                    : "连接设备后可用",
            _restartPending));
    }

    private void UpdateStartup()
    {
        _view.UpdateStartup(new SolisStartupViewState(
            _startupSettings.SilentStartup,
            _startupSettings.AutoStart,
            _startupSettings.GetSummary()));
    }

    private void UpdateService()
    {
        SolisDiagnosticsSnapshot diagnostics = _runtime.Diagnostics;
        DiagnosticCheckState overallState = GetOverallState(diagnostics);
        string overallStatus = overallState switch
        {
            DiagnosticCheckState.Normal => "运行正常",
            DiagnosticCheckState.Fault => "发现问题",
            _ => "正在检查"
        };
        DiscoveredDevice? device = _runtime.CurrentDevice.Device;
        string? currentWeatherLocation =
            _runtime.CurrentMetrics.Weather.Location;
        string weatherDetail = !_runtime.WeatherSettings.Enabled
            ? "尚未配置天气服务"
            : string.IsNullOrWhiteSpace(currentWeatherLocation)
                ? "正在获取位置"
                : currentWeatherLocation;

        _view.ServiceView.UpdateState(new SolisServiceViewState(
            overallStatus,
            overallState,
            FormatLastCommunication(_runtime.LastDeviceCommunicationAt),
            $"进程 {Environment.ProcessId}",
            diagnostics.DeviceApi.Status,
            diagnostics.DeviceApi.State,
            $"端口 {DeviceMetricsServer.DefaultPort}",
            diagnostics.Device.Status,
            diagnostics.Device.State,
            device is null ? "等待设备信息" : BuildDeviceDetail(device),
            diagnostics.Codex.Status,
            diagnostics.Codex.State,
            _runtime.CodexSessionsRoot,
            diagnostics.Weather.Status,
            diagnostics.Weather.State,
            weatherDetail));
    }

    private void OnSilentStartupChanged(object? sender, bool enabled)
    {
        _startupSettings.SetSilentStartup(enabled);
        UpdateStartup();
    }

    private void OnAutoStartChanged(object? sender, bool enabled)
    {
        _startupSettings.SetAutoStart(enabled);
        UpdateStartup();
    }

    private void OnDeviceWizardRequested(
        object? sender,
        EventArgs eventArgs) =>
        _serviceActions.ShowDeviceWizard();

    private void OnClearPairingRequested(object? sender, EventArgs eventArgs)
    {
        if (!_serviceActions.Confirm(
                "清除配对",
                "确定清除 PC 本地的设备记录和配对令牌吗？重新连接需要再次输入 6 位配对码。"))
        {
            return;
        }

        _runtime.ClearPairing();
        ResetDeviceSettings();
        _runtime.ObserveDeviceStatus(DateTimeOffset.Now, false);
        UpdateDevice();
        UpdateService();
        _serviceActions.ShowResult(
            "清除配对",
            "PC 本地配对记录和设备令牌已清除。",
            true);
    }

    private void OnBrightnessChanged(object? sender, int brightness)
    {
        if (_deviceSettings is null)
            return;

        _deviceSettings = _deviceSettings with
        {
            BrightnessPercent = brightness
        };
        UpdateDevice();

        _brightnessSaveCancellation?.Cancel();
        _brightnessSaveCancellation?.Dispose();
        _brightnessSaveCancellation = new CancellationTokenSource();
        _ = SaveBrightnessAfterDelayAsync(
            _deviceSettings,
            _brightnessSaveCancellation.Token);
    }

    private void OnNightBacklightRequested(
        object? sender,
        EventArgs eventArgs)
    {
        if (_deviceSettings is null)
            return;

        DeviceDisplaySettings? edited =
            _serviceActions.EditNightBacklight(_deviceSettings);
        if (edited is null)
            return;

        _ = SaveNightBacklightAsync(edited);
    }

    private void OnRestartDeviceRequested(
        object? sender,
        EventArgs eventArgs)
    {
        if (_restartPending ||
            !_serviceActions.Confirm(
                "远程重启",
                "确定要重新启动副屏吗？PC 后台服务不会受到影响。"))
        {
            return;
        }

        _ = RestartDeviceAsync();
    }

    private void OnFirmwareSelectRequested(
        object? sender,
        EventArgs eventArgs) =>
        _serviceActions.ShowFirmwareUpdate();

    private void OnVersionClicked(object? sender, EventArgs eventArgs)
    {
        if (_developerUnlockTracker.RegisterClick(DateTimeOffset.Now))
            _view.SetDeveloperVisible(true);
    }

    private void OnDeveloperRequested(object? sender, EventArgs eventArgs) =>
        _serviceActions.EnterDeveloperMode();

    private void OnDisableDeveloperRequested(
        object? sender,
        EventArgs eventArgs)
    {
        _developerUnlockTracker.Reset();
        _view.SetDeveloperVisible(false);
    }

    private void OnRestartRequested(object? sender, EventArgs eventArgs)
    {
        _view.ServiceView.SetRestartBusy(true);
        try
        {
            bool restarted = _runtime.RestartDeviceApi();
            _runtime.ObserveDeviceStatus(DateTimeOffset.Now, false);
            _serviceActions.ShowResult(
                "重启服务",
                restarted
                    ? $"设备 API 已重新启动，端口 {DeviceMetricsServer.DefaultPort} 正在监听。"
                    : "设备 API 重启失败，请复制诊断信息查看具体原因。",
                restarted);
        }
        finally
        {
            _view.ServiceView.SetRestartBusy(false);
            UpdateService();
        }
    }

    private void OnCopyDiagnosticsRequested(
        object? sender,
        EventArgs eventArgs) =>
        _serviceActions.CopyDiagnostics();

    private void OnOpenCodexRequested(
        object? sender,
        EventArgs eventArgs) =>
        _serviceActions.OpenCodexSessions();

    private void OnEditWeatherRequested(
        object? sender,
        EventArgs eventArgs) =>
        _serviceActions.EditWeather();

    private void EnsureDeviceSettingsLoaded()
    {
        DiscoveredDevice? device = _runtime.CurrentDevice.Device;
        if (device?.Paired != true)
        {
            ResetDeviceSettings();
            return;
        }

        if (!string.Equals(
                _settingsDeviceIp,
                device.IpAddress,
                StringComparison.Ordinal))
        {
            _settingsDeviceIp = device.IpAddress;
            _deviceSettings = null;
            _settingsLoading = false;
        }

        if (_deviceSettings is not null || _settingsLoading)
            return;

        _settingsLoading = true;
        _ = LoadDeviceSettingsAsync(device.IpAddress);
    }

    private async Task LoadDeviceSettingsAsync(string deviceIp)
    {
        try
        {
            DeviceControlResult result =
                await _runtime.LoadDeviceSettingsAsync();
            if (_disposed ||
                !string.Equals(
                    _settingsDeviceIp,
                    deviceIp,
                    StringComparison.Ordinal))
            {
                return;
            }

            if (result.Success && result.Settings is not null)
                _deviceSettings = result.Settings;
        }
        finally
        {
            _settingsLoading = false;
            if (!_disposed)
                UpdateDevice();
        }
    }

    private async Task SaveBrightnessAfterDelayAsync(
        DeviceDisplaySettings settings,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(350), cancellationToken);
            DeviceControlResult result =
                await _runtime.SaveDeviceSettingsAsync(
                    settings,
                    cancellationToken);
            if (!result.Success && !cancellationToken.IsCancellationRequested)
            {
                _serviceActions.ShowResult(
                    "亮度",
                    result.Message,
                    false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task SaveNightBacklightAsync(
        DeviceDisplaySettings settings)
    {
        DeviceControlResult result =
            await _runtime.SaveDeviceSettingsAsync(settings);
        if (result.Success)
        {
            _deviceSettings = settings;
            UpdateDevice();
        }

        _serviceActions.ShowResult(
            "夜间背光",
            result.Success
                ? settings.NightEnabled
                    ? $"夜间背光已启用：{FormatMinute(settings.NightStartMinute)}–{FormatMinute(settings.NightEndMinute)}。"
                    : "夜间背光已关闭。"
                : result.Message,
            result.Success);
    }

    private async Task RestartDeviceAsync()
    {
        _restartPending = true;
        UpdateDevice();
        DeviceControlResult result;
        try
        {
            result = await _runtime.RestartDeviceAsync();
        }
        finally
        {
            _restartPending = false;
            UpdateDevice();
        }

        _serviceActions.ShowResult(
            "远程重启",
            result.Success ? "副屏正在重新启动。" : result.Message,
            result.Success);

        if (result.Success)
            _ = RefreshRestartedDeviceAsync();
    }

    private async Task RefreshRestartedDeviceAsync()
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            await Task.Delay(TimeSpan.FromSeconds(1));
            if (_disposed)
                return;

            try
            {
                if (await _runtime.RefreshPairedDeviceAsync())
                {
                    UpdateDevice();
                    return;
                }
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private void ResetDeviceSettings()
    {
        _settingsDeviceIp = null;
        _deviceSettings = null;
        _settingsLoading = false;
    }

    private static string BuildNightSummary(DeviceDisplaySettings settings) =>
        settings.NightEnabled
            ? $"已启用 · {FormatMinute(settings.NightStartMinute)}–{FormatMinute(settings.NightEndMinute)}"
            : "未启用";

    private static string FormatMinute(int minuteOfDay) =>
        $"{minuteOfDay / 60:00}:{minuteOfDay % 60:00}";

    private static string BuildDeviceDetail(DiscoveredDevice device)
    {
        string rssi = device.Rssi.HasValue
            ? $" · {device.Rssi.Value} dBm"
            : string.Empty;
        return $"{device.HostName}\n{device.IpAddress}{rssi}\n" +
            $"固件 {device.FirmwareVersion}";
    }

    private static DiagnosticCheckState GetOverallState(
        SolisDiagnosticsSnapshot diagnostics)
    {
        if (diagnostics.CurrentFault != DiagnosticSource.None)
            return DiagnosticCheckState.Fault;

        return diagnostics.DeviceApi.State == DiagnosticCheckState.Checking ||
               diagnostics.Device.State == DiagnosticCheckState.Checking ||
               diagnostics.Codex.State == DiagnosticCheckState.Checking ||
               diagnostics.Weather.State == DiagnosticCheckState.Checking
            ? DiagnosticCheckState.Checking
            : DiagnosticCheckState.Normal;
    }

    private static string FormatLastCommunication(DateTimeOffset? value)
    {
        if (!value.HasValue)
            return "尚未通信";

        TimeSpan elapsed = DateTimeOffset.UtcNow - value.Value.ToUniversalTime();
        if (elapsed < TimeSpan.Zero || elapsed < TimeSpan.FromSeconds(10))
            return "刚刚";
        if (elapsed < TimeSpan.FromMinutes(1))
            return $"{Math.Max(1, (int)elapsed.TotalSeconds)} 秒前";
        if (elapsed < TimeSpan.FromHours(1))
            return $"{Math.Max(1, (int)elapsed.TotalMinutes)} 分钟前";

        return value.Value.ToLocalTime().ToString("HH:mm:ss");
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SolisDesktopPresenter));
    }
}
