#nullable enable

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using LibreHardwareMonitor.Solis.Firmware;
using LibreHardwareMonitor.Solis.DeviceControl;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using LibreHardwareMonitor.Solis;
using LibreHardwareMonitor.Solis.Desktop;
using LibreHardwareMonitor.Solis.Settings;
using LibreHardwareMonitor.UI;

namespace LibreHardwareMonitor.UI.WpfViews;

public sealed class SolisWpfDesktopHost : IDisposable
{
    private readonly SolisDesktopBackend _backend;
    private readonly SolisControlCenterView _view;
    private readonly SolisMainWindow _window;
    private readonly SolisTaskbarHost _taskbar;
    private readonly SolisDesktopPresenter _presenter;
    private readonly DesktopStartupSettingsController _startupSettings;
    private readonly DispatcherTimer _refreshTimer;
    private bool _disposed;
    private bool _exitRequested;

    public SolisWpfDesktopHost()
        : this(null)
    {
    }

    public SolisWpfDesktopHost(Action? enterDeveloperMode)
        : this(
            new SolisDesktopBackend(),
            new StartupManager(),
            enterDeveloperMode)
    {
    }

    private SolisWpfDesktopHost(
        SolisDesktopBackend backend,
        StartupManager startupManager,
        Action? enterDeveloperMode)
        : this(
            backend,
            () => backend.Settings.GetValue("startMinMenuItem", false),
            value =>
            {
                backend.Settings.SetValue("startMinMenuItem", value);
                backend.Save();
            },
            () => startupManager.Startup,
            value =>
            {
                startupManager.Startup = value;
                startupManager.EnsureStartupArguments();
            },
            enterDeveloperMode)
    {
        startupManager.EnsureStartupArguments();
    }

    public SolisWpfDesktopHost(
        Func<bool> getSilentStartup,
        Action<bool> setSilentStartup,
        Func<bool> getAutoStart,
        Action<bool> setAutoStart)
        : this(
            new SolisDesktopBackend(),
            getSilentStartup,
            setSilentStartup,
            getAutoStart,
            setAutoStart,
            null)
    {
    }

    private SolisWpfDesktopHost(
        SolisDesktopBackend backend,
        Func<bool> getSilentStartup,
        Action<bool> setSilentStartup,
        Func<bool> getAutoStart,
        Action<bool> setAutoStart,
        Action? enterDeveloperMode)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _view = new SolisControlCenterView();
        _window = new SolisMainWindow(_view);
        _startupSettings = new DesktopStartupSettingsController(
            getSilentStartup,
            setSilentStartup,
            getAutoStart,
            setAutoStart);
        var serviceActions = new SolisDesktopServiceActions(
            () => _backend.Runtime.Diagnostics,
            GetApplicationVersion,
            () => DateTimeOffset.Now,
            () => _backend.Runtime.CodexSessionsRoot,
            CopyText,
            OpenDirectory,
            ShowWeatherSettings,
            ShowDeviceWizard,
            ShowFirmwareUpdate,
            Confirm,
            ShowResult,
            ShowNightBacklightSettings,
            enterDeveloperMode);
        _presenter = new SolisDesktopPresenter(
            _backend.Runtime,
            _view,
            _startupSettings,
            serviceActions);
        _taskbar = new SolisTaskbarHost(
            ShowWindow,
            getSilentStartup,
            setSilentStartup,
            getAutoStart,
            setAutoStart,
            RequestExit,
            () => DeviceTrayPresentation.From(_backend.Runtime.CurrentDevice),
            OpenDeviceWebUi);
        _refreshTimer = new DispatcherTimer(
            TimeSpan.FromSeconds(1),
            DispatcherPriority.Background,
            (_, _) => _presenter.Refresh(),
            _window.Dispatcher);
        _refreshTimer.Stop();

