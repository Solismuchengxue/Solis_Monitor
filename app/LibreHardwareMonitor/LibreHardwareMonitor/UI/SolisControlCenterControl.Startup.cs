#nullable enable

using System;
using System.Windows.Forms;

namespace LibreHardwareMonitor.UI;

internal sealed partial class SolisControlCenterControl : UserControl
{
    private void InitializeStartupStateControls()
    {
        _silentStartupToggle = CreateToggle(
            _startupSettings.SilentStartup,
            true);
        _silentStartupToggle.CheckedChanged += (_, _) =>
        {
            if (_synchronizingStartupToggles)
                return;

            _startupSettings.SetSilentStartup(
                _silentStartupToggle.Checked);
            RefreshStartupSettings();
        };

        _autoStartToggle = CreateToggle(_startupSettings.AutoStart, true);
        _autoStartToggle.CheckedChanged += (_, _) =>
        {
            if (_synchronizingStartupToggles)
                return;

            _startupSettings.SetAutoStart(_autoStartToggle.Checked);
            RefreshStartupSettings();
        };
        _startupSummaryStatus = CreateValueLabel("正在读取启动策略");
    }

    private Control CreateStartupPage()
    {
        FlowLayoutPanel body = CreatePageBody(
            "启动与托盘",
            "控制 Solis Monitor 如何随 Windows 启动。");

        body.Controls.Add(CreateStatusCard(
            "后台运行",
            "关闭或最小化窗口后继续采集并向副屏提供数据。",
            _startupSummaryStatus,
            "当前策略"));

        body.Controls.Add(CreateSection(
            "Windows 启动",
            CreateSettingRow("静默启动", "只影响 Windows 开机自动启动；手动打开仍显示控制台", _silentStartupToggle),
            CreateSettingRow("开机启动", "使用最高权限计划任务，首次启用需要管理员授权", _autoStartToggle)));

        body.Controls.Add(CreateNote(
            "关闭或最小化窗口时，Solis Monitor 会隐藏到托盘。\n需要完全退出时，请使用托盘菜单中的“退出”。"));

        return body;
    }

    public void RefreshStartupSettings()
    {
        if (_wpfShell is not null)
        {
            RefreshWpfStartup();
            return;
        }

        if (_silentStartupToggle == null || _autoStartToggle == null)
            return;

        _synchronizingStartupToggles = true;
        try
        {
            bool silent = _startupSettings.SilentStartup;
            bool autoStart = _startupSettings.AutoStart;
            SetToggleValue(_silentStartupToggle, silent);
            SetToggleValue(_autoStartToggle, autoStart);
            if (_startupSummaryStatus != null)
                _startupSummaryStatus.Text =
                    _startupSettings.GetSummary();
        }
        finally
        {
            _synchronizingStartupToggles = false;
        }
    }
}
