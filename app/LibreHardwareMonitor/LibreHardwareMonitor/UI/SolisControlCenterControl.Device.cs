#nullable enable

using System;
using System.Drawing;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows.Forms;
using LibreHardwareMonitor.Solis.DeviceControl;

namespace LibreHardwareMonitor.UI;

internal sealed partial class SolisControlCenterControl : UserControl
{
    private void InitializeDeviceStateControls()
    {
        _brightnessSlider = new SolisSlider
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            Enabled = false,
            LargeChange = 10,
            Maximum = DeviceDisplaySettings.MaximumBrightness,
            Minimum = DeviceDisplaySettings.MinimumBrightness,
            SmallChange = 10,
            Value = DeviceDisplaySettings.MaximumBrightness
        };
        _brightnessSlider.ValueChanged += (_, _) =>
        {
            _brightnessValue.Text = $"{_brightnessSlider.Value}%";
            if (_synchronizingDeviceSettings ||
                _deviceDisplaySettings is null)
            {
                return;
            }

            _deviceDisplaySettings = _deviceDisplaySettings with
            {
                BrightnessPercent = _brightnessSlider.Value
            };
            ScheduleInlineDeviceSettingsSave();
        };
        _brightnessValue = CreateValueLabel("读取中");
        _brightnessValue.AutoEllipsis = true;
        _brightnessValue.Dock = DockStyle.Fill;
        _brightnessValue.TextAlign = ContentAlignment.MiddleRight;

