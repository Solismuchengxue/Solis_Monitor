#nullable enable

using System;
using System.Drawing;
using System.Windows.Forms;
using LibreHardwareMonitor.Solis.DeviceControl;

namespace LibreHardwareMonitor.UI;

internal sealed class NightBacklightSettingsForm : SolisDialogForm
{
    private readonly CheckBox _enabled;
    private readonly DateTimePicker _start;
    private readonly DateTimePicker _end;
    private readonly Label _status;

    public NightBacklightSettingsForm(DeviceDisplaySettings existing)
        : base("夜间背光", new Size(580, 370))
    {
        var layout = new TableLayoutPanel
        {
            BackColor = Palette.Canvas,
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Padding = new Padding(24, 20, 24, 18),
            RowCount = 4
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        layout.Controls.Add(CreateDialogHeader(
            "\uE708",
            "夜间背光",
            "按本地时间自动关闭副屏背光。"), 0, 0);

        SolisDialogCard card = CreateCard(new Padding(18, 16, 18, 16));
        var settings = new TableLayoutPanel
        {
            BackColor = Palette.Surface,
            ColumnCount = 4,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            RowCount = 3
        };
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 32));
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
        settings.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        settings.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        settings.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

        _enabled = new CheckBox
        {
            Checked = existing.NightEnabled,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Text = "启用夜间背光",
            TextAlign = ContentAlignment.MiddleLeft
        };
        StyleCheckBox(_enabled);
        settings.Controls.Add(_enabled, 0, 0);
        settings.SetColumnSpan(_enabled, 4);
        settings.Controls.Add(CreateCaption("开始"), 1, 1);
        settings.Controls.Add(CreateCaption("结束"), 3, 1);
        _start = CreateTimeInput(existing.NightStartMinute);
        _end = CreateTimeInput(existing.NightEndMinute);
        settings.Controls.Add(_start, 1, 2);
        Label separator = CreateLabel(
            "–",
            10,
            FontStyle.Regular,
            Palette.TextSecondary,
            ContentAlignment.MiddleCenter);
        settings.Controls.Add(separator, 2, 2);
        settings.Controls.Add(_end, 3, 2);
        card.Controls.Add(settings);
        layout.Controls.Add(card, 0, 1);

        _status = CreateLabel(
            "时间按 PC 当前时区同步，保存后副屏可独立执行。",
            9,
            FontStyle.Regular,
            Palette.TextSecondary,
            ContentAlignment.MiddleLeft);
        _status.BackColor = Palette.Canvas;
        _status.Padding = new Padding(4, 0, 0, 0);
        layout.Controls.Add(_status, 0, 2);

        SolisDialogButton saveButton = CreateButton(
            "保存设置",
            SolisButtonKind.Primary);
        saveButton.Click += SaveClick;
        SolisDialogButton cancelButton = CreateButton(
            "取消",
            SolisButtonKind.Secondary);
        cancelButton.DialogResult = DialogResult.Cancel;
        layout.Controls.Add(CreateFooter(saveButton, cancelButton), 0, 3);

        _enabled.CheckedChanged += (_, _) => UpdateInputState();
        UpdateInputState();
        AcceptButton = saveButton;
        CancelButton = cancelButton;
        Controls.Add(layout);
    }

    public bool NightEnabled => _enabled.Checked;

    public int NightStartMinute { get; private set; }

    public int NightEndMinute { get; private set; }

    private void UpdateInputState()
    {
        _start.Enabled = _enabled.Checked;
        _end.Enabled = _enabled.Checked;
        _status.ForeColor = Palette.TextSecondary;
        _status.Text = _enabled.Checked
            ? "时间按 PC 当前时区同步，保存后副屏可独立执行。"
            : "关闭后保留当前时间范围，但副屏不会执行夜间熄屏。";
    }

    private void SaveClick(object? sender, EventArgs e)
    {
        int start = (_start.Value.Hour * 60) + _start.Value.Minute;
        int end = (_end.Value.Hour * 60) + _end.Value.Minute;
        if (start == end)
        {
            ShowError("开始时间和结束时间不能相同。");
            return;
        }

        NightStartMinute = start;
        NightEndMinute = end;
        DialogResult = DialogResult.OK;
        Close();
    }

    private void ShowError(string message)
    {
        _status.ForeColor = Palette.Danger;
        _status.Text = message;
    }

    private Label CreateCaption(string text)
    {
        Label label = CreateLabel(
            text,
            8.5f,
            FontStyle.Regular,
            Palette.TextSecondary,
            ContentAlignment.MiddleCenter);
        label.BackColor = Palette.Surface;
        return label;
    }

    private DateTimePicker CreateTimeInput(int minuteOfDay)
    {
        return new DateTimePicker
        {
            CalendarForeColor = Palette.TextPrimary,
            CalendarMonthBackground = Palette.SurfaceRaised,
            CustomFormat = "HH:mm",
            Dock = DockStyle.Fill,
            Font = new Font(
                "Segoe UI Variable Text",
                11,
                FontStyle.Bold,
                GraphicsUnit.Point),
            Format = DateTimePickerFormat.Custom,
            Margin = new Padding(0, 0, 0, 2),
            ShowUpDown = true,
            Value = DateTime.Today.AddMinutes(minuteOfDay)
        };
    }
}
