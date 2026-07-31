#nullable enable

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading.Tasks;
using System.Windows.Forms;
using LibreHardwareMonitor.Solis.DeviceControl;
using LibreHardwareMonitor.Solis.Desktop;
using LibreHardwareMonitor.Solis.Diagnostics;
using LibreHardwareMonitor.Solis.Firmware;
using LibreHardwareMonitor.Solis.Metrics;
using LibreHardwareMonitor.Solis.Startup;
using LibreHardwareMonitor.Solis.Weather;
using LibreHardwareMonitor.UI.Themes;
using LibreHardwareMonitor.Utilities;

namespace LibreHardwareMonitor.UI;

internal sealed partial class SolisControlCenterControl : UserControl
{
    private const int PageContentWidth = 820;
    private const int SettingRowHeight = 72;
    private const int TrailingControlHeight = 32;
    private const int TrailingControlWidth = 120;

    private enum ControlCenterPage
    {
        Device,
        Service,
        Startup,
        Firmware,
        Developer
    }

    private readonly Func<bool> _deviceApiRunningProvider;
    private readonly Func<DeviceDiscoveryState> _deviceDiscoveryProvider;
    private readonly Func<SolisDiagnosticsSnapshot> _diagnosticsProvider;
    private readonly Func<DateTimeOffset?> _lastDeviceCommunicationProvider;
    private readonly Func<bool> _deviceApiRestarter;
    private readonly Func<string> _codexSessionsPathProvider;
    private readonly Func<SolisMetricsSnapshot> _snapshotProvider;
    private readonly Func<QWeatherSettings> _weatherSettingsProvider;
    private readonly Func<QWeatherSettings, WeatherMetricsReading> _weatherSettingsTester;
    private readonly Action<QWeatherSettings, WeatherMetricsReading> _weatherSettingsSaver;
    private readonly DesktopStartupSettingsController _startupSettings;
    private readonly Func<
        string,
        IProgress<FirmwareUpdateProgress>,
        Task<FirmwareUpdateResult>> _firmwareUpdater;
    private readonly Func<Task<DeviceControlResult>> _deviceSettingsLoader;
    private readonly Func<
        DeviceDisplaySettings,
        Task<DeviceControlResult>> _deviceSettingsSaver;
    private readonly Func<Task<DeviceControlResult>> _deviceRestarter;
    private readonly Action _deviceSetupWizardRequested;
    private readonly Action _clearPairingRequested;
    private readonly Action _restoreDefaultsRequested;
    private readonly Action<bool> _developerModeUnlockedSaver;
    private readonly DeveloperModeUnlockTracker _developerModeUnlockTracker = new();
    private readonly Dictionary<ControlCenterPage, Button> _navigationButtons = new();
    private readonly Dictionary<ControlCenterPage, Control> _pages = new();
    private readonly Panel _contentHost = null!;
    private readonly TableLayoutPanel _developerHost = null!;
    private readonly Panel _navigationPanel = null!;
    private readonly Label _codexStatus = null!;
    private readonly Label _deviceState = null!;
    private readonly Label _pairingStatus = null!;
    private readonly Label _diagnosticApiStatus = null!;
    private readonly Label _diagnosticWeatherStatus = null!;
    private readonly Label _serviceOverallStatus = null!;
    private readonly Label _serviceLastCheckedStatus = null!;
    private readonly Label _serviceSummaryDeviceStatus = null!;
    private readonly Label _serviceDeviceDetail = null!;
    private readonly Label _serviceDeviceStatus = null!;
    private readonly Label _serviceApiDetail = null!;
    private readonly Label _serviceApiStatus = null!;
    private readonly Label _serviceCodexDetail = null!;
    private readonly Label _serviceCodexStatus = null!;
    private readonly Label _serviceWeatherDetail = null!;
    private readonly Label _serviceWeatherStatus = null!;
    private readonly Button _developerModeButton = null!;
    private readonly Label _versionLabel = null!;
    private readonly Font _navigationFont = null!;
    private readonly Font _navigationSelectedFont = null!;
    private readonly System.Windows.Forms.Timer _deviceSettingsSaveTimer;
    private TableLayoutPanel _connectionSection = null!;
    private Control _clearPairingRow = null!;
    private CheckBox _autoStartToggle = null!;
    private CheckBox _developerModeToggle = null!;
    private CheckBox _silentStartupToggle = null!;
    private Label _startupSummaryStatus = null!;
    private Label _firmwareStatus = null!;
    private SolisProgressBar _firmwareProgress = null!;
    private Button _firmwareSelectButton = null!;
    private SolisSlider _brightnessSlider = null!;
    private Label _brightnessValue = null!;
    private Button _nightSettingsButton = null!;
    private Button _restartDeviceButton = null!;
    private DeviceDisplaySettings? _deviceDisplaySettings;
    private bool _deviceControlBusy;
    private bool _deviceSettingsDirty;
    private bool _deviceSettingsLoading;
    private bool _deviceSettingsSaveRunning;
    private bool _deviceRestartPending;
    private bool _synchronizingDeviceSettings;
    private bool _synchronizingStartupToggles;
    private bool _developerModeUnlocked;
    private bool _deviceSettingsAvailable;
    private bool _deviceCanControl;
    private string _deviceStateText = "正在等待局域网发现";
    private string _pairingStatusText = "尚未配对";
    private string _firmwareStatusText = "尚未选择固件";
    private int _firmwareProgressValue;
    private bool _firmwareSelectEnabled = true;
    private ControlCenterPage _activePage;

