#nullable enable

using System;
using System.Windows.Forms;
using LibreHardwareMonitor.Solis.Firmware;

namespace LibreHardwareMonitor.UI;

internal sealed partial class SolisControlCenterControl : UserControl
{
    private void InitializeFirmwareStateControls()
    {
        _firmwareStatus = CreateValueLabel("尚未选择固件");
        _firmwareStatus.AutoEllipsis = false;
        _firmwareSelectButton = CreateActionButton("选择固件", true);
        _firmwareSelectButton.Tag = "primary-action";
        _firmwareSelectButton.Click += SelectFirmwareClick;
        _firmwareProgress = new SolisProgressBar
        {
            Height = TrailingControlHeight,
            Maximum = 100,
            Minimum = 0,
            Width = TrailingControlWidth
        };
    }

    private Control CreateFirmwarePage()
    {
        FlowLayoutPanel body = CreatePageBody(
            "固件更新",
            "副屏只接电源时，也可以通过局域网完成维护。");

        body.Controls.Add(CreateStatusCard(
            "本地 OTA",
            "只接受 Solis Monitor 的 ESP32-S3 固件；更新失败时保留或回滚旧版本。",
            _firmwareStatus,
            "更新状态"));

        body.Controls.Add(CreateSection(
            "更新来源",
            CreateSettingRow(
                "本地固件",
                "选择 .bin 文件后先校验芯片、项目、版本和 SHA-256",
                _firmwareSelectButton),
            CreateSettingRow(
                "更新进度",
                "上传完成后等待副屏重启并确认实际运行版本",
                _firmwareProgress)));

        return body;
    }

    private async void SelectFirmwareClick(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            DefaultExt = "bin",
            Filter = "ESP32 固件 (*.bin)|*.bin|所有文件 (*.*)|*.*",
            Multiselect = false,
            RestoreDirectory = true,
            Title = "选择 Solis Monitor 固件"
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
            return;

        SetFirmwareProgress(0);
        SetFirmwareStatus($"正在校验\r\n{dialog.SafeFileName}");
        RefreshWpfFirmware();
        FirmwareImageValidationResult validation =
            FirmwareImageValidator.ValidateFile(dialog.FileName, long.MaxValue);
        if (!validation.Success)
        {
            SetFirmwareStatus($"校验失败\r\n{validation.ErrorMessage}");
            RefreshWpfFirmware();
            SolisDialog.Show(
                FindForm(),
                "固件校验失败",
                validation.ErrorMessage ?? "固件校验失败。",
                SolisDialogKind.Warning);
            return;
        }

        FirmwareImageInfo image = validation.Image!;
        SetFirmwareStatus(
            $"已校验，等待确认\r\n{image.Version} · {dialog.SafeFileName}");
        RefreshWpfFirmware();
        DialogResult confirmation = SolisDialog.Confirm(
            FindForm(),
            "确认固件更新",
            "即将通过局域网更新副屏固件。\r\n" +
            $"文件：{dialog.SafeFileName}\r\n" +
            $"版本：{image.Version}\r\n" +
            $"大小：{image.Size / 1024d / 1024d:F2} MB\r\n" +
            $"SHA-256：{image.Sha256.Substring(0, 16)}…\r\n" +
            "更新期间请勿关闭 Solis Monitor 或断开副屏电源。\r\n" +
            "确定开始更新吗？",
            "开始更新",
            "取消");
        if (confirmation != DialogResult.OK)
        {
            SetFirmwareStatus(
                $"已取消更新\r\n{image.Version} · {dialog.SafeFileName}");
            RefreshWpfFirmware();
            return;
        }

        SetFirmwareSelectEnabled(false);
        RefreshWpfFirmware();
        try
        {
            var progress = new Progress<FirmwareUpdateProgress>(update =>
            {
                SetFirmwareProgress(update.Percent);
                SetFirmwareStatus($"{update.Stage}\r\n{update.Detail}");
                RefreshWpfFirmware();
            });
            FirmwareUpdateResult result = await _firmwareUpdater(
                dialog.FileName,
                progress);
            SetFirmwareStatus(result.Message);
            RefreshWpfFirmware();
            SolisDialog.Show(
                FindForm(),
                result.Success ? "固件更新完成" : "固件更新未完成",
                result.Message,
                result.Success ? SolisDialogKind.Success : SolisDialogKind.Warning);
        }
        finally
        {
            SetFirmwareSelectEnabled(true);
            RefreshWpfFirmware();
        }
    }

    private void SetFirmwareStatus(string text)
    {
        _firmwareStatusText = text;
        if (_firmwareStatus is not null)
            _firmwareStatus.Text = text;
    }

    private void SetFirmwareProgress(int value)
    {
        _firmwareProgressValue = Math.Max(0, Math.Min(100, value));
        if (_firmwareProgress is not null)
            _firmwareProgress.Value = _firmwareProgressValue;
    }

    private void SetFirmwareSelectEnabled(bool enabled)
    {
        _firmwareSelectEnabled = enabled;
        if (_firmwareSelectButton is not null)
            _firmwareSelectButton.Enabled = enabled;
    }
}
