#nullable enable

using System;
using System.Windows.Forms;
using LibreHardwareMonitor.Solis.Diagnostics;

namespace LibreHardwareMonitor.UI;

internal sealed partial class SolisControlCenterControl : UserControl
{

    private Control CreateDeveloperPage()
    {
        FlowLayoutPanel body = CreatePageBody(
            "开发者模式",
            "查看完整的 LibreHardwareMonitor 传感器树和高级菜单。");

        Button enterButton = CreateActionButton("进入模式", true);
        enterButton.Click += (_, _) => DeveloperModeRequested?.Invoke(this, EventArgs.Empty);

        _developerModeToggle = CreateToggle(true, true);
        _developerModeToggle.CheckedChanged += (_, _) =>
        {
            if (_developerModeToggle.Checked)
                return;

            SetDeveloperModeUnlocked(false);
            ShowPage(ControlCenterPage.Device);
        };

        Label unlocked = CreateValueLabel("已解锁 · 高级功能可用");
        body.Controls.Add(CreateStatusCard(
            "开发者入口",
            "此页面会显示上游硬件树和高级菜单，普通使用无需进入。",
            unlocked,
            "当前状态"));
        body.Controls.Add(CreateSection(
            "开发者功能",
            CreateSettingRow("完整传感器树", "打开原始硬件树、传感器菜单和高级设置", enterButton),
            CreateSettingRow("开发者入口", "关闭后隐藏本页，需要再次点击版本号 10 次", _developerModeToggle)));

        return body;
    }

    private void RefreshDiagnosticsStatus()
    {
        if (_diagnosticApiStatus is null ||
            _codexStatus is null ||
            _diagnosticWeatherStatus is null)
        {
            return;
        }

        SolisDiagnosticsSnapshot diagnostics = _diagnosticsProvider();
        _diagnosticApiStatus.Text = diagnostics.DeviceApi.Status;
        _codexStatus.Text = diagnostics.Codex.Status;
        _diagnosticWeatherStatus.Text = diagnostics.Weather.Status;
    }

    private void CopyDiagnostics()
    {
        string report = SolisDiagnosticsReport.Create(
            _diagnosticsProvider(),
            Application.ProductVersion,
            DateTimeOffset.Now);
        try
        {
            Clipboard.SetText(report);
            SolisDialog.Show(
                FindForm(),
                "Solis Monitor",
                "诊断信息已复制，敏感配置未包含。",
                SolisDialogKind.Success);
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            SolisDialog.Show(
                FindForm(),
                "Solis Monitor",
                "剪贴板当前被其他程序占用，请稍后重试。",
                SolisDialogKind.Warning);
        }
    }
}
