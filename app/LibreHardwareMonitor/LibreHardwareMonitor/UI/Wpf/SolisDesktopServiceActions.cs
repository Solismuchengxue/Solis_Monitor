#nullable enable

using System;
using LibreHardwareMonitor.Solis.DeviceControl;
using LibreHardwareMonitor.Solis.Diagnostics;

namespace LibreHardwareMonitor.UI.WpfViews;

public sealed class SolisDesktopServiceActions
{
    private readonly Func<SolisDiagnosticsSnapshot> _getDiagnostics;
    private readonly Func<string> _getApplicationVersion;
    private readonly Func<DateTimeOffset> _getCurrentTime;
    private readonly Func<string> _getCodexSessionsRoot;
    private readonly Action<string> _copyText;
    private readonly Action<string> _openDirectory;
    private readonly Action _editWeather;
    private readonly Action _showDeviceWizard;
    private readonly Action _showFirmwareUpdate;
    private readonly Func<string, string, bool>? _confirm;
    private readonly Action<string, string, bool>? _showResult;
    private readonly Func<DeviceDisplaySettings, DeviceDisplaySettings?>?
        _editNightBacklight;
    private readonly Action? _enterDeveloperMode;

    public SolisDesktopServiceActions(
        Func<SolisDiagnosticsSnapshot> getDiagnostics,
        Func<string> getApplicationVersion,
        Func<DateTimeOffset> getCurrentTime,
        Func<string> getCodexSessionsRoot,
        Action<string> copyText,
        Action<string> openDirectory,
        Action editWeather,
        Action showDeviceWizard,
        Action showFirmwareUpdate)
        : this(
            getDiagnostics,
            getApplicationVersion,
            getCurrentTime,
            getCodexSessionsRoot,
            copyText,
            openDirectory,
            editWeather,
            showDeviceWizard,
            showFirmwareUpdate,
            null,
            null,
            null,
            null)
    {
    }

    public SolisDesktopServiceActions(
        Func<SolisDiagnosticsSnapshot> getDiagnostics,
        Func<string> getApplicationVersion,
        Func<DateTimeOffset> getCurrentTime,
        Func<string> getCodexSessionsRoot,
        Action<string> copyText,
        Action<string> openDirectory,
        Action editWeather,
        Action showDeviceWizard,
        Action showFirmwareUpdate,
        Func<string, string, bool>? confirm = null,
        Action<string, string, bool>? showResult = null,
        Func<DeviceDisplaySettings, DeviceDisplaySettings?>?
            editNightBacklight = null,
        Action? enterDeveloperMode = null)
    {
        _getDiagnostics = getDiagnostics ??
            throw new ArgumentNullException(nameof(getDiagnostics));
        _getApplicationVersion = getApplicationVersion ??
            throw new ArgumentNullException(nameof(getApplicationVersion));
        _getCurrentTime = getCurrentTime ??
            throw new ArgumentNullException(nameof(getCurrentTime));
        _getCodexSessionsRoot = getCodexSessionsRoot ??
            throw new ArgumentNullException(nameof(getCodexSessionsRoot));
        _copyText = copyText ?? throw new ArgumentNullException(nameof(copyText));
        _openDirectory = openDirectory ??
            throw new ArgumentNullException(nameof(openDirectory));
        _editWeather = editWeather ??
            throw new ArgumentNullException(nameof(editWeather));
        _showDeviceWizard = showDeviceWizard ??
            throw new ArgumentNullException(nameof(showDeviceWizard));
        _showFirmwareUpdate = showFirmwareUpdate ??
            throw new ArgumentNullException(nameof(showFirmwareUpdate));
        _confirm = confirm;
        _showResult = showResult;
        _editNightBacklight = editNightBacklight;
        _enterDeveloperMode = enterDeveloperMode;
    }

    public string ApplicationVersion => _getApplicationVersion();

    public void CopyDiagnostics()
    {
        string report = SolisDiagnosticsReport.Create(
            _getDiagnostics(),
            _getApplicationVersion(),
            _getCurrentTime());
        _copyText(report);
    }

    public void OpenCodexSessions() =>
        _openDirectory(_getCodexSessionsRoot());

    public void EditWeather() => _editWeather();

    public void ShowDeviceWizard() => _showDeviceWizard();

    public void ShowFirmwareUpdate() => _showFirmwareUpdate();

    public bool Confirm(string title, string message) =>
        _confirm?.Invoke(title, message) == true;

    public void ShowResult(string title, string message, bool success) =>
        _showResult?.Invoke(title, message, success);

    public DeviceDisplaySettings? EditNightBacklight(
        DeviceDisplaySettings settings) =>
        _editNightBacklight?.Invoke(settings);

    public void EnterDeveloperMode() => _enterDeveloperMode?.Invoke();
}
