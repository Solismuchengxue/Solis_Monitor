#nullable enable

using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using LibreHardwareMonitor.Solis.DeviceControl;
using LibreHardwareMonitor.Solis.Diagnostics;
using LibreHardwareMonitor.Solis.Metrics;
using LibreHardwareMonitor.Solis.Weather;
using LibreHardwareMonitor.UI.WpfViews;
using LibreHardwareMonitor.Utilities;

namespace LibreHardwareMonitor.UI;

internal sealed partial class SolisControlCenterControl : UserControl
{
    private const int ServicePanelGap = 10;
    private const string FluentApi = "\uE774";
    private const string FluentCheck = "\uE73E";
    private const string FluentChevronRight = "\uE72A";
    private const string FluentClock = "\uE823";
    private const string FluentCloud = "\uE753";
    private const string FluentCode = "\uE943";
    private const string FluentCopy = "\uE77F";
    private const string FluentDevices = "\uE772";
    private const string FluentRefresh = "\uE72C";
    private const string FluentSystem = "\uE770";
    private SolisServiceView? _serviceWpfView;

    private Control CreateServicePage()
    {
        try
        {
            _serviceWpfView = new SolisServiceView();
            _serviceWpfView.RestartRequested += (_, _) => RestartDeviceApiFromWpf();
            _serviceWpfView.CopyDiagnosticsRequested += (_, _) => CopyDiagnostics();
            _serviceWpfView.EditWeatherRequested += (_, _) => ShowWeatherSettings();
            _serviceWpfView.OpenCodexRequested += (_, _) => OpenCodexSessionsFolder();
            return new ElementHost
            {
                AutoSize = false,
                Child = _serviceWpfView,
                Height = 520,
                Margin = Padding.Empty,
                Tag = "wpf-service",
                Width = PageContentWidth
            };
        }
        catch
        {
            _serviceWpfView = null;
            return CreateWinFormsServicePage();
        }
    }

    private Control CreateWinFormsServicePage()
    {
        FlowLayoutPanel body = CreatePageBody(
            "服务",
            "从 PC 后台到副屏，一眼查看整条服务链路。");

        body.Controls.Add(CreateServiceSummary());
        body.Controls.Add(CreateDependencySection());
        body.Controls.Add(CreateSupportingServicesSection());
        body.Controls.Add(CreateRecoverySection());
        body.Controls.Add(CreateRestoreDefaultsSection());
        return body;
    }

