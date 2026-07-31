#nullable enable

using System;
using System.Diagnostics;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using LibreHardwareMonitor.Solis.DeviceControl;
using LibreHardwareMonitor.Solis.Metrics;
using LibreHardwareMonitor.UI.WpfViews;

namespace LibreHardwareMonitor.UI;

internal sealed partial class SolisControlCenterControl
{
    private ElementHost? _wpfShellHost;
    private SolisControlCenterView? _wpfShell;

    private bool InitializeWpfShell(bool developerModeUnlocked)
    {
        try
        {
            _wpfShell = new SolisControlCenterView();
            _wpfShell.SetVersion(Application.ProductVersion);
            _wpfShell.SetDeveloperVisible(developerModeUnlocked);

            _wpfShell.DeviceWizardRequested += (_, _) =>
                _deviceSetupWizardRequested();
            _wpfShell.ClearPairingRequested += (_, _) =>
                _clearPairingRequested();
            _wpfShell.BrightnessChanged += (_, value) =>
            {
                if (_deviceDisplaySettings is null ||
                    !_deviceSettingsAvailable)
                {
                    return;
                }

                int brightness = Math.Max(
                    DeviceDisplaySettings.MinimumBrightness,
                    Math.Min(DeviceDisplaySettings.MaximumBrightness, value));
                if (_deviceDisplaySettings.BrightnessPercent == brightness)
                    return;

                _deviceDisplaySettings = _deviceDisplaySettings with
                {
                    BrightnessPercent = brightness
                };
                ScheduleInlineDeviceSettingsSave();
            };
            _wpfShell.NightBacklightRequested += async (_, _) =>
                await ShowNightBacklightSettingsAsync();
            _wpfShell.RestartDeviceRequested += async (_, _) =>
                await RestartDeviceAsync();
            _wpfShell.SilentStartupChanged += (_, enabled) =>
            {
                _startupSettings.SetSilentStartup(enabled);
                RefreshStartupSettings();
                RefreshWpfStartup();
            };
            _wpfShell.AutoStartChanged += (_, enabled) =>
            {
                _startupSettings.SetAutoStart(enabled);
                RefreshStartupSettings();
                RefreshWpfStartup();
            };
            _wpfShell.FirmwareSelectRequested += (_, _) =>
                SelectFirmwareClick(_wpfShell, EventArgs.Empty);
            _wpfShell.VersionClicked += (_, _) =>
                HandleWpfVersionClick();
            _wpfShell.DeveloperRequested += (_, _) =>
                DeveloperModeRequested?.Invoke(this, EventArgs.Empty);
            _wpfShell.DisableDeveloperRequested += (_, _) =>
            {
                SetDeveloperModeUnlocked(false);
                ShowPage(ControlCenterPage.Device);
            };

            _serviceWpfView = _wpfShell.ServiceView;
            _serviceWpfView.RestartRequested += (_, _) =>
                RestartDeviceApiFromWpf();
            _serviceWpfView.CopyDiagnosticsRequested += (_, _) =>
                CopyDiagnostics();
            _serviceWpfView.EditWeatherRequested += (_, _) =>
                ShowWeatherSettings();
            _serviceWpfView.OpenCodexRequested += (_, _) =>
                OpenCodexSessionsFolder();

            _wpfShellHost = new ElementHost
            {
                Child = _wpfShell,
                Dock = DockStyle.Fill,
                Margin = Padding.Empty
            };
            Controls.Add(_wpfShellHost);

            _wpfShellHost.BringToFront();
            return true;
        }
        catch (Exception exception)
        {
            Debug.WriteLine(
                $"Solis WPF shell initialization failed: {exception}");
            _wpfShellHost?.Dispose();
            _wpfShellHost = null;
            _wpfShell = null;
            return false;
        }
    }

    private void HandleWpfVersionClick()
    {
        if (_developerModeUnlocked)
            return;

        if (!_developerModeUnlockTracker.RegisterClick(DateTimeOffset.UtcNow))
            return;

        SetDeveloperModeUnlocked(true);
    }

    private void RefreshWpfShell(
        SolisMetricsSnapshot snapshot,
        DeviceDiscoveryState discovery,
        bool settingsAvailable,
        bool canControl)
    {
        if (_wpfShell is null)
            return;

        DeviceDisplaySettings? settings = _deviceDisplaySettings;
        string nightSummary = settings is null
            ? "连接设备后可设置"
            : settings.NightEnabled
                ? $"已启用 · {FormatMinute(settings.NightStartMinute)}–{FormatMinute(settings.NightEndMinute)}"
                : "未启用";
        int brightness = settings?.BrightnessPercent ?? 100;

        _wpfShell.UpdateDevice(new SolisDeviceViewState(
            _deviceStateText,
            _pairingStatusText,
            discovery.Device?.Paired == true,
            settingsAvailable,
            canControl,
            brightness,
            settings?.NightEnabled == true,
            nightSummary,
            _deviceRestartPending));
        RefreshWpfStartup();
        RefreshWpfFirmware();
    }

    private void RefreshWpfStartup()
    {
        if (_wpfShell is null)
            return;

        bool silent = _startupSettings.SilentStartup;
        bool autoStart = _startupSettings.AutoStart;
        _wpfShell.UpdateStartup(new SolisStartupViewState(
            silent,
            autoStart,
            _startupSettings.GetSummary()));
    }

    private void RefreshWpfFirmware()
    {
        if (_wpfShell is null)
            return;

        _wpfShell.UpdateFirmware(new SolisFirmwareViewState(
            _firmwareStatusText,
            _firmwareProgressValue,
            !_firmwareSelectEnabled));
    }
}
