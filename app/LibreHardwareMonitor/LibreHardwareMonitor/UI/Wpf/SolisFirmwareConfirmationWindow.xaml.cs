#nullable enable

using System;
using LibreHardwareMonitor.Solis.Firmware;

namespace LibreHardwareMonitor.UI.WpfViews;

public partial class SolisFirmwareConfirmationWindow
{
    public SolisFirmwareConfirmationWindow(
        string fileName,
        FirmwareImageInfo image)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(image);

        InitializeComponent();
        FirmwareDetailsText.Text = BuildDetails(fileName, image);
    }

    private static string BuildDetails(
        string fileName,
        FirmwareImageInfo image)
    {
        double sizeMegabytes = image.Size / 1024d / 1024d;
        string sha256 = image.Sha256;
        string formattedSha256 = sha256.Length > 32
            ? $"{sha256[..32]}\n{sha256[32..]}"
            : sha256;
        return
            "即将通过局域网更新副屏固件。\n\n" +
            $"文件：{fileName}\n" +
            $"版本：{image.Version}\n" +
            $"大小：{sizeMegabytes:0.00} MB\n" +
            $"SHA-256：\n{formattedSha256}\n" +
            "更新期间请勿关闭 Solis Monitor 或断开副屏电源。";
    }

    private void ConfirmButton_Click(
        object sender,
        System.Windows.RoutedEventArgs eventArgs) =>
        DialogResult = true;
}
