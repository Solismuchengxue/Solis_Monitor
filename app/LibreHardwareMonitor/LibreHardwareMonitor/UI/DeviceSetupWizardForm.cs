#nullable enable

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using LibreHardwareMonitor.Solis.DeviceControl;

namespace LibreHardwareMonitor.UI;

internal sealed class DeviceSetupWizardForm : SolisDialogForm
{
    private readonly Func<DeviceDiscoveryState> _deviceProvider;
    private readonly Func<IReadOnlyList<DiscoveredDevice>> _candidateProvider;
    private readonly Action _scanRequested;
    private readonly Func<DiscoveredDevice, string, CancellationToken, Task<DevicePairingResult>>
        _pairRequested;
    private readonly Action _beginProvisioningMaintenance;
    private readonly Panel _content = new();
    private readonly Label _status = new();
    private readonly ListBox _deviceList = new();
    private readonly Button _pairButton;
    private IReadOnlyList<DiscoveredDevice> _displayedCandidates =
        Array.Empty<DiscoveredDevice>();
    private readonly System.Windows.Forms.Timer _refreshTimer;

    public DeviceSetupWizardForm(
        Func<DeviceDiscoveryState> deviceProvider,
        Func<IReadOnlyList<DiscoveredDevice>> candidateProvider,
        Action scanRequested,
        Func<DiscoveredDevice, string, CancellationToken, Task<DevicePairingResult>>
            pairRequested,
        Action beginProvisioningMaintenance)
        : base("设备向导", new Size(760, 580))
    {
        _deviceProvider = deviceProvider ??
                          throw new ArgumentNullException(nameof(deviceProvider));
        _candidateProvider = candidateProvider ??
                             throw new ArgumentNullException(nameof(candidateProvider));
        _scanRequested = scanRequested ??
                         throw new ArgumentNullException(nameof(scanRequested));
        _pairRequested = pairRequested ??
                         throw new ArgumentNullException(nameof(pairRequested));
        _beginProvisioningMaintenance = beginProvisioningMaintenance ??
                                        throw new ArgumentNullException(
                                            nameof(beginProvisioningMaintenance));

        ShowInTaskbar = true;

        _content.Dock = DockStyle.Fill;
        _content.BackColor = Palette.Canvas;
        _content.Padding = new Padding(32, 24, 32, 22);
        Controls.Add(_content);

        _status.AutoSize = false;
        _status.BackColor = Palette.SurfaceRaised;
        _status.ForeColor = Palette.TextPrimary;
        _status.Font = new Font(
            "Segoe UI Variable Text",
            9.5f,
            FontStyle.Bold,
            GraphicsUnit.Point);
        _status.Height = 72;
        _status.Margin = new Padding(0, 14, 0, 12);
        _status.Padding = new Padding(16, 10, 16, 10);
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.Width = 680;

        _deviceList.BackColor = Palette.Surface;
        _deviceList.BorderStyle = BorderStyle.None;
        _deviceList.DrawMode = DrawMode.OwnerDrawFixed;
        _deviceList.ForeColor = Palette.TextPrimary;
        _deviceList.Height = 190;
        _deviceList.ItemHeight = 58;
        _deviceList.IntegralHeight = false;
        _deviceList.Margin = new Padding(0, 0, 0, 12);
        _deviceList.Width = 680;
        _deviceList.DrawItem += DrawDeviceItem;
        _deviceList.SelectedIndexChanged += (_, _) => UpdatePairButton();

        _pairButton = CreateButton("连接");
        _pairButton.Enabled = false;
        _pairButton.Click += async (_, _) => await PairSelectedDeviceAsync();

        ShowChoice();

        _refreshTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _refreshTimer.Tick += (_, _) => RefreshDeviceStatus();
        _refreshTimer.Start();
        FormClosed += (_, _) => _refreshTimer.Dispose();
    }

    private void ShowChoice()
    {
        FlowLayoutPanel body = CreateBody(
            "设备向导",
            "选择副屏当前的网络状态。天气和启动设置在各自页面维护。");

        Button connected = CreateChoiceButton(
            "副屏已连接 Wi-Fi",
            "进入局域网发现和安全配对。");
        connected.Click += (_, _) =>
        {
            _scanRequested();
            ShowDiscovery();
        };
        body.Controls.Add(connected);

        Button configure = CreateChoiceButton(
            "配置或更换 Wi-Fi",
            "长按 GPIO21，通过副屏 AP WebUI 完成配网。");
        configure.Click += (_, _) =>
        {
            _beginProvisioningMaintenance();
            ShowProvisioning();
        };
        body.Controls.Add(configure);
        ShowContent(body);
    }

