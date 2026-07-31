#nullable enable

using System.Linq;
using System.Windows;
using System.Windows.Input;
using LibreHardwareMonitor.Solis.DeviceControl;

namespace LibreHardwareMonitor.UI.WpfViews;

public partial class SolisPairingCodeWindow : Window
{
    public SolisPairingCodeWindow(string deviceName)
    {
        InitializeComponent();
        DeviceNameText.Text = $"正在连接 {deviceName}";
        Loaded += (_, _) =>
        {
            PairingCodeInput.Focus();
            Keyboard.Focus(PairingCodeInput);
        };
    }

    public string PairingCode => PairingCodeInput.Text;

    private void PairingCodeInput_PreviewTextInput(
        object sender,
        TextCompositionEventArgs e) =>
        e.Handled = e.Text.Any(character => character is < '0' or > '9');

    private void PairingCodeInput_TextChanged(
        object sender,
        System.Windows.Controls.TextChangedEventArgs e)
    {
        ConfirmButton.IsEnabled =
            DevicePairingProtocol.IsValidCode(PairingCodeInput.Text);
        ValidationText.Visibility = Visibility.Collapsed;
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        if (!DevicePairingProtocol.IsValidCode(PairingCodeInput.Text))
        {
            ValidationText.Visibility = Visibility.Visible;
            return;
        }

        DialogResult = true;
    }
}