    private Control CreateServiceSummary()
    {
        TableLayoutPanel summary = new()
        {
            Height = 108,
            Margin = new Padding(0, 0, 0, 10),
            Padding = Padding.Empty,
            Width = PageContentWidth
        };
        summary.ColumnCount = 3;
        summary.RowCount = 1;
        summary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
        summary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
        summary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.334f));
        summary.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        summary.Controls.Add(
            CreateServiceSummaryItem(
                "整体状态",
                _serviceOverallStatus,
                "核心服务综合结果",
                FluentCheck,
                new Padding(0, 0, ServicePanelGap / 2, 0)),
            0,
            0);
        summary.Controls.Add(
            CreateServiceSummaryItem(
                "副屏",
                _serviceSummaryDeviceStatus,
                "当前配对设备",
                FluentDevices,
                new Padding(ServicePanelGap / 2, 0, ServicePanelGap / 2, 0)),
            1,
            0);
        summary.Controls.Add(
            CreateServiceSummaryItem(
                "最近通信",
                _serviceLastCheckedStatus,
                "副屏最后请求数据",
                FluentClock,
                new Padding(ServicePanelGap / 2, 0, 0, 0)),
            2,
            0);
        return summary;
    }

    private Control CreateDependencySection()
    {
        TableLayoutPanel content = new()
        {
            ColumnCount = 5,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            RowCount = 1
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 31));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 3.5f));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 31));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 3.5f));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 31));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        Label pcStatus = CreateServiceStatusLabel("运行中");
        SetServiceStatus(pcStatus, "运行中", DiagnosticCheckState.Normal);
        content.Controls.Add(
            CreateDependencyNode(
                "PC 后台",
                "Solis Monitor",
                $"进程 {System.Diagnostics.Process.GetCurrentProcess().Id}",
                pcStatus,
                FluentSystem,
                Padding.Empty),
            0,
            0);
        content.Controls.Add(CreateDependencyConnector(), 1, 0);
        content.Controls.Add(
            CreateDependencyNode(
                "设备 API",
                "本机与局域网服务",
                "端口 18472",
                _serviceApiStatus,
                FluentApi,
                Padding.Empty),
            2,
            0);
        content.Controls.Add(CreateDependencyConnector(), 3, 0);
        content.Controls.Add(
            CreateDependencyNode(
                "当前副屏",
                "等待设备信息",
                string.Empty,
                _serviceDeviceStatus,
                FluentDevices,
                Padding.Empty,
                _serviceDeviceDetail),
            4,
            0);

        return CreateServiceSection("服务依赖链", content, 186);
    }

    private Control CreateSupportingServicesSection()
    {
        Button editWeatherButton = CreateActionButton("编辑天气", true);
        editWeatherButton.Click += (_, _) => ShowWeatherSettings();

        TableLayoutPanel content = new()
        {
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            RowCount = 1
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.Controls.Add(
            CreateSupportingServiceCard(
                "Codex 采集",
                _serviceCodexStatus,
                "采集路径",
                _serviceCodexDetail,
                FluentCode,
                null,
                new Padding(0, 0, ServicePanelGap / 2, 0)),
            0,
            0);
        content.Controls.Add(
            CreateSupportingServiceCard(
                "天气 API",
                _serviceWeatherStatus,
                "当前位置",
                _serviceWeatherDetail,
                FluentCloud,
                editWeatherButton,
                new Padding(ServicePanelGap / 2, 0, 0, 0)),
            1,
            0);

        return CreateServiceSection("独立采集", content, 162);
    }

    private Control CreateRecoverySection()
    {
        Button restartButton = CreateActionButton("重启服务", true);
        restartButton.Click += (_, _) => RestartDeviceApi(restartButton);
        Button copyButton = CreateActionButton("复制诊断信息", true);
        copyButton.Click += (_, _) => CopyDiagnostics();

        TableLayoutPanel actions = new()
        {
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            RowCount = 1
        };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        actions.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        actions.Controls.Add(
            CreateRecoveryAction(
                "重启服务",
                "重新启动本地设备 API 并立即检查监听状态",
                restartButton,
                FluentRefresh,
                new Padding(0, 0, ServicePanelGap / 2, 0)),
            0,
            0);
        actions.Controls.Add(
            CreateRecoveryAction(
                "诊断信息",
                "复制已排除密钥、密码和完整令牌的状态摘要",
                copyButton,
                FluentCopy,
                new Padding(ServicePanelGap / 2, 0, 0, 0)),
            1,
            0);

        return CreateServiceSection("修复与导出", actions, 124);
    }

    private Control CreateRestoreDefaultsSection()
    {
        Button restoreButton = CreateActionButton("恢复默认设置", true);
        restoreButton.Tag = "danger-action";
        restoreButton.Click += (_, _) =>
        {
            using var form = new RestoreDefaultsConfirmationForm();
            if (form.ShowDialog(FindForm()) == DialogResult.OK)
                _restoreDefaultsRequested();
        };
        Control danger = CreateSection(
            "恢复默认设置",
            CreateSettingRow(
                "恢复 Solis 配置",
                "清除配对、天气、启动和开发者设置后重新启动程序",
                restoreButton));
        danger.Tag = "danger";
        return danger;
    }

    private void RestartDeviceApi(Button button)
    {
        button.Enabled = false;
        button.Text = "正在重启";
        try
        {
            RestartDeviceApiAndNotify();
        }
        finally
        {
            button.Text = "重启服务";
            button.Enabled = true;
        }
    }

    private void RestartDeviceApiFromWpf()
    {
        _serviceWpfView?.SetRestartBusy(true);
        try
        {
            RestartDeviceApiAndNotify();
        }
        finally
        {
            _serviceWpfView?.SetRestartBusy(false);
        }
    }

    private void RestartDeviceApiAndNotify()
    {
        bool restarted = _deviceApiRestarter();
        RefreshStatus();
        SolisDialog.Show(
            FindForm(),
            "重启服务",
            restarted
                ? "设备 API 已重新启动，端口 18472 正在监听。"
                : "设备 API 启动失败，请复制诊断信息查看错误类别。",
            restarted
                ? SolisDialogKind.Success
                : SolisDialogKind.Warning);
    }

    private void ShowWeatherSettings()
    {
        using var form = new WeatherSettingsForm(
            _weatherSettingsProvider(),
            _weatherSettingsTester,
            _weatherSettingsSaver);
        if (form.ShowDialog(FindForm()) == DialogResult.OK)
            RefreshStatus();
    }

    private void OpenCodexSessionsFolder()
    {
        string path = _codexSessionsPathProvider();
        if (!Directory.Exists(path))
        {
            SolisDialog.Show(
                FindForm(),
                "Codex 采集路径",
                "当前采集路径不存在，请检查 Codex 是否已经创建会话数据。",
                SolisDialogKind.Warning);
            return;
        }

        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private void RefreshServiceOverview(
        SolisMetricsSnapshot snapshot,
        DeviceDiscoveryState discovery)
    {
        SolisDiagnosticsSnapshot diagnostics = _diagnosticsProvider();
        DiagnosticCheckState overallState =
            diagnostics.CurrentFault != LibreHardwareMonitor.Solis.Diagnostics.DiagnosticSource.None
            ? DiagnosticCheckState.Fault
            : diagnostics.DeviceApi.State == DiagnosticCheckState.Checking ||
              diagnostics.Device.State == DiagnosticCheckState.Checking ||
              diagnostics.Codex.State == DiagnosticCheckState.Checking ||
              diagnostics.Weather.State == DiagnosticCheckState.Checking
                ? DiagnosticCheckState.Checking
                : DiagnosticCheckState.Normal;

        string overallText = overallState == DiagnosticCheckState.Normal
            ? "运行正常"
            : overallState == DiagnosticCheckState.Checking
                ? "正在检查"
                : "发现问题";
        string lastCommunication = FormatLastCommunication(
            _lastDeviceCommunicationProvider(),
            DateTimeOffset.UtcNow);
        string deviceStatus =
            diagnostics.Device.State == DiagnosticCheckState.Normal
                ? "已连接"
                : diagnostics.Device.Status;
        string deviceDetail = discovery.Device is DiscoveredDevice device
            ? $"{device.HostName}\r\n{device.IpAddress}\r\n固件 {device.FirmwareVersion}"
            : discovery.IsScanning
                ? "正在扫描局域网"
                : "尚未发现副屏";
        string apiStatus = diagnostics.DeviceApi.Status;
        const string apiDetail = "端口 18472";
        string codexStatus = diagnostics.Codex.Status;
        string codexDetail = _codexSessionsPathProvider();
        string weatherStatus = diagnostics.Weather.Status;
        string weatherDetail = string.IsNullOrWhiteSpace(snapshot.Weather.Location)
            ? "尚未获得地点"
            : string.IsNullOrWhiteSpace(snapshot.Weather.Description)
                ? snapshot.Weather.Location!
                : $"{snapshot.Weather.Location} · {snapshot.Weather.Description}";

        if (_serviceOverallStatus is not null)
        {
            SetServiceStatus(
                _serviceOverallStatus,
                overallText,
                overallState);
            _serviceLastCheckedStatus.Text = lastCommunication;
            SetServiceStatus(
                _serviceSummaryDeviceStatus,
                deviceStatus,
                diagnostics.Device.State);
            SetServiceStatus(
                _serviceDeviceStatus,
                deviceStatus,
                diagnostics.Device.State);
            _serviceDeviceDetail.Text = deviceDetail;
            SetServiceStatus(
                _serviceApiStatus,
                apiStatus,
                diagnostics.DeviceApi.State);
            _serviceApiDetail.Text = apiDetail;
            SetServiceStatus(
                _serviceCodexStatus,
                codexStatus,
                diagnostics.Codex.State);
            _serviceCodexDetail.Text = codexDetail;
            SetServiceStatus(
                _serviceWeatherStatus,
                weatherStatus,
                diagnostics.Weather.State);
            _serviceWeatherDetail.Text = weatherDetail;
            ApplyServiceStatusColors();
        }

        _serviceWpfView?.UpdateState(
            new SolisServiceViewState(
                overallText,
                overallState,
                lastCommunication,
                $"进程 {System.Diagnostics.Process.GetCurrentProcess().Id}",
                apiStatus,
                diagnostics.DeviceApi.State,
                apiDetail,
                deviceStatus,
                diagnostics.Device.State,
                deviceDetail,
                codexStatus,
                diagnostics.Codex.State,
                codexDetail,
                weatherStatus,
                diagnostics.Weather.State,
                weatherDetail));
    }

    private static string FormatLastCommunication(
        DateTimeOffset? communicationAt,
        DateTimeOffset now)
    {
        if (communicationAt is null)
            return "--";

        TimeSpan elapsed = now - communicationAt.Value;
        if (elapsed < TimeSpan.Zero || elapsed < TimeSpan.FromSeconds(5))
            return "刚刚";
        if (elapsed < TimeSpan.FromMinutes(1))
            return $"{Math.Max(1, (int)elapsed.TotalSeconds)} 秒前";
        if (elapsed < TimeSpan.FromHours(1))
            return $"{Math.Max(1, (int)elapsed.TotalMinutes)} 分钟前";
        return communicationAt.Value.ToLocalTime()
            .ToString("HH:mm:ss", CultureInfo.InvariantCulture);
    }

    private static void SetServiceStatus(
        Label label,
        string text,
        DiagnosticCheckState state)
    {
        label.Text = text;
        label.Tag = state switch
        {
            DiagnosticCheckState.Normal => "service-normal",
            DiagnosticCheckState.Fault => "service-fault",
            _ => "service-checking"
        };
    }

    private void ApplyServiceStatusColors()
    {
        foreach (Label label in new[]
                 {
                     _serviceOverallStatus,
                     _serviceSummaryDeviceStatus,
                     _serviceDeviceStatus,
                     _serviceApiStatus,
                     _serviceCodexStatus,
                     _serviceWeatherStatus
                 })
        {
            label.ForeColor = (label.Tag as string) switch
            {
                "service-normal" => Color.FromArgb(72, 199, 116),
                "service-fault" => Color.FromArgb(231, 83, 96),
                _ => SystemColors.Highlight
            };
        }
    }

    private TableLayoutPanel CreateServiceDashboardCard(int height)
    {
        var card = new SolisDashboardPanel
        {
            AutoSize = false,
            Height = height,
            Margin = new Padding(0, 0, 0, 10),
            Tag = "service-dashboard-card",
            Width = PageContentWidth
        };
        card.Padding = new Padding(12, 9, 12, 9);
        return card;
    }

    private Control CreateServiceSection(
        string title,
        Control content,
        int height)
    {
        TableLayoutPanel section = CreateServiceDashboardCard(height);
        section.ColumnCount = 1;
        section.RowCount = 2;
        section.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        section.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        Label heading = CreateTextLabel(title, 10.5f, FontStyle.Bold);
        heading.Padding = new Padding(2, 0, 0, 0);
        content.Dock = DockStyle.Fill;
        content.Margin = Padding.Empty;
        section.Controls.Add(heading, 0, 0);
        section.Controls.Add(content, 0, 1);
        return section;
    }

    private Control CreateServiceSummaryItem(
        string title,
        Label value,
        string description,
        string iconGlyph,
        Padding margin)
    {
        var item = new SolisDashboardPanel
        {
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            Margin = margin,
            Padding = new Padding(10, 7, 10, 7),
            RowCount = 1,
            Tag = "service-summary-card"
        };
        item.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42));
        item.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        SolisFluentIcon icon = CreateServiceIcon(iconGlyph, 32, 14);
        TableLayoutPanel text = new()
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            RowCount = 3
        };
        text.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        text.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        text.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Label titleLabel = CreateTextLabel(title, 9, FontStyle.Regular);
        value.Dock = DockStyle.Fill;
        value.Font = CreateFont(11.5f, FontStyle.Bold);
        value.Margin = Padding.Empty;
        value.TextAlign = ContentAlignment.MiddleLeft;
        Label descriptionLabel = CreateTextLabel(description, 8.5f, FontStyle.Regular);
        text.Controls.Add(titleLabel, 0, 0);
        text.Controls.Add(value, 0, 1);
        text.Controls.Add(descriptionLabel, 0, 2);
        item.Controls.Add(icon, 0, 0);
        item.Controls.Add(text, 1, 0);
        return item;
    }

    private Control CreateDependencyNode(
        string title,
        string description,
        string detail,
        Label status,
        string iconGlyph,
        Padding margin,
        Label? detailLabel = null)
    {
        var node = new SolisDashboardPanel
        {
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            Margin = margin,
            Padding = new Padding(10, 7, 10, 7),
            RowCount = 4,
            Tag = "service-inner-card"
        };
        node.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 36));
        node.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        node.RowStyles.Add(new RowStyle(SizeType.Absolute, 23));
        node.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        node.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        node.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        SolisFluentIcon icon = CreateServiceIcon(iconGlyph, 28, 13);
        Label titleLabel = CreateTextLabel(title, 9.5f, FontStyle.Bold);
        Label descriptionLabel = CreateTextLabel(description, 8.3f, FontStyle.Regular);
        status.Dock = DockStyle.Fill;
        status.Margin = Padding.Empty;
        status.TextAlign = ContentAlignment.MiddleLeft;
        Label actualDetail = detailLabel ?? CreateValueLabel(detail);
        actualDetail.Dock = DockStyle.Fill;
        actualDetail.AutoEllipsis = false;
        actualDetail.Margin = Padding.Empty;
        actualDetail.TextAlign = ContentAlignment.TopLeft;

        node.SetRowSpan(icon, 2);
        node.Controls.Add(icon, 0, 0);
        node.Controls.Add(titleLabel, 1, 0);
        node.Controls.Add(status, 1, 1);
        node.Controls.Add(descriptionLabel, 0, 2);
        node.SetColumnSpan(descriptionLabel, 2);
        node.Controls.Add(actualDetail, 0, 3);
        node.SetColumnSpan(actualDetail, 2);
        return node;
    }

    private Control CreateSupportingServiceCard(
        string title,
        Label status,
        string detailCaption,
        Label detail,
        string iconGlyph,
        Button? action,
        Padding margin)
    {
        var card = new SolisDashboardPanel
        {
            ColumnCount = 3,
            Dock = DockStyle.Fill,
            Margin = margin,
            Padding = new Padding(10, 7, 10, 7),
            RowCount = 4,
            Tag = "service-inner-card"
        };
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 36));
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        card.ColumnStyles.Add(new ColumnStyle(
            SizeType.Absolute,
            action is null ? 0 : 106));
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 23));
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        SolisFluentIcon icon = CreateServiceIcon(iconGlyph, 28, 13);
        Label titleLabel = CreateTextLabel(title, 9.5f, FontStyle.Bold);
        status.Dock = DockStyle.Fill;
        status.Margin = Padding.Empty;
        status.TextAlign = ContentAlignment.MiddleLeft;
        Label caption = CreateTextLabel(detailCaption, 8.3f, FontStyle.Regular);
        detail.Dock = DockStyle.Fill;
        detail.AutoEllipsis = true;
        detail.Margin = Padding.Empty;
        detail.TextAlign = ContentAlignment.TopLeft;
        card.SetRowSpan(icon, 2);
        card.Controls.Add(icon, 0, 0);
        card.Controls.Add(titleLabel, 1, 0);
        card.Controls.Add(status, 1, 1);
        card.Controls.Add(caption, 0, 2);
        card.SetColumnSpan(caption, 2);
        card.Controls.Add(detail, 0, 3);
        card.SetColumnSpan(detail, 2);

        if (action is not null)
        {
            action.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            action.Margin = new Padding(8, 0, 0, 0);
            action.Width = 98;
            card.Controls.Add(action, 2, 0);
            card.SetRowSpan(action, 4);
        }

        return card;
    }

    private Control CreateRecoveryAction(
        string title,
        string description,
        Button action,
        string iconGlyph,
        Padding margin)
    {
        var panel = new SolisDashboardPanel
        {
            ColumnCount = 3,
            Dock = DockStyle.Fill,
            Margin = margin,
            Padding = new Padding(10, 7, 10, 7),
            RowCount = 2,
            Tag = "service-inner-card"
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 36));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, TrailingControlWidth + 10));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        SolisFluentIcon icon = CreateServiceIcon(iconGlyph, 28, 13);
        Label titleLabel = CreateTextLabel(title, 9.2f, FontStyle.Bold);
        Label descriptionLabel = CreateTextLabel(description, 8.2f, FontStyle.Regular);
        action.Anchor = AnchorStyles.Right | AnchorStyles.Top;
        action.Margin = new Padding(10, 3, 0, 0);
        panel.Controls.Add(icon, 0, 0);
        panel.SetRowSpan(icon, 2);
        panel.Controls.Add(titleLabel, 1, 0);
        panel.Controls.Add(descriptionLabel, 1, 1);
        panel.Controls.Add(action, 2, 0);
        panel.SetRowSpan(action, 2);
        return panel;
    }

    private static Label CreateServiceStatusLabel(string text)
    {
        return new SolisStatusLabel
        {
            AutoEllipsis = true,
            AutoSize = false,
            Font = CreateFont(9.5f, FontStyle.Bold),
            Tag = "service-checking",
            Text = text,
            TextAlign = ContentAlignment.MiddleLeft
        };
    }

    private static SolisFluentIcon CreateServiceIcon(
        string glyph,
        int size,
        float iconSize)
    {
        return new SolisFluentIcon(glyph, iconSize)
        {
            BadgeColor = Color.FromArgb(36, SystemColors.Highlight),
            Dock = DockStyle.None,
            GlyphColor = SystemColors.Highlight,
            Height = size,
            Margin = new Padding(0, 2, 8, 0),
            Size = new Size(size, size),
            Tag = "service-icon"
        };
    }

    private static Control CreateDependencyConnector()
    {
        return new SolisFluentIcon(FluentChevronRight, 17)
        {
            Dock = DockStyle.Fill,
            GlyphColor = Color.FromArgb(72, 199, 116),
            Margin = Padding.Empty,
            Tag = "service-connector"
        };
    }
}
