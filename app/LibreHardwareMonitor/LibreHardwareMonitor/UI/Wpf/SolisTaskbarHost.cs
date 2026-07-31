#nullable enable

using System;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using H.NotifyIcon;
using LibreHardwareMonitor.Solis.Desktop;
using LibreHardwareMonitor.Solis.DeviceControl;

namespace LibreHardwareMonitor.UI.WpfViews;

public sealed class SolisTaskbarHost : IDisposable
{
    private readonly Action _exit;
    private readonly DesktopStartupSettingsController _startupSettings;
    private readonly Action _showWindow;
    private readonly Func<DeviceTrayPresentation>? _getDevicePresentation;
    private readonly Action<string>? _openDeviceWebUi;
    private readonly MenuItem _autoStartItem;
    private readonly MenuItem _openDeviceWebUiItem;
    private readonly MenuItem _silentStartupItem;
    private readonly TaskbarIcon _taskbarIcon;

    public SolisTaskbarHost(
        Action showWindow,
        Func<bool> getSilentStartup,
        Action<bool> setSilentStartup,
        Func<bool> getAutoStart,
        Action<bool> setAutoStart,
        Action exit,
        Func<DeviceTrayPresentation>? getDevicePresentation = null,
        Action<string>? openDeviceWebUi = null)
    {
        _showWindow = showWindow ??
            throw new ArgumentNullException(nameof(showWindow));
        _startupSettings = new DesktopStartupSettingsController(
            getSilentStartup,
            setSilentStartup,
            getAutoStart,
            setAutoStart);
        _exit = exit ?? throw new ArgumentNullException(nameof(exit));
        _getDevicePresentation = getDevicePresentation;
        _openDeviceWebUi = openDeviceWebUi;

        _silentStartupItem = CreateCheckableItem(
            "静默启动",
            ToggleSilentStartup);
        _autoStartItem = CreateCheckableItem(
            "开机启动",
            ToggleAutoStart);

        var showItem = new MenuItem { Header = "显示控制台" };
        showItem.Click += (_, _) => _showWindow();
        _openDeviceWebUiItem = new MenuItem
        {
            Header = "显示副屏 WebUI",
            IsEnabled = false
        };
        _openDeviceWebUiItem.Click += (_, _) => OpenDeviceWebUi();

        var exitItem = new MenuItem { Header = "退出" };
        exitItem.Click += (_, _) => _exit();

        var contextMenu = new ContextMenu();
        contextMenu.Items.Add(showItem);
        contextMenu.Items.Add(_openDeviceWebUiItem);
        contextMenu.Items.Add(new Separator());
        contextMenu.Items.Add(_silentStartupItem);
        contextMenu.Items.Add(_autoStartItem);
        contextMenu.Items.Add(new Separator());
        contextMenu.Items.Add(exitItem);
        contextMenu.Opened += (_, _) => RefreshState();

        _taskbarIcon = new TaskbarIcon
        {
            ToolTipText = "Solis Monitor",
            IconSource = LoadIconSource(),
            ContextMenu = contextMenu
        };
        _taskbarIcon.TrayLeftMouseUp += (_, _) => _showWindow();
        _taskbarIcon.ForceCreate(enablesEfficiencyMode: false);
        RefreshState();
    }

    public void Dispose()
    {
        _taskbarIcon.Dispose();
    }

    public void RefreshStartupState()
    {
        _silentStartupItem.IsChecked = _startupSettings.SilentStartup;
        _autoStartItem.IsChecked = _startupSettings.AutoStart;
    }

    private void RefreshState()
    {
        RefreshStartupState();
        DeviceTrayPresentation? presentation = _getDevicePresentation?.Invoke();
        _openDeviceWebUiItem.IsEnabled =
            presentation?.CanOpenWebUi == true && _openDeviceWebUi is not null;
    }

    private void OpenDeviceWebUi()
    {
        DeviceTrayPresentation? presentation = _getDevicePresentation?.Invoke();
        if (presentation?.WebUiUrl is string url && _openDeviceWebUi is not null)
            _openDeviceWebUi(url);
    }

    private static MenuItem CreateCheckableItem(
        string header,
        Action click)
    {
        var item = new MenuItem
        {
            Header = header,
            IsCheckable = true
        };
        item.Click += (_, _) => click();
        return item;
    }

    private static ImageSource LoadIconSource()
    {
        var source = new BitmapImage();
        source.BeginInit();
        source.UriSource = new Uri(
            "pack://application:,,,/SolisMonitor;component/Resources/icon.ico",
            UriKind.Absolute);
        source.CacheOption = BitmapCacheOption.OnLoad;
        source.EndInit();
        source.Freeze();
        return source;
    }

    private void ToggleAutoStart()
    {
        bool enabled = !_startupSettings.AutoStart;
        _startupSettings.SetAutoStart(enabled);
        RefreshStartupState();
    }

    private void ToggleSilentStartup()
    {
        _startupSettings.SetSilentStartup(
            !_startupSettings.SilentStartup);
        RefreshStartupState();
    }
}