        _window.Closing += OnWindowClosing;
        _window.StateChanged += OnWindowStateChanged;
    }

    public bool SilentStartup => _startupSettings.SilentStartup;

    public SolisRuntime Runtime => _backend.Runtime;

    public void Start(bool showWindow)
    {
        ThrowIfDisposed();
        _backend.Start();
        _presenter.Refresh();
        _refreshTimer.Start();

        if (showWindow)
            ShowWindow();
    }

    public void ShowWindow()
    {
        ThrowIfDisposed();
        if (!_window.IsVisible)
            _window.Show();
        if (_window.WindowState == WindowState.Minimized)
            _window.WindowState = WindowState.Normal;

        _window.Activate();
        _window.Topmost = true;
        _window.Topmost = false;
        _window.Focus();
    }

    public void ShowServicePage()
    {
        ThrowIfDisposed();
        _view.ShowPage("Service");
        ShowWindow();
    }

    public void ShowDevicePage()
    {
        ThrowIfDisposed();
        _view.ShowPage("Device");
        ShowWindow();
    }

    public bool ConsumeDevicePageAfterReset()
    {
        ThrowIfDisposed();
        return SolisSettingsResetter.ConsumeDevicePageAfterRestart(
            _backend.Settings);
    }

    public void RequestExit()
    {
        if (_disposed)
            return;

        _exitRequested = true;
        _refreshTimer.Stop();
        _window.Close();
        Application.Current?.Shutdown();
    }

    public void CloseForHostSwitch(bool preserveRuntime)
    {
        if (_disposed)
            return;

        _exitRequested = true;
        _refreshTimer.Stop();
        _window.Close();
        if (preserveRuntime)
            _backend.ReleaseRuntimeOwnership();
        Dispose();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _exitRequested = true;
        _refreshTimer.Stop();
        _window.Closing -= OnWindowClosing;
        _window.StateChanged -= OnWindowStateChanged;
        _presenter.Dispose();
        _taskbar.Dispose();
        _backend.Dispose();
        _disposed = true;
    }

    private void OnWindowClosing(object? sender, CancelEventArgs eventArgs)
    {
        if (_exitRequested)
            return;

        eventArgs.Cancel = true;
        _window.Hide();
    }

    private void OnWindowStateChanged(object? sender, EventArgs eventArgs)
    {
        if (_window.WindowState == WindowState.Minimized)
            _window.Hide();
    }

    private static string GetApplicationVersion()
    {
        FileVersionInfo versionInfo = FileVersionInfo.GetVersionInfo(
            typeof(SolisWpfDesktopHost).Assembly.Location);
        return versionInfo.ProductVersion ?? "未知";
    }

    private void CopyText(string text)
    {
        try
        {
            Clipboard.SetText(text);
            MessageBox.Show(
                _window,
                "诊断信息已复制，敏感配置未包含。",
                "Solis Monitor",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (ExternalException)
        {
            MessageBox.Show(
                _window,
                "剪贴板当前被其他程序占用，请稍后重试。",
                "Solis Monitor",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void OpenDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            MessageBox.Show(
                _window,
                "当前采集路径不存在，请检查 Codex 是否已经创建会话数据。",
                "Codex 采集路径",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        Process.Start(new ProcessStartInfo(path)
        {
            UseShellExecute = true
        });
    }

    private bool Confirm(string title, string message) =>
        MessageBox.Show(
            _window,
            message,
            title,
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning) == MessageBoxResult.Yes;

    private void ShowResult(string title, string message, bool success) =>
        MessageBox.Show(
            _window,
            message,
            title,
            MessageBoxButton.OK,
            success ? MessageBoxImage.Information : MessageBoxImage.Warning);

    private DeviceDisplaySettings? ShowNightBacklightSettings(
        DeviceDisplaySettings settings)
    {
        var dialog = new SolisNightBacklightSettingsWindow(settings)
        {
            Owner = _window
        };
        return dialog.ShowDialog() == true ? dialog.Settings : null;
    }

    private static void OpenDeviceWebUi(string url)
    {
        Process.Start(new ProcessStartInfo(url)
        {
            UseShellExecute = true
        });
    }

    private void ShowWeatherSettings()
    {
        var dialog = new SolisWeatherSettingsWindow(
            _backend.Runtime.WeatherSettings,
            SolisRuntime.TestWeatherSettings,
            _backend.Runtime.SaveWeatherSettings)
        {
            Owner = _window
        };

        dialog.ShowDialog();
        _presenter.Refresh();
    }

    private void ShowDeviceWizard()
    {
        var dialog = new SolisDeviceSetupWizardWindow(
            () => _backend.Runtime.CurrentDevice,
            () => _backend.Runtime.DiscoveryCandidates,
            _backend.Runtime.ScanDevicesNow,
            _backend.Runtime.PairDeviceAsync,
            () => _backend.Runtime.BeginDeviceMaintenance(
                DateTimeOffset.Now,
                TimeSpan.FromMinutes(10)))
        {
            Owner = _window
        };

        dialog.ShowDialog();
        _presenter.Refresh();
    }

    private async void ShowFirmwareUpdate()
    {
        var fileDialog = new OpenFileDialog
        {
            Title = "选择 Solis Monitor 固件",
            Filter = "Solis Monitor 固件 (*.bin)|*.bin|所有文件 (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (fileDialog.ShowDialog(_window) != true)
            return;

        FirmwareImageValidationResult validation =
            FirmwareImageValidator.ValidateFile(fileDialog.FileName, long.MaxValue);
        if (!validation.Success || validation.Image is null)
        {
            _view.UpdateFirmware(new SolisFirmwareViewState(
                validation.ErrorMessage ?? "固件校验失败。",
                0,
                false));
            MessageBox.Show(
                _window,
                validation.ErrorMessage ?? "无法读取所选固件。",
                "固件校验失败",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        FirmwareImageInfo image = validation.Image;
        _view.UpdateFirmware(new SolisFirmwareViewState(
            $"已校验，等待确认\n{image.Version} · {Path.GetFileName(fileDialog.FileName)}",
            0,
            false));
        var confirmation = new SolisFirmwareConfirmationWindow(
            Path.GetFileName(fileDialog.FileName),
            image)
        {
            Owner = _window
        };
        if (confirmation.ShowDialog() != true)
            return;

        _view.UpdateFirmware(new SolisFirmwareViewState(
            "正在准备固件更新",
            0,
            true));
        var progress = new Progress<FirmwareUpdateProgress>(value =>
            _view.UpdateFirmware(new SolisFirmwareViewState(
                string.IsNullOrWhiteSpace(value.Detail)
                    ? value.Stage
                    : $"{value.Stage}\n{value.Detail}",
                value.Percent,
                true)));

        try
        {
            FirmwareUpdateResult result = await _backend.Runtime
                .UpdateFirmwareAsync(fileDialog.FileName, progress);
            _view.UpdateFirmware(new SolisFirmwareViewState(
                result.Message,
                result.Success ? 100 : 0,
                false));
            MessageBox.Show(
                _window,
                result.Message,
                result.Success ? "固件更新完成" : "固件更新未完成",
                MessageBoxButton.OK,
                result.Success
                    ? MessageBoxImage.Information
                    : MessageBoxImage.Warning);
        }
        catch (Exception exception)
        {
            _view.UpdateFirmware(new SolisFirmwareViewState(
                $"固件更新失败\n{exception.Message}",
                0,
                false));
            MessageBox.Show(
                _window,
                exception.Message,
                "固件更新失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SolisWpfDesktopHost));
    }
}