    public SolisControlCenterControl(
        Func<SolisMetricsSnapshot> snapshotProvider,
        Func<bool> deviceApiRunningProvider,
        Func<DeviceDiscoveryState> deviceDiscoveryProvider,
        Func<SolisDiagnosticsSnapshot> diagnosticsProvider,
        Func<DateTimeOffset?> lastDeviceCommunicationProvider,
        Func<bool> deviceApiRestarter,
        Func<string> codexSessionsPathProvider,
        Func<QWeatherSettings> weatherSettingsProvider,
        Func<QWeatherSettings, WeatherMetricsReading> weatherSettingsTester,
        Action<QWeatherSettings, WeatherMetricsReading> weatherSettingsSaver,
        Func<bool> silentStartupProvider,
        Action<bool> silentStartupSaver,
        Func<bool> autoStartProvider,
        Action<bool> autoStartSaver,
        Func<
            string,
            IProgress<FirmwareUpdateProgress>,
        Task<FirmwareUpdateResult>> firmwareUpdater,
        Func<Task<DeviceControlResult>> deviceSettingsLoader,
        Func<DeviceDisplaySettings, Task<DeviceControlResult>>
            deviceSettingsSaver,
        Func<Task<DeviceControlResult>> deviceRestarter,
        Action deviceSetupWizardRequested,
        Action clearPairingRequested,
        Action restoreDefaultsRequested,
        bool developerModeUnlocked,
        Action<bool> developerModeUnlockedSaver)
    {
        _snapshotProvider = snapshotProvider ?? throw new ArgumentNullException(nameof(snapshotProvider));
        _deviceApiRunningProvider = deviceApiRunningProvider ?? throw new ArgumentNullException(nameof(deviceApiRunningProvider));
        _deviceDiscoveryProvider = deviceDiscoveryProvider ??
                                   throw new ArgumentNullException(nameof(deviceDiscoveryProvider));
        _diagnosticsProvider = diagnosticsProvider ??
                               throw new ArgumentNullException(nameof(diagnosticsProvider));
        _lastDeviceCommunicationProvider = lastDeviceCommunicationProvider ??
                                           throw new ArgumentNullException(nameof(lastDeviceCommunicationProvider));
        _deviceApiRestarter = deviceApiRestarter ??
                              throw new ArgumentNullException(nameof(deviceApiRestarter));
        _codexSessionsPathProvider = codexSessionsPathProvider ??
                                     throw new ArgumentNullException(nameof(codexSessionsPathProvider));
        _weatherSettingsProvider = weatherSettingsProvider ??
                                   throw new ArgumentNullException(nameof(weatherSettingsProvider));
        _weatherSettingsTester = weatherSettingsTester ??
                                 throw new ArgumentNullException(nameof(weatherSettingsTester));
        _weatherSettingsSaver = weatherSettingsSaver ??
                                throw new ArgumentNullException(nameof(weatherSettingsSaver));
        _startupSettings = new DesktopStartupSettingsController(
            silentStartupProvider,
            silentStartupSaver,
            autoStartProvider,
            autoStartSaver);
        _firmwareUpdater = firmwareUpdater ??
                           throw new ArgumentNullException(nameof(firmwareUpdater));
        _deviceSettingsLoader = deviceSettingsLoader ??
                                throw new ArgumentNullException(nameof(deviceSettingsLoader));
        _deviceSettingsSaver = deviceSettingsSaver ??
                               throw new ArgumentNullException(nameof(deviceSettingsSaver));
        _deviceRestarter = deviceRestarter ??
                           throw new ArgumentNullException(nameof(deviceRestarter));
        _deviceSetupWizardRequested = deviceSetupWizardRequested ??
                                      throw new ArgumentNullException(nameof(deviceSetupWizardRequested));
        _clearPairingRequested = clearPairingRequested ??
                                 throw new ArgumentNullException(nameof(clearPairingRequested));
        _restoreDefaultsRequested = restoreDefaultsRequested ??
                                    throw new ArgumentNullException(nameof(restoreDefaultsRequested));
        _developerModeUnlockedSaver = developerModeUnlockedSaver ??
                                      throw new ArgumentNullException(nameof(developerModeUnlockedSaver));

        AutoScaleMode = AutoScaleMode.Dpi;
        Dock = DockStyle.Fill;
        MinimumSize = new Size(1340, 860);
        Padding = Padding.Empty;
        _developerModeUnlocked = developerModeUnlocked;
        _deviceSettingsSaveTimer = new System.Windows.Forms.Timer
        {
            Interval = 300
        };
        _deviceSettingsSaveTimer.Tick += async (_, _) =>
            await SaveInlineDeviceSettingsAsync();

        _activePage = ControlCenterPage.Device;
        if (InitializeWpfShell(developerModeUnlocked))
        {
            ApplyTheme();
            RefreshStatus();
            return;
        }

        _navigationFont = CreateFont(9.5f, FontStyle.Regular);
        _navigationSelectedFont = CreateFont(9.5f, FontStyle.Bold);
        _deviceState = CreateValueLabel("正在等待局域网发现");
        _pairingStatus = CreateValueLabel("尚未配对");
        _codexStatus = CreateValueLabel("等待数据");
        _diagnosticApiStatus = CreateValueLabel("正在启动");
        _diagnosticWeatherStatus = CreateValueLabel("等待数据");
        _serviceOverallStatus = CreateServiceStatusLabel("正在检查");
        _serviceLastCheckedStatus = CreateValueLabel("--");
        _serviceSummaryDeviceStatus = CreateServiceStatusLabel("正在扫描");
        _serviceDeviceDetail = CreateValueLabel("等待局域网发现");
        _serviceDeviceStatus = CreateServiceStatusLabel("正在扫描");
        _serviceApiDetail = CreateValueLabel("端口 18472");
        _serviceApiStatus = CreateServiceStatusLabel("正在启动");
        _serviceCodexDetail = CreateValueLabel(_codexSessionsPathProvider());
        _serviceCodexStatus = CreateServiceStatusLabel("等待采集");
        _serviceWeatherDetail = CreateValueLabel("尚未获得地点");
        _serviceWeatherStatus = CreateServiceStatusLabel("等待采集");

        _developerModeButton = CreateSecondaryButton("开发者模式");
        _developerModeButton.Visible = developerModeUnlocked;
        _developerModeButton.Click += (_, _) => ShowPage(ControlCenterPage.Developer);
        _versionLabel = new Label
        {
            AutoEllipsis = true,
            Cursor = Cursors.Hand,
            Dock = DockStyle.Fill,
            Font = CreateFont(8.5f, FontStyle.Regular),
            Margin = Padding.Empty,
            Text = $"版本 {Application.ProductVersion.Split('+')[0]}",
            TextAlign = ContentAlignment.MiddleCenter
        };
        _versionLabel.MouseUp += VersionLabelMouseUp;

        InitializeDeviceStateControls();
        InitializeStartupStateControls();
        InitializeFirmwareStateControls();

        _navigationPanel = new Panel
        {
            Dock = DockStyle.Left,
            Padding = new Padding(10, 16, 10, 12),
            Width = 160
        };

        PictureBox brandIcon = new()
        {
            Dock = DockStyle.Fill,
            Image = EmbeddedResources.GetImage("solis_monitor_icon.png"),
            Margin = new Padding(0, 4, 8, 6),
            SizeMode = PictureBoxSizeMode.Zoom
        };
        Label productName = new()
        {
            AutoEllipsis = true,
            Dock = DockStyle.Fill,
            Font = CreateFont(11, FontStyle.Bold),
            Margin = Padding.Empty,
            Text = "Solis Monitor",
            TextAlign = ContentAlignment.BottomLeft
        };
        Label productRole = new()
        {
            AutoEllipsis = true,
            Dock = DockStyle.Fill,
            Font = CreateFont(8.5f, FontStyle.Regular),
            Margin = Padding.Empty,
            Text = "副屏控制中心",
            TextAlign = ContentAlignment.TopLeft
        };
        TableLayoutPanel productText = new()
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            RowCount = 2
        };
        productText.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        productText.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        productText.Controls.Add(productName, 0, 0);
        productText.Controls.Add(productRole, 0, 1);
        TableLayoutPanel brandHeader = new()
        {
            ColumnCount = 2,
            Dock = DockStyle.Top,
            Height = 60,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            RowCount = 1,
            Tag = "brand-header"
        };
        brandHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34));
        brandHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        brandHeader.Controls.Add(brandIcon, 0, 0);
        brandHeader.Controls.Add(productText, 1, 0);

        _developerHost = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Bottom,
            Height = developerModeUnlocked ? 82 : 34,
            Padding = new Padding(0, 4, 0, 0),
            RowCount = 2
        };
        _developerHost.RowStyles.Add(new RowStyle(SizeType.Absolute, developerModeUnlocked ? 48 : 0));
        _developerHost.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        _developerModeButton.Dock = DockStyle.Fill;
        _developerModeButton.Margin = new Padding(0, 6, 0, 4);
        _developerHost.Controls.Add(_developerModeButton, 0, 0);
        _developerHost.Controls.Add(_versionLabel, 0, 1);

        FlowLayoutPanel navigation = new()
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            Padding = new Padding(0, 10, 0, 0),
            WrapContents = false
        };

        AddNavigationButton(navigation, ControlCenterPage.Device, "设备");
        AddNavigationButton(navigation, ControlCenterPage.Service, "服务");
        AddNavigationButton(navigation, ControlCenterPage.Startup, "启动与托盘");
        AddNavigationButton(navigation, ControlCenterPage.Firmware, "固件更新");

        _navigationPanel.Controls.Add(navigation);
        _navigationPanel.Controls.Add(_developerHost);
        _navigationPanel.Controls.Add(brandHeader);

        Panel separator = new()
        {
            Dock = DockStyle.Left,
            Width = 1
        };

        _contentHost = new Panel
        {
            AutoScroll = true,
            Dock = DockStyle.Fill,
            Padding = new Padding(24, 18, 24, 20)
        };

        _pages.Add(ControlCenterPage.Device, CreateDevicePage());
        _pages.Add(ControlCenterPage.Service, CreateServicePage());
        _pages.Add(ControlCenterPage.Startup, CreateStartupPage());
        _pages.Add(ControlCenterPage.Firmware, CreateFirmwarePage());
        _pages.Add(ControlCenterPage.Developer, CreateDeveloperPage());

        Controls.Add(_contentHost);
        Controls.Add(separator);
        Controls.Add(_navigationPanel);

        ShowPage(_activePage);
        ApplyTheme();
        RefreshStatus();
    }

    public event EventHandler? DeveloperModeRequested;

    public void ShowDevicePage() => ShowPage(ControlCenterPage.Device);

    public void ShowServicePage() => ShowPage(ControlCenterPage.Service);

    public void ApplyTheme()
    {
        Color background = Theme.Current.BackgroundColor;
        Color foreground = Theme.Current.ForegroundColor;
        bool dark = IsDark(background);
        Color canvas = dark
            ? Color.FromArgb(20, 23, 34)
            : Blend(background, Color.FromArgb(236, 243, 249), 0.36f);
        Color muted = Blend(canvas, foreground, dark ? 0.66f : 0.58f);
        Color surface = dark
            ? Color.FromArgb(31, 36, 50)
            : Blend(canvas, foreground, 0.035f);
        Color navigationSurface = dark
            ? Color.FromArgb(37, 41, 56)
            : Blend(surface, foreground, 0.025f);
        Color border = dark
            ? Color.FromArgb(55, 63, 82)
            : Blend(Theme.Current.LineColor, SystemColors.Highlight, 0.08f);

        BackColor = canvas;
        ForeColor = foreground;
        if (_wpfShellHost is not null)
        {
            _wpfShellHost.BackColor = canvas;
            Invalidate(true);
            return;
        }
        _contentHost.BackColor = canvas;
        _navigationPanel.BackColor = navigationSurface;
        _versionLabel.BackColor = navigationSurface;
        _versionLabel.ForeColor = muted;

        foreach (Control page in _pages.Values)
        {
            ApplyColors(page, canvas, surface, foreground, muted, border);
        }

        foreach (Button button in _navigationButtons.Values)
        {
            ApplyNavigationButtonTheme(button, button.Tag is ControlCenterPage page && page == _activePage);
        }

        foreach (Label label in FindControls<Label>(_navigationPanel))
        {
            label.BackColor = navigationSurface;
            label.ForeColor = label.Font.Bold ? foreground : muted;
        }

        foreach (Control control in FindControls<Control>(_navigationPanel))
        {
            if (control is SolisNavigationButton)
                continue;
            control.BackColor = navigationSurface;
        }

        foreach (Button button in FindControls<Button>(_navigationPanel))
        {
            if (_navigationButtons.ContainsValue(button))
                continue;
            button.BackColor = Blend(navigationSurface, SystemColors.Highlight, 0.12f);
            button.ForeColor = foreground;
            button.FlatAppearance.BorderColor = SystemColors.Highlight;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.MouseOverBackColor = Blend(navigationSurface, foreground, 0.08f);
            button.FlatAppearance.MouseDownBackColor = Blend(navigationSurface, foreground, 0.13f);
        }

        ApplyServiceStatusColors();
        Invalidate(true);
    }

    public void RefreshStatus()
    {
        SolisMetricsSnapshot snapshot = _snapshotProvider();
        bool weatherAvailable = snapshot.Weather.Location != null &&
                                (snapshot.Weather.OutdoorLowC.Available ||
                                 snapshot.Weather.OutdoorHighC.Available);

        if (_diagnosticApiStatus is not null)
            _diagnosticApiStatus.Text = _deviceApiRunningProvider() ? "运行中" : "未运行";
        DeviceDiscoveryState discovery = _deviceDiscoveryProvider();
        if (_deviceRestartPending)
        {
            _deviceStateText = "副屏正在重新启动…";
            _pairingStatusText = "等待副屏重新上线";
        }
        else if (discovery.Device is DiscoveredDevice device)
        {
            string signal = device.Rssi.HasValue ? $"{device.Rssi.Value} dBm" : "信号 --";
            _deviceStateText =
                $"{device.HostName}\r\n{device.IpAddress} · {signal}\r\n固件 {device.FirmwareVersion}";
            _pairingStatusText = device.PairingActive
                ? "已开启发现，等待配对"
                : device.Paired
                    ? "已连接 · 已配对"
                    : "已连接 · 未配对";
        }
        else
        {
            _deviceStateText = discovery.IsScanning
                ? "正在扫描当前局域网…"
                : discovery.ErrorCategory == "MultipleDevices"
                    ? "发现多个设备，暂不自动选择"
                    : "尚未发现副屏";
            _pairingStatusText = "尚未配对";
        }
        if (_deviceState is not null)
            _deviceState.Text = _deviceStateText;
        if (_pairingStatus is not null)
            _pairingStatus.Text = _pairingStatusText;
        if (_connectionSection is not null && _clearPairingRow is not null)
        {
            SetSettingRowVisible(
                _connectionSection,
                _clearPairingRow,
                discovery.Device?.Paired == true);
        }
        bool canControl = discovery.Device?.Paired == true &&
                          !_deviceControlBusy &&
                          !_deviceRestartPending;
        if (!canControl && !_deviceRestartPending &&
            discovery.Device?.Paired != true)
        {
            _deviceDisplaySettings = null;
        }
        bool settingsAvailable =
            canControl && _deviceDisplaySettings is not null;
        _deviceSettingsAvailable = settingsAvailable;
        _deviceCanControl = canControl;
        if (_brightnessSlider is not null &&
            _nightSettingsButton is not null &&
            _restartDeviceButton is not null)
        {
            bool controlStateChanged =
                _brightnessSlider.Enabled != settingsAvailable ||
                _nightSettingsButton.Enabled != settingsAvailable ||
                _restartDeviceButton.Enabled != canControl;
            _brightnessSlider.Enabled = settingsAvailable;
            _nightSettingsButton.Enabled = settingsAvailable;
            _restartDeviceButton.Enabled = canControl;
            if (controlStateChanged)
                ApplyTheme();
        }
        if (canControl && _deviceDisplaySettings is null &&
            !_deviceSettingsLoading)
        {
            _ = LoadInlineDeviceSettingsAsync();
        }
        if (_diagnosticWeatherStatus is not null)
            _diagnosticWeatherStatus.Text = weatherAvailable ? "天气 API 正常" : "等待天气数据";
        if (_codexStatus is not null)
            _codexStatus.Text = snapshot.Codex.Online ? "采集活跃" : "当前不活跃";
        RefreshDiagnosticsStatus();
        RefreshServiceOverview(snapshot, discovery);
        RefreshStartupSettings();
        RefreshWpfShell(snapshot, discovery, settingsAvailable, canControl);
    }

    private FlowLayoutPanel CreatePageBody(string title, string subtitle)
    {
        FlowLayoutPanel body = new()
        {
            AutoScroll = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.TopDown,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            WrapContents = false
        };

        var header = new TableLayoutPanel
        {
            ColumnCount = 1,
            Height = 68,
            Margin = new Padding(0, 0, 0, 14),
            Padding = Padding.Empty,
            RowCount = 1,
            Tag = "page-header",
            Width = PageContentWidth
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var text = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            RowCount = 2
        };
        text.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        text.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        Label titleLabel = CreateTextLabel(title, 18, FontStyle.Bold);
        titleLabel.TextAlign = ContentAlignment.BottomLeft;
        Label subtitleLabel = CreateTextLabel(subtitle, 9, FontStyle.Regular);
        subtitleLabel.TextAlign = ContentAlignment.TopLeft;
        text.Controls.Add(titleLabel, 0, 0);
        text.Controls.Add(subtitleLabel, 0, 1);
        header.Controls.Add(text, 0, 0);

        body.Controls.Add(header);
        return body;
    }

    private Control CreateStatusCard(
        string title,
        string description,
        Label status,
        string statusCaption)
    {
        TableLayoutPanel card = CreateCard(104);
        card.ColumnCount = 3;
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44));
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        card.RowCount = 2;
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 58));

        var icon = new SolisFluentIcon(GetStatusCardGlyph(title), 15)
        {
            Anchor = AnchorStyles.Left,
            Height = 36,
            Margin = new Padding(0, 0, 8, 0),
            Size = new Size(36, 36),
            Tag = "summary-icon"
        };
        Label titleLabel = CreateTextLabel(title, 11.5f, FontStyle.Bold);
        Label descriptionLabel = CreateTextLabel(description, 8.7f, FontStyle.Regular);
        Label captionLabel = CreateTextLabel(statusCaption, 8.5f, FontStyle.Regular);

        status.Dock = DockStyle.Fill;
        status.Margin = new Padding(12, 0, 2, 0);
        status.TextAlign = ContentAlignment.TopLeft;
        captionLabel.Margin = new Padding(12, 0, 2, 0);
        captionLabel.TextAlign = ContentAlignment.BottomLeft;

        card.Controls.Add(icon, 0, 0);
        card.SetRowSpan(icon, 2);
        card.Controls.Add(titleLabel, 1, 0);
        card.Controls.Add(descriptionLabel, 1, 1);
        card.Controls.Add(captionLabel, 2, 0);
        card.Controls.Add(status, 2, 1);
        return card;
    }

    private static string GetStatusCardGlyph(string title) => title switch
    {
        "当前设备" => "\uE772",
        "后台运行" => "\uE768",
        "本地 OTA" => "\uE898",
        "开发者入口" => "\uE943",
        _ => "\uE946"
    };

    private Control CreateSection(string title, params Control[] rows)
    {
        const int titleHeight = 38;
        int height = 16 + titleHeight + rows.Length * SettingRowHeight;
        TableLayoutPanel section = CreateCard(height);
        section.ColumnCount = 1;
        section.RowCount = rows.Length + 1;
        section.RowStyles.Add(new RowStyle(SizeType.Absolute, titleHeight));

        Label titleLabel = CreateTextLabel(title, 10.5f, FontStyle.Bold);
        titleLabel.Padding = new Padding(2, 0, 0, 0);
        section.Controls.Add(titleLabel, 0, 0);

        for (int i = 0; i < rows.Length; i++)
        {
            rows[i].Dock = DockStyle.Fill;
            rows[i].Margin = Padding.Empty;
            section.RowStyles.Add(new RowStyle(SizeType.Absolute, SettingRowHeight));
            section.Controls.Add(rows[i], 0, i + 1);
        }

        return section;
    }

    private static void SetSettingRowVisible(
        TableLayoutPanel section,
        Control row,
        bool visible)
    {
        int rowIndex = section.GetRow(row);
        if (rowIndex < 1 || rowIndex >= section.RowStyles.Count)
            return;

        RowStyle style = section.RowStyles[rowIndex];
        bool currentlyVisible = style.Height > 0;
        row.Visible = visible;
        if (currentlyVisible == visible)
            return;

        style.SizeType = SizeType.Absolute;
        style.Height = visible ? SettingRowHeight : 0;
        section.Height += visible ? SettingRowHeight : -SettingRowHeight;
    }

    private Control CreateSettingRow(string title, string description, Control trailing)
    {
        bool isInlineSetting = string.Equals(
            trailing.Tag as string,
            "inline-setting",
            StringComparison.Ordinal);
        TableLayoutPanel row = new()
        {
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = new Padding(2, 0, 2, 0),
            RowCount = 1,
            Tag = "setting-row"
        };
        row.ColumnStyles.Add(new ColumnStyle(
            SizeType.Percent,
            isInlineSetting ? 62 : 66));
        row.ColumnStyles.Add(new ColumnStyle(
            SizeType.Percent,
            isInlineSetting ? 38 : 34));

        Panel text = new()
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty
        };
        Label descriptionLabel = CreateTextLabel(description, 8.5f, FontStyle.Regular);
        descriptionLabel.Dock = DockStyle.Fill;
        descriptionLabel.TextAlign = ContentAlignment.TopLeft;
        Label titleLabel = CreateTextLabel(title, 9.7f, FontStyle.Regular);
        titleLabel.Dock = DockStyle.Top;
        titleLabel.Height = 24;
        titleLabel.TextAlign = ContentAlignment.MiddleLeft;
        text.Controls.Add(descriptionLabel);
        text.Controls.Add(titleLabel);

        if (trailing is Label label)
        {
            trailing.Dock = DockStyle.Top;
            trailing.Height = 26;
            trailing.Margin = new Padding(12, 0, 4, 0);
            label.TextAlign = ContentAlignment.MiddleLeft;
        }
        else if (isInlineSetting)
        {
            trailing.Dock = DockStyle.None;
            trailing.Anchor =
                AnchorStyles.Left |
                AnchorStyles.Top |
                AnchorStyles.Right;
            trailing.Height = TrailingControlHeight;
            trailing.Margin = new Padding(12, 8, 4, 8);
        }
        else
        {
            trailing.Margin = new Padding(12, 8, 4, 8);
            trailing.Anchor = AnchorStyles.Right | AnchorStyles.Top;
        }

        row.Controls.Add(text, 0, 0);
        row.Controls.Add(trailing, 1, 0);
        return row;
    }

    private Control CreateNote(string text)
    {
        Label note = CreateTextLabel(text, 8.7f, FontStyle.Regular);
        note.AutoEllipsis = false;
        note.Dock = DockStyle.Fill;
        note.Margin = Padding.Empty;
        note.Padding = new Padding(4, 0, 4, 0);

        var card = new SolisDashboardPanel
        {
            AutoSize = false,
            ColumnCount = 1,
            Height = 58,
            Margin = new Padding(0, 0, 0, 10),
            Padding = new Padding(14, 8, 14, 8),
            RowCount = 1,
            Tag = "dashboard-card",
            Width = PageContentWidth
        };
        card.Controls.Add(note, 0, 0);
        return card;
    }

    private TableLayoutPanel CreateCard(int height)
    {
        return new SolisDashboardPanel
        {
            AutoSize = false,
            Height = height,
            Margin = new Padding(0, 0, 0, 10),
            Padding = new Padding(14, 8, 14, 8),
            Tag = "dashboard-card",
            Width = PageContentWidth
        };
    }

    private static Label CreateTextLabel(string text, float size, FontStyle style)
    {
        return new Label
        {
            AutoEllipsis = true,
            Dock = DockStyle.Fill,
            Font = CreateFont(size, style),
            Margin = Padding.Empty,
            Text = text,
            TextAlign = ContentAlignment.MiddleLeft
        };
    }

    private static Label CreateValueLabel(string text)
    {
        return new Label
        {
            AutoEllipsis = true,
            AutoSize = false,
            Font = CreateFont(9, FontStyle.Regular),
            Tag = "value",
            Text = text,
            TextAlign = ContentAlignment.MiddleRight
        };
    }

    private static Button CreateActionButton(string text, bool enabled)
    {
        Button button = CreateSecondaryButton(text);
        button.Enabled = enabled;
        button.Height = TrailingControlHeight;
        button.Width = TrailingControlWidth;
        return button;
    }

    private static CheckBox CreateToggle(bool value, bool enabled)
    {
        var toggle = new SolisToggle()
        {
            AutoSize = false,
            Checked = value,
            Enabled = enabled,
            Height = 28,
            Text = value ? "开" : "关",
            Width = 52
        };
        toggle.CheckedChanged += (_, _) => SetToggleValue(toggle, toggle.Checked);
        return toggle;
    }

    private static void SetToggleValue(CheckBox toggle, bool value)
    {
        toggle.Checked = value;
        toggle.Text = value ? "开" : "关";
    }

    private static Button CreateSecondaryButton(string text)
    {
        Button button = new SolisButton()
        {
            AutoSize = false,
            Cursor = Cursors.Hand,
            FlatStyle = FlatStyle.Flat,
            Font = CreateFont(9.5f, FontStyle.Regular),
            Text = text,
            UseVisualStyleBackColor = false
        };
        button.FlatAppearance.BorderSize = 1;
        return button;
    }

    private void VersionLabelMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
            return;

        if (_developerModeButton.Visible)
            return;

        if (!_developerModeUnlockTracker.RegisterClick(DateTimeOffset.UtcNow))
            return;

        SetDeveloperModeUnlocked(true);
    }

    private void SetDeveloperModeUnlocked(bool unlocked)
    {
        _developerModeUnlocked = unlocked;
        if (_developerModeButton is not null)
            _developerModeButton.Visible = unlocked;
        if (_developerHost is not null)
        {
            _developerHost.RowStyles[0].Height = unlocked ? 50 : 0;
            _developerHost.Height = unlocked ? 94 : 40;
        }

        _developerModeUnlockedSaver(unlocked);
        if (_developerModeToggle != null)
            SetToggleValue(_developerModeToggle, unlocked);
        _wpfShell?.SetDeveloperVisible(unlocked);
        ApplyTheme();
    }

    private void AddNavigationButton(
        FlowLayoutPanel host,
        ControlCenterPage page,
        string text)
    {
        Button button = new SolisNavigationButton(text, GetNavigationGlyph(page))
        {
            AutoSize = false,
            Cursor = Cursors.Hand,
            Font = _navigationFont,
            Height = 40,
            Margin = new Padding(0, 0, 0, 4),
            Tag = page,
            UseVisualStyleBackColor = false,
            Width = 140
        };
        button.Click += (_, _) => ShowPage(page);
        _navigationButtons.Add(page, button);
        host.Controls.Add(button);
    }

    private void ShowPage(ControlCenterPage page)
    {
        _developerModeUnlockTracker.Reset();
        if (_wpfShell is not null)
        {
            _activePage = page;
            _wpfShell.ShowPage(page.ToString());
            return;
        }

        _contentHost.SuspendLayout();
        try
        {
            _pages.TryGetValue(_activePage, out Control? previous);
            Control content = _pages[page];
            if (content.Parent != _contentHost)
            {
                content.Visible = false;
                _contentHost.Controls.Add(content);
            }

            _activePage = page;
            ResizePage(content);
            _contentHost.AutoScrollPosition = Point.Empty;
            content.Visible = true;
            content.BringToFront();

            if (previous != null && previous != content)
                previous.Visible = false;
        }
        finally
        {
            _contentHost.ResumeLayout(false);
        }

        _contentHost.PerformLayout();
        _contentHost.Invalidate(true);
        _contentHost.Update();

        foreach (KeyValuePair<ControlCenterPage, Button> pair in _navigationButtons)
        {
            ApplyNavigationButtonTheme(pair.Value, pair.Key == page);
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        ResizeActivePage();
    }

    private void ResizeActivePage()
    {
        if (!_pages.TryGetValue(_activePage, out Control? content))
            return;

        ResizePage(content);
    }

    private void ResizePage(Control content)
    {
        int width = Math.Max(
            500,
            _contentHost.ClientSize.Width -
            _contentHost.Padding.Horizontal -
            SystemInformation.VerticalScrollBarWidth);
        content.Width = width;
        if (string.Equals(
                content.Tag as string,
                "wpf-service",
                StringComparison.Ordinal))
        {
            content.Height = Math.Max(
                420,
                _contentHost.ClientSize.Height -
                _contentHost.Padding.Vertical -
                1);
        }
        foreach (Control child in content.Controls)
        {
            child.Width = width;
        }
    }

    private void ApplyNavigationButtonTheme(Button button, bool selected)
    {
        Color background = Theme.Current.BackgroundColor;
        Color navigationBackground = _navigationPanel.BackColor;
        Color accent = SystemColors.Highlight;
        if (button is SolisNavigationButton navigationButton)
        {
            navigationButton.AccentColor = Blend(navigationBackground, accent, 0.72f);
            navigationButton.BaseColor = navigationBackground;
            navigationButton.HoverColor = Blend(navigationBackground, accent, 0.2f);
            navigationButton.InactiveTextColor = Theme.Current.ForegroundColor;
            navigationButton.Selected = selected;
            navigationButton.Font = selected ? _navigationSelectedFont : _navigationFont;
            navigationButton.Invalidate();
            return;
        }

        button.BackColor = selected ? Blend(navigationBackground, accent, 0.2f) : navigationBackground;
        button.ForeColor = selected ? accent : Theme.Current.ForegroundColor;
        button.FlatAppearance.MouseOverBackColor = Blend(navigationBackground, accent, 0.14f);
        button.FlatAppearance.MouseDownBackColor = Blend(navigationBackground, accent, 0.24f);
        button.Font = selected ? _navigationSelectedFont : _navigationFont;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _deviceSettingsSaveTimer.Dispose();
            _navigationFont?.Dispose();
            _navigationSelectedFont?.Dispose();
        }

        base.Dispose(disposing);
    }

    private static void ApplyColors(
        Control root,
        Color background,
        Color surface,
        Color foreground,
        Color muted,
        Color border)
    {
        root.BackColor = background;
        root.ForeColor = foreground;

        foreach (Control control in FindControls<Control>(root))
        {
            control.BackColor = background;
            control.ForeColor = foreground;

            if (control is Label label &&
                !label.Font.Bold &&
                !string.Equals(label.Tag as string, "value", StringComparison.Ordinal))
            {
                label.ForeColor = label.Font.Size <= 9.5f ? muted : foreground;
            }
        }

        foreach (Control card in FindControls<Control>(root))
        {
            bool isCard = string.Equals(card.Tag as string, "card", StringComparison.Ordinal);
            bool isNote = string.Equals(card.Tag as string, "note", StringComparison.Ordinal);
            if (!isCard && !isNote)
                continue;

            card.BackColor = surface;
            foreach (Control child in FindControls<Control>(card))
            {
                child.BackColor = surface;
            }
        }

        bool dark = IsDark(background);
        Color dashboardSurface = dark
            ? surface
            : Blend(surface, foreground, 0.018f);
        Color dashboardSurfaceEnd = dashboardSurface;
        Color innerSurface = dark
            ? Color.FromArgb(38, 44, 59)
            : Blend(surface, foreground, 0.028f);
        Color innerSurfaceEnd = innerSurface;
        foreach (SolisDashboardPanel dashboard in FindControls<SolisDashboardPanel>(root))
        {
            string? role = dashboard.Tag as string;
            bool inner = string.Equals(
                role,
                "service-inner-card",
                StringComparison.Ordinal);
            bool summary = string.Equals(
                role,
                "service-summary-card",
                StringComparison.Ordinal);
            bool outer = string.Equals(
                role,
                "service-dashboard-card",
                StringComparison.Ordinal) ||
                         string.Equals(
                             role,
                             "dashboard-card",
                             StringComparison.Ordinal);
            if (!inner && !summary && !outer)
            {
                continue;
            }

            dashboard.BackColor = Color.Transparent;
            dashboard.BorderColor = outer
                ? Blend(dashboardSurface, border, dark ? 0.34f : 0.48f)
                : inner
                    ? Blend(innerSurface, border, dark ? 0.54f : 0.62f)
                    : Blend(dashboardSurface, border, dark ? 0.72f : 0.78f);
            dashboard.CornerRadius = inner ? 9 : 12;
            dashboard.FillColor = inner ? innerSurface : dashboardSurface;
            dashboard.FillColorEnd = inner ? innerSurfaceEnd : dashboardSurfaceEnd;
            foreach (Control child in FindControls<Control>(dashboard))
            {
                if (child is Button)
                    continue;
                if (child is Label ||
                    child is Panel ||
                    child is TableLayoutPanel ||
                    child is SolisFluentIcon)
                {
                    child.BackColor = Color.Transparent;
                }
            }

            dashboard.Invalidate();
        }

        foreach (SolisFluentIcon icon in FindControls<SolisFluentIcon>(root))
        {
            icon.BackColor = Color.Transparent;
            if (string.Equals(
                    icon.Tag as string,
                    "service-connector",
                    StringComparison.Ordinal))
            {
                icon.BadgeColor = Color.Transparent;
                icon.GlyphColor = Color.FromArgb(72, 199, 116);
            }
            else
            {
                icon.BadgeColor = Color.FromArgb(
                    dark ? 46 : 30,
                    SystemColors.Highlight);
                icon.GlyphColor = dark
                    ? Blend(Color.White, SystemColors.Highlight, 0.28f)
                    : SystemColors.Highlight;
            }

            icon.Invalidate();
        }

        foreach (Button button in FindControls<Button>(root))
        {
            Color accent = SystemColors.Highlight;
            bool primary = string.Equals(
                button.Tag as string,
                "primary-action",
                StringComparison.Ordinal);
            button.BackColor = button.Enabled
                ? primary
                    ? accent
                    : Blend(surface, foreground, dark ? 0.055f : 0.025f)
                : Blend(surface, foreground, IsDark(background) ? 0.11f : 0.06f);
            button.ForeColor = primary && button.Enabled ? Color.White : foreground;
            button.FlatAppearance.BorderColor = button.Enabled
                ? primary
                    ? accent
                    : Blend(surface, border, dark ? 0.70f : 0.84f)
                : Blend(surface, foreground, 0.34f);
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.MouseOverBackColor = Blend(surface, foreground, 0.08f);
            button.FlatAppearance.MouseDownBackColor = Blend(surface, foreground, 0.13f);
            if (button is SolisButton solisButton)
            {
                solisButton.DisabledTextColor = muted;
                solisButton.DisabledBorderColor = Blend(surface, foreground, 0.34f);
            }
        }

        foreach (CheckBox checkBox in FindControls<CheckBox>(root))
        {
            bool isInlineCheck = string.Equals(
                checkBox.Tag as string,
                "inline-check",
                StringComparison.Ordinal);
            checkBox.BackColor = checkBox.Enabled
                ? isInlineCheck
                    ? surface
                    : Blend(surface, SystemColors.Highlight, 0.12f)
                : Blend(surface, foreground, IsDark(background) ? 0.11f : 0.06f);
            checkBox.ForeColor = foreground;
            checkBox.FlatAppearance.BorderColor = checkBox.Enabled
                ? SystemColors.Highlight
                : Blend(surface, foreground, 0.34f);
            checkBox.FlatAppearance.BorderSize = 1;
            if (checkBox is SolisToggle solisToggle)
            {
                solisToggle.DisabledTextColor = muted;
                solisToggle.DisabledBorderColor = Blend(surface, foreground, 0.34f);
                solisToggle.AccentColor = SystemColors.Highlight;
                solisToggle.TrackColor = Blend(surface, foreground, dark ? 0.18f : 0.13f);
                solisToggle.KnobColor = dark ? Color.White : Color.White;
                solisToggle.Invalidate();
            }
        }

        foreach (SolisProgressBar progress in FindControls<SolisProgressBar>(root))
        {
            progress.BackColor = surface;
            progress.AccentColor = SystemColors.Highlight;
            progress.TrackColor = Blend(surface, foreground, dark ? 0.16f : 0.10f);
            progress.Invalidate();
        }

        foreach (SolisSlider slider in FindControls<SolisSlider>(root))
        {
            slider.BackColor = surface;
            slider.AccentColor = SystemColors.Highlight;
            slider.TrackColor = Blend(surface, foreground, dark ? 0.18f : 0.12f);
            slider.ThumbColor = dark ? Color.White : SystemColors.Highlight;
            slider.DisabledColor = Blend(surface, foreground, dark ? 0.22f : 0.16f);
            slider.Invalidate();
        }

        foreach (Control control in FindControls<Control>(root))
        {
            if (string.Equals(control.Tag as string, "setting-row", StringComparison.Ordinal))
            {
                control.Padding = new Padding(2, 0, 2, 0);
            }
        }

        foreach (Control danger in FindControls<Control>(root))
        {
            if (!string.Equals(danger.Tag as string, "danger", StringComparison.Ordinal))
                continue;

            Color dangerSurface = Blend(
                surface,
                Color.FromArgb(176, 43, 55),
                IsDark(background) ? 0.22f : 0.12f);
            danger.BackColor = dangerSurface;
            foreach (Control child in FindControls<Control>(danger))
                child.BackColor = dangerSurface;
            foreach (Button button in FindControls<Button>(danger))
            {
                button.BackColor = Color.FromArgb(176, 43, 55);
                button.ForeColor = Color.White;
                button.FlatAppearance.BorderColor = Color.FromArgb(210, 76, 86);
                button.FlatAppearance.MouseOverBackColor = Color.FromArgb(194, 54, 66);
                button.FlatAppearance.MouseDownBackColor = Color.FromArgb(150, 34, 44);
            }
        }
    }

    private static IEnumerable<T> FindControls<T>(Control root) where T : Control
    {
        foreach (Control child in root.Controls)
        {
            if (child is T match)
                yield return match;
            foreach (T nested in FindControls<T>(child))
                yield return nested;
        }
    }

    private static Font CreateFont(float size, FontStyle style)
    {
        string family = size >= 13
            ? "Segoe UI Variable Display"
            : "Segoe UI Variable Text";
        return new Font(family, size, style, GraphicsUnit.Point);
    }

    private static string GetNavigationGlyph(ControlCenterPage page)
    {
        return page switch
        {
            ControlCenterPage.Device => "\uE772",
            ControlCenterPage.Service => "\uE791",
            ControlCenterPage.Startup => "\uE7E8",
            ControlCenterPage.Firmware => "\uE777",
            ControlCenterPage.Developer => "\uE713",
            _ => "\uE700"
        };
    }

    private static bool IsDark(Color color)
    {
        double luminance = color.R * 0.2126 + color.G * 0.7152 + color.B * 0.0722;
        return luminance < 128;
    }

    private static Color Blend(Color first, Color second, float amount)
    {
        amount = Math.Max(0, Math.Min(1, amount));
        return Color.FromArgb(
            (int)Math.Round(first.R + (second.R - first.R) * amount),
            (int)Math.Round(first.G + (second.G - first.G) * amount),
            (int)Math.Round(first.B + (second.B - first.B) * amount));
    }

    private sealed class SolisButton : Button
    {
        private bool _hovered;
        private bool _pressed;

        public Color DisabledBorderColor { get; set; } = SystemColors.ControlDark;

        public Color DisabledTextColor { get; set; } = SystemColors.GrayText;

        protected override void OnMouseDown(MouseEventArgs mevent)
        {
            _pressed = true;
            Invalidate();
            base.OnMouseDown(mevent);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hovered = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hovered = false;
            _pressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseUp(MouseEventArgs mevent)
        {
            _pressed = false;
            Invalidate();
            base.OnMouseUp(mevent);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.Clear(ResolveControlBackground(Parent, BackColor));
            Color fill = !Enabled
                ? BackColor
                : _pressed
                    ? Blend(BackColor, ForeColor, 0.13f)
                    : _hovered
                        ? Blend(BackColor, ForeColor, 0.08f)
                        : BackColor;
            Color border = Enabled
                ? FlatAppearance.BorderColor
                : DisabledBorderColor;
            Rectangle bounds = ClientRectangle;
            bounds.Width -= 1;
            bounds.Height -= 1;
            using GraphicsPath path = CreateRoundedRectangle(bounds, 7);
            using var brush = new SolidBrush(fill);
            using var pen = new Pen(border);
            e.Graphics.FillPath(brush, path);
            e.Graphics.DrawPath(pen, path);
            TextRenderer.DrawText(
                e.Graphics,
                Text,
                Font,
                ClientRectangle,
                Enabled ? ForeColor : DisabledTextColor,
                Color.Transparent,
                TextFormatFlags.HorizontalCenter |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine |
                TextFormatFlags.EndEllipsis);
        }
    }

    private sealed class SolisToggle : CheckBox
    {
        public SolisToggle()
        {
            AutoCheck = true;
            DoubleBuffered = true;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor |
                ControlStyles.UserPaint,
                true);
        }

        public Color AccentColor { get; set; } = SystemColors.Highlight;

        public Color DisabledBorderColor { get; set; } = SystemColors.ControlDark;

        public Color DisabledTextColor { get; set; } = SystemColors.GrayText;

        public Color KnobColor { get; set; } = Color.White;

        public Color TrackColor { get; set; } = SystemColors.ControlDark;

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(ResolveControlBackground(Parent, BackColor));

            int trackHeight = Math.Min(22, Math.Max(12, Height - 4));
            int trackWidth = Math.Min(46, Math.Max(trackHeight * 2, Width - 4));
            Rectangle track = new(
                Math.Max(0, Width - trackWidth),
                Math.Max(0, (Height - trackHeight) / 2),
                trackWidth,
                trackHeight);
            Color trackColor = !Enabled
                ? DisabledBorderColor
                : Checked
                    ? AccentColor
                    : TrackColor;
            using (GraphicsPath path = CreateRoundedRectangle(track, trackHeight / 2))
            using (var brush = new SolidBrush(trackColor))
            {
                e.Graphics.FillPath(brush, path);
            }

            int knobSize = trackHeight - 6;
            int knobX = Checked
                ? track.Right - knobSize - 3
                : track.Left + 3;
            Rectangle knob = new(
                knobX,
                track.Top + 3,
                knobSize,
                knobSize);
            using var knobBrush = new SolidBrush(
                Enabled ? KnobColor : DisabledTextColor);
            e.Graphics.FillEllipse(knobBrush, knob);
        }
    }
}
