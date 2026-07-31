#nullable enable

using System.Drawing;
using System.Windows.Forms;

namespace LibreHardwareMonitor.UI;

internal sealed class RestoreDefaultsConfirmationForm : SolisDialogForm
{
    public RestoreDefaultsConfirmationForm()
        : base("恢复默认设置", new Size(620, 500))
    {
        var root = new TableLayoutPanel
        {
            BackColor = Palette.Canvas,
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Padding = new Padding(24, 20, 24, 18),
            RowCount = 4
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        root.Controls.Add(CreateDialogHeader(
            "\uE7BA",
            "恢复 Solis Monitor 默认设置？",
            "此操作会清除当前用户配置并重新启动程序。"), 0, 0);

        SolisDialogCard card = CreateCard(new Padding(18, 12, 18, 12));
        var items = new TableLayoutPanel
        {
            BackColor = Palette.Surface,
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            RowCount = 5
        };
        for (int i = 0; i < 4; i++)
            items.RowStyles.Add(new RowStyle(SizeType.Percent, 20));
        items.RowStyles.Add(new RowStyle(SizeType.Percent, 20));
        items.Controls.Add(CreateResetItem(
            "当前副屏",
            "设备记录、配对关系和设备令牌"), 0, 0);
        items.Controls.Add(CreateResetItem(
            "天气服务",
            "API Host、API Key 和经纬度"), 0, 1);
        items.Controls.Add(CreateResetItem(
            "Windows 启动",
            "开机启动、静默启动和计划任务"), 0, 2);
        items.Controls.Add(CreateResetItem(
            "高级设置",
            "开发者模式解锁状态及其他 Solis 用户配置"), 0, 3);
        Label preserved = CreateLabel(
            "不会删除程序文件、固件文件、日志或 LibreHardwareMonitor 传感器配置。",
            9,
            FontStyle.Regular,
            Palette.Success,
            ContentAlignment.MiddleLeft);
        preserved.BackColor = Palette.Surface;
        preserved.Padding = new Padding(4, 4, 4, 0);
        items.Controls.Add(preserved, 0, 4);
        card.Controls.Add(items);
        root.Controls.Add(card, 0, 1);

        Label warning = CreateLabel(
            "恢复后程序会重新启动并进入设备页，不会自动打开设备向导。",
            9,
            FontStyle.Regular,
            Palette.Warning,
            ContentAlignment.MiddleLeft);
        warning.BackColor = Palette.Canvas;
        warning.Padding = new Padding(4, 4, 0, 0);
        root.Controls.Add(warning, 0, 2);

        SolisDialogButton restoreButton = CreateButton(
            "恢复并重新启动",
            SolisButtonKind.Danger,
            168);
        restoreButton.DialogResult = DialogResult.OK;
        SolisDialogButton cancelButton = CreateButton(
            "取消",
            SolisButtonKind.Secondary);
        cancelButton.DialogResult = DialogResult.Cancel;
        root.Controls.Add(CreateFooter(restoreButton, cancelButton), 0, 3);

        Controls.Add(root);
        AcceptButton = cancelButton;
        CancelButton = cancelButton;
        ActiveControl = cancelButton;
    }

    private TableLayoutPanel CreateResetItem(string title, string description)
    {
        var item = new TableLayoutPanel
        {
            BackColor = Palette.Surface,
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = new Padding(2, 2, 2, 2),
            RowCount = 1
        };
        item.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42));
        item.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var icon = new SolisDialogIcon("\uEA39", Palette.Danger)
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 6, 10, 6)
        };
        var text = new TableLayoutPanel
        {
            BackColor = Palette.Surface,
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            RowCount = 2
        };
        text.RowStyles.Add(new RowStyle(SizeType.Percent, 48));
        text.RowStyles.Add(new RowStyle(SizeType.Percent, 52));
        Label titleLabel = CreateLabel(
            title,
            9.5f,
            FontStyle.Bold,
            Palette.TextPrimary,
            ContentAlignment.BottomLeft);
        titleLabel.BackColor = Palette.Surface;
        Label descriptionLabel = CreateLabel(
            description,
            8.5f,
            FontStyle.Regular,
            Palette.TextSecondary,
            ContentAlignment.TopLeft);
        descriptionLabel.BackColor = Palette.Surface;
        text.Controls.Add(titleLabel, 0, 0);
        text.Controls.Add(descriptionLabel, 0, 1);
        item.Controls.Add(icon, 0, 0);
        item.Controls.Add(text, 1, 0);
        return item;
    }
}