    private void ShowDiscovery()
    {
        FlowLayoutPanel body = CreateBody(
            "发现并配对副屏",
            "双击 GPIO21 让副屏进入“开启发现”，然后在这里等待设备出现。");
        body.Controls.Add(_status);
        body.Controls.Add(_deviceList);
        body.SetFlowBreak(_deviceList, true);

        Button scan = CreateButton("发现设备");
        scan.Click += (_, _) =>
        {
            _scanRequested();
            RefreshDeviceStatus();
        };
        body.Controls.Add(scan);
        body.Controls.Add(_pairButton);

        Button back = CreateButton("返回");
        back.Margin = new Padding(12, 0, 0, 0);
        back.Click += (_, _) => ShowChoice();
        body.Controls.Add(back);

        ShowContent(body);
        RefreshDeviceStatus();
    }

    private void ShowProvisioning()
    {
        FlowLayoutPanel body = CreateBody(
            "配置或更换 Wi-Fi",
            "PC 已进入 10 分钟维护状态，期间暂停副屏离线通知。");

        body.Controls.Add(CreateBodyLabel(
            "1. 长按副屏 GPIO21 约 5 秒，进入 AP 配网。\r\n" +
            "2. 手机连接屏幕显示的 Solis-Monitor-xxxx 热点。\r\n" +
            "3. 关闭手机移动数据，打开 http://192.168.0.1/。\r\n" +
            "4. 选择 Wi-Fi、输入密码并保存。\r\n\r\n" +
            "副屏重新连入局域网后，点击下面的按钮继续发现与配对。"));

        Button continueButton = CreateButton("Wi-Fi 已连接，继续");
        continueButton.Width = 200;
        continueButton.Click += (_, _) =>
        {
            _scanRequested();
            ShowDiscovery();
        };
        body.Controls.Add(continueButton);

        Button back = CreateButton("返回");
        back.Margin = new Padding(12, 0, 0, 0);
        back.Click += (_, _) => ShowChoice();
        body.Controls.Add(back);
        ShowContent(body);
    }

    private void RefreshDeviceStatus()
    {
        if (_status.Parent is null)
            return;

        DeviceDiscoveryState discovery = _deviceProvider();
        RefreshCandidateList();
        if (_displayedCandidates.Any(candidate => candidate.PairingActive))
        {
            _status.Text =
                $"发现 {_displayedCandidates.Count(candidate => candidate.PairingActive)} " +
                "台处于开启发现状态的副屏，请选择设备并连接。";
            return;
        }
        if (discovery.Device is DiscoveredDevice device)
        {
            _status.Text = device.Paired
                ? $"已发现并完成配对：{device.HostName}\r\n{device.IpAddress}"
                : $"已发现 {device.HostName}\r\n请确认副屏仍处于开启发现状态";
            return;
        }

        _status.Text = discovery.IsScanning
            ? "正在扫描当前局域网…"
            : _displayedCandidates.Count > 0
                ? $"发现 {_displayedCandidates.Count} 台处于开启发现状态的副屏，请选择设备并连接。"
                : "尚未发现副屏，请确认电脑和副屏连接同一局域网。";
    }

    private void RefreshCandidateList()
    {
        IReadOnlyList<DiscoveredDevice> candidates = _candidateProvider();
        string? selectedIp = _deviceList.SelectedIndex >= 0 &&
                             _deviceList.SelectedIndex < _displayedCandidates.Count
            ? _displayedCandidates[_deviceList.SelectedIndex].IpAddress
            : null;
        string currentSignature = string.Join(
            "|",
            _displayedCandidates.Select(DeviceSignature));
        string nextSignature = string.Join("|", candidates.Select(DeviceSignature));
        if (string.Equals(currentSignature, nextSignature, StringComparison.Ordinal))
            return;

        _displayedCandidates = candidates.ToArray();
        _deviceList.BeginUpdate();
        _deviceList.Items.Clear();
        foreach (DiscoveredDevice candidate in _displayedCandidates)
        {
            _deviceList.Items.Add(
                $"{candidate.HostName}    {candidate.IpAddress}" +
                (candidate.PairingActive
                    ? "    等待配对"
                    : candidate.Paired
                        ? "    已配对"
                        : string.Empty));
        }
        _deviceList.EndUpdate();
        int selectedIndex = selectedIp is null
            ? (_displayedCandidates.Count == 1 ? 0 : -1)
            : _displayedCandidates
                .Select((candidate, index) => (candidate, index))
                .Where(item => string.Equals(
                    item.candidate.IpAddress,
                    selectedIp,
                    StringComparison.Ordinal))
                .Select(item => item.index)
                .DefaultIfEmpty(-1)
                .First();
        _deviceList.SelectedIndex = selectedIndex;
        UpdatePairButton();
    }