        _nightSettingsButton = CreateActionButton("设置", false);
        _nightSettingsButton.Click += async (_, _) =>
            await ShowNightBacklightSettingsAsync();
        _restartDeviceButton = CreateActionButton("重新启动", false);
        _restartDeviceButton.Click += async (_, _) =>
            await RestartDeviceAsync();
    }

    private Control CreateDevicePage()
    {
        FlowLayoutPanel body = CreatePageBody(
            "设备",
            "发现、配对并控制唯一的副屏设备。");

        Button setupWizardButton = CreateActionButton("设备向导", true);
        setupWizardButton.Tag = "primary-action";
        setupWizardButton.Click += (_, _) => _deviceSetupWizardRequested();
        Button clearPairingButton = CreateActionButton("清除配对", true);
        clearPairingButton.Click += (_, _) => _clearPairingRequested();
        _clearPairingRow = CreateSettingRow(
            "清除配对",
            "清除本地令牌；重连需输入 6 位配对码",
            clearPairingButton);

        body.Controls.Add(CreateStatusCard(
            "当前设备",
            "副屏与 PC 连接同一局域网后，这里显示连接状态。",
            _deviceState,
            "设备状态"));

        _connectionSection = (TableLayoutPanel)CreateSection(
            "连接与安全",
            CreateSettingRow("设备向导", "配置 Wi-Fi、发现并配对副屏", setupWizardButton),
            CreateSettingRow("安全配对", "令牌由配对流程自动同步", _pairingStatus),
            _clearPairingRow);
        Control brightnessControl = CreateBrightnessControl();
        Control displaySection = CreateSection(
            "显示与电源",
            CreateSettingRow("亮度", "拖动后自动保存", brightnessControl),
            CreateSettingRow("夜间背光", "设置关闭背光的时间范围", _nightSettingsButton),
            CreateSettingRow("远程重启", "仅重启副屏", _restartDeviceButton));

        var columns = new TableLayoutPanel
        {
            ColumnCount = 2,
            Height = 270,
            Margin = new Padding(0, 0, 0, 10),
            Padding = Padding.Empty,
            RowCount = 1,
            Width = PageContentWidth
        };
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        columns.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _connectionSection.Dock = DockStyle.Fill;
        _connectionSection.Margin = new Padding(0, 0, 6, 0);
        displaySection.Dock = DockStyle.Fill;
        displaySection.Margin = new Padding(6, 0, 0, 0);
        columns.Controls.Add(_connectionSection, 0, 0);
        columns.Controls.Add(displaySection, 1, 0);
        body.Controls.Add(columns);

        return body;
    }

    private Control CreateBrightnessControl()
    {
        var panel = new TableLayoutPanel
        {
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            Height = TrailingControlHeight,
            RowCount = 1,
            Tag = "inline-setting"
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        panel.Controls.Add(_brightnessSlider, 0, 0);
        panel.Controls.Add(_brightnessValue, 1, 0);
        return panel;
    }

    private async Task LoadInlineDeviceSettingsAsync()
    {
        _deviceSettingsLoading = true;
        if (_brightnessValue is not null)
            _brightnessValue.Text = "读取中";
        DeviceControlResult loaded;
        try
        {
            loaded = await _deviceSettingsLoader();
        }
        finally
        {
            _deviceSettingsLoading = false;
        }

        if (!loaded.Success || loaded.Settings is null)
        {
            if (_brightnessValue is not null)
                _brightnessValue.Text = "读取失败";
            RefreshStatus();
            return;
        }
        ApplyInlineDeviceSettings(loaded.Settings);
        RefreshStatus();
    }

    private void ApplyInlineDeviceSettings(DeviceDisplaySettings settings)
    {
        _synchronizingDeviceSettings = true;
        try
        {
            _deviceDisplaySettings = settings;
            if (_brightnessSlider is not null)
                _brightnessSlider.Value = settings.BrightnessPercent;
            if (_brightnessValue is not null)
                _brightnessValue.Text = $"{settings.BrightnessPercent}%";
        }
        finally
        {
            _synchronizingDeviceSettings = false;
        }
    }

    private void ScheduleInlineDeviceSettingsSave()
    {
        _deviceSettingsDirty = true;
        _deviceSettingsSaveTimer.Stop();
        _deviceSettingsSaveTimer.Start();
    }

    private async Task SaveInlineDeviceSettingsAsync()
    {
        _deviceSettingsSaveTimer.Stop();
        if (_deviceSettingsSaveRunning ||
            _deviceDisplaySettings is null)
        {
            return;
        }

        _deviceSettingsSaveRunning = true;
        try
        {
            while (_deviceSettingsDirty &&
                   _deviceDisplaySettings is not null)
            {
                _deviceSettingsDirty = false;
                DeviceDisplaySettings saving = _deviceDisplaySettings;
                if (_brightnessValue is not null)
                    _brightnessValue.Text =
                        $"{saving.BrightnessPercent}%…";
                DeviceControlResult result =
                    await _deviceSettingsSaver(saving);
                if (!result.Success)
                {
                    if (_brightnessValue is not null)
                        _brightnessValue.Text = "保存失败";
                    return;
                }
                if (_brightnessValue is not null)
                    _brightnessValue.Text =
                        $"{saving.BrightnessPercent}%";
            }
        }
        finally
        {
            _deviceSettingsSaveRunning = false;
        }
    }

    private async Task ShowNightBacklightSettingsAsync()
    {
        if (_deviceDisplaySettings is null)
            return;

        using var form = new NightBacklightSettingsForm(
            _deviceDisplaySettings);
        if (form.ShowDialog(FindForm()) != DialogResult.OK)
            return;

        DeviceDisplaySettings settings = _deviceDisplaySettings with
        {
            NightEnabled = form.NightEnabled,
            NightStartMinute = form.NightStartMinute,
            NightEndMinute = form.NightEndMinute,
            UtcOffsetMinutes = (int)Math.Round(
                TimeZoneInfo.Local.GetUtcOffset(
                    DateTimeOffset.Now).TotalMinutes)
        };

        _deviceControlBusy = true;
        SetDeviceControlsEnabled(false);
        try
        {
            DeviceControlResult result =
                await _deviceSettingsSaver(settings);
            if (!result.Success)
            {
                SolisDialog.Show(
                    FindForm(),
                    "夜间背光",
                    result.Message,
                    SolisDialogKind.Warning);
                return;
            }

            ApplyInlineDeviceSettings(settings);
            SolisDialog.Show(
                FindForm(),
                "夜间背光",
                settings.NightEnabled
                    ? $"夜间背光已启用：{FormatMinute(settings.NightStartMinute)}–{FormatMinute(settings.NightEndMinute)}。"
                    : "夜间背光已关闭。",
                SolisDialogKind.Success);
        }
        finally
        {
            _deviceControlBusy = false;
            RefreshStatus();
        }
    }

    private static string FormatMinute(int minuteOfDay) =>
        $"{minuteOfDay / 60:00}:{minuteOfDay % 60:00}";

    private async Task RestartDeviceAsync()
    {
        if (SolisDialog.Confirm(
                FindForm(),
                "远程重启",
                "确定要重新启动副屏吗？PC 后台服务不会受到影响。",
                "重新启动",
                "取消") != DialogResult.OK)
        {
            return;
        }

        _deviceControlBusy = true;
        SetDeviceControlsEnabled(false);
        DeviceControlResult result;
        try
        {
            result = await _deviceRestarter();
        }
        finally
        {
            _deviceControlBusy = false;
        }

        if (!result.Success)
        {
            RefreshStatus();
            SolisDialog.Show(
                FindForm(),
                "远程重启",
                result.Message,
                SolisDialogKind.Warning);
            return;
        }

        _deviceRestartPending = true;
        if (_restartDeviceButton is not null)
            _restartDeviceButton.Text = "正在重启";
        RefreshStatus();
        DeviceControlResult? ready = await WaitForRestartAsync();
        if (ready?.Success == true && ready.Settings is not null)
            ApplyInlineDeviceSettings(ready.Settings);
        _deviceRestartPending = false;
        if (_restartDeviceButton is not null)
            _restartDeviceButton.Text = "重新启动";
        RefreshStatus();

        SolisDialog.Show(
            FindForm(),
            "远程重启",
            ready?.Success == true
                ? "副屏已重新上线。"
                : "副屏尚未重新上线，请稍后重试。",
            ready?.Success == true
                ? SolisDialogKind.Success
                : SolisDialogKind.Warning);
    }

    private async Task<DeviceControlResult?> WaitForRestartAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(2));
        DeviceControlResult? last = null;
        for (int attempt = 0; attempt < 20; attempt++)
        {
            last = await _deviceSettingsLoader();
            if (last.Success)
                return last;
            await Task.Delay(TimeSpan.FromSeconds(1));
        }
        return last;
    }

    private void SetDeviceControlsEnabled(bool enabled)
    {
        bool settingsAvailable =
            enabled && _deviceDisplaySettings is not null;
        _deviceSettingsAvailable = settingsAvailable;
        _deviceCanControl = enabled;
        if (_brightnessSlider is not null)
            _brightnessSlider.Enabled = settingsAvailable;
        if (_nightSettingsButton is not null)
            _nightSettingsButton.Enabled = settingsAvailable;
        if (_restartDeviceButton is not null)
            _restartDeviceButton.Enabled = enabled;
    }
}