    private static string DeviceSignature(DiscoveredDevice device) =>
        $"{device.HostName}\0{device.IpAddress}\0{device.Paired}\0{device.PairingActive}";

    private void UpdatePairButton()
    {
        bool canPair = _deviceList.SelectedIndex >= 0 &&
                       _deviceList.SelectedIndex < _displayedCandidates.Count &&
                       _displayedCandidates[_deviceList.SelectedIndex].PairingActive;
        _pairButton.Enabled = canPair;
    }

    private async Task PairSelectedDeviceAsync()
    {
        int index = _deviceList.SelectedIndex;
        if (index < 0 || index >= _displayedCandidates.Count)
            return;

        DiscoveredDevice device = _displayedCandidates[index];
        string? code = ShowPairingCodeDialog(device);
        if (code is null)
            return;

        _pairButton.Enabled = false;
        _status.Text = $"正在与 {device.HostName} 配对…";
        DevicePairingResult result = await _pairRequested(
            device,
            code,
            CancellationToken.None);
        if (IsDisposed)
            return;

        RefreshDeviceStatus();
        if (!result.Success)
        {
            SolisDialog.Show(
                this,
                "设备配对",
                result.ErrorMessage ?? "配对失败，请重试。",
                SolisDialogKind.Warning);
        }
    }

    private string? ShowPairingCodeDialog(DiscoveredDevice device)
    {
        using var dialog = new PairingCodeDialog(device);
        return dialog.ShowDialog(this) == DialogResult.OK
            ? dialog.PairingCode
            : null;
    }

    private FlowLayoutPanel CreateBody(string title, string subtitle)
    {
        var body = new FlowLayoutPanel
        {
            AutoScroll = false,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = Padding.Empty,
            WrapContents = true
        };
        TableLayoutPanel header = CreateDialogHeader(
            "\uE772",
            title,
            subtitle);
        header.Height = 84;
        header.Margin = new Padding(0, 0, 0, 8);
        header.Width = 680;
        body.Controls.Add(header);
        body.SetFlowBreak(header, true);
        return body;
    }

    private Button CreateChoiceButton(string title, string description)
    {
        Button button = CreateButton($"{title}\r\n{description}");
        button.Font = new Font(
            "Segoe UI Variable Text",
            10,
            FontStyle.Regular,
            GraphicsUnit.Point);
        button.Height = 84;
        button.Margin = new Padding(0, 12, 0, 0);
        button.Padding = new Padding(18, 0, 18, 0);
        button.TextAlign = ContentAlignment.MiddleLeft;
        button.Width = 680;
        return button;
    }

    private Button CreateButton(string text)
    {
        return base.CreateButton(text, SolisButtonKind.Secondary, 160);
    }

    private Control CreateBodyLabel(string text)
    {
        Label label = CreateLabel(
            text,
            9.5f,
            FontStyle.Regular,
            Palette.TextPrimary,
            ContentAlignment.TopLeft);
        label.AutoEllipsis = false;
        label.BackColor = Palette.Surface;
        label.BackColor = Palette.Surface;
        var card = new SolisDialogCard(Palette)
        {
            Height = 220,
            Margin = new Padding(0, 14, 0, 14),
            Padding = new Padding(18, 16, 18, 16),
            Width = 680
        };
        card.Controls.Add(label);
        return card;
    }

    private void ShowContent(Control control)
    {
        _content.SuspendLayout();
        _content.Controls.Clear();
        _content.Controls.Add(control);
        _content.ResumeLayout();
    }

    private void DrawDeviceItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _displayedCandidates.Count)
            return;

        DiscoveredDevice device = _displayedCandidates[e.Index];
        bool selected = (e.State & DrawItemState.Selected) != 0;
        Color background = selected
            ? SolisUiPalette.Blend(Palette.SurfaceRaised, Palette.Accent, 0.22f)
            : Palette.SurfaceRaised;
        using var backgroundBrush = new SolidBrush(background);
        e.Graphics.FillRectangle(backgroundBrush, e.Bounds);
        Rectangle iconBounds = new(e.Bounds.Left + 14, e.Bounds.Top + 12, 34, 34);
        using var iconBrush = new SolidBrush(
            device.PairingActive ? Palette.Accent : Palette.TextSecondary);
        e.Graphics.FillEllipse(iconBrush, iconBounds);

        Rectangle titleBounds = new(e.Bounds.Left + 62, e.Bounds.Top + 8, e.Bounds.Width - 210, 24);
        Rectangle detailBounds = new(e.Bounds.Left + 62, e.Bounds.Top + 31, e.Bounds.Width - 210, 20);
        Rectangle statusBounds = new(e.Bounds.Right - 138, e.Bounds.Top + 8, 120, 42);
        using var titleFont = new Font(
            "Segoe UI Variable Text",
            10,
            FontStyle.Bold,
            GraphicsUnit.Point);
        using var detailFont = new Font(
            "Segoe UI Variable Text",
            8.5f,
            FontStyle.Regular,
            GraphicsUnit.Point);
        using var statusFont = new Font(
            "Segoe UI Variable Text",
            9,
            FontStyle.Bold,
            GraphicsUnit.Point);
        TextRenderer.DrawText(
            e.Graphics,
            device.HostName,
            titleFont,
            titleBounds,
            Palette.TextPrimary,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(
            e.Graphics,
            device.IpAddress,
            detailFont,
            detailBounds,
            Palette.TextSecondary,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        string status = device.PairingActive
            ? "等待配对"
            : device.Paired
                ? "已配对"
                : "不可配对";
        TextRenderer.DrawText(
            e.Graphics,
            status,
            statusFont,
            statusBounds,
            device.PairingActive ? Palette.Accent : Palette.TextSecondary,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        using var borderPen = new Pen(Palette.Border);
        e.Graphics.DrawLine(
            borderPen,
            e.Bounds.Left + 12,
            e.Bounds.Bottom - 1,
            e.Bounds.Right - 12,
            e.Bounds.Bottom - 1);
    }
}

internal sealed class PairingCodeDialog : SolisDialogForm
{
    private readonly TextBox _code;
    private readonly Label _status;

    public PairingCodeDialog(DiscoveredDevice device)
        : base("输入配对码", new Size(520, 360))
    {
        var root = new TableLayoutPanel
        {
            BackColor = Palette.Canvas,
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Padding = new Padding(24, 20, 24, 18),
            RowCount = 4
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        root.Controls.Add(CreateDialogHeader(
            "\uE72E",
            "输入 6 位配对码",
            $"请输入 {device.HostName} 屏幕显示的配对码。"), 0, 0);

        SolisDialogCard card = CreateCard(new Padding(24, 28, 24, 28));
        _code = new TextBox
        {
            Dock = DockStyle.Fill,
            Font = new Font(
                "Segoe UI Variable Display",
                24,
                FontStyle.Bold,
                GraphicsUnit.Point),
            MaxLength = DevicePairingProtocol.CodeLength,
            TextAlign = HorizontalAlignment.Center
        };
        StyleTextBox(_code);
        card.Controls.Add(_code);
        root.Controls.Add(card, 0, 1);

        _status = CreateLabel(
            "配对码每 60 秒刷新一次，请以副屏当前显示为准。",
            8.8f,
            FontStyle.Regular,
            Palette.TextSecondary,
            ContentAlignment.MiddleLeft);
        _status.BackColor = Palette.Canvas;
        root.Controls.Add(_status, 0, 2);

        SolisDialogButton confirm = CreateButton(
            "确认配对",
            SolisButtonKind.Primary);
        confirm.Click += ConfirmClick;
        SolisDialogButton cancel = CreateButton(
            "取消",
            SolisButtonKind.Secondary);
        cancel.DialogResult = DialogResult.Cancel;
        root.Controls.Add(CreateFooter(confirm, cancel), 0, 3);
        Controls.Add(root);
        AcceptButton = confirm;
        CancelButton = cancel;
        Shown += (_, _) => _code.Focus();
    }

    public string PairingCode => _code.Text;

    private void ConfirmClick(object? sender, EventArgs e)
    {
        if (!DevicePairingProtocol.IsValidCode(_code.Text))
        {
            _status.ForeColor = Palette.Danger;
            _status.Text = "请输入副屏显示的 6 位数字配对码。";
            _code.SelectAll();
            _code.Focus();
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }
}
