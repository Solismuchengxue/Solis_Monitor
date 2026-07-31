#nullable enable

using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LibreHardwareMonitor.UI.WpfViews;

public sealed record SolisDeviceViewState(
    string DeviceState,
    string PairingStatus,
    bool IsPaired,
    bool SettingsAvailable,
    bool CanRestart,
    int BrightnessPercent,
    bool NightEnabled,
    string NightSummary,
    bool RestartPending);

public sealed record SolisStartupViewState(
    bool SilentStartup,
    bool AutoStart,
    string Summary);

public sealed record SolisFirmwareViewState(
    string Status,
    int Progress,
    bool IsBusy);

public partial class SolisControlCenterView : UserControl
{
    private static readonly Brush SelectedNavigationBrush =
        new LinearGradientBrush(
            Color.FromRgb(20, 65, 137),
            Color.FromRgb(32, 75, 153),
            0);
    private static readonly Brush TransparentBrush = Brushes.Transparent;
    private bool _synchronizing;
    private string _activePage = "Device";

    public SolisControlCenterView()
    {
        InitializeComponent();
        LoadBrandImage();
        ShowPage("Device");
    }

    public SolisServiceView ServiceView => ServicePage;

    public event EventHandler? DeviceWizardRequested;
    public event EventHandler? ClearPairingRequested;
    public event EventHandler<int>? BrightnessChanged;
    public event EventHandler? NightBacklightRequested;
    public event EventHandler? RestartDeviceRequested;
    public event EventHandler<bool>? SilentStartupChanged;
    public event EventHandler<bool>? AutoStartChanged;
    public event EventHandler? FirmwareSelectRequested;
    public event EventHandler? VersionClicked;
    public event EventHandler? DeveloperRequested;
    public event EventHandler? DisableDeveloperRequested;

    public void SetVersion(string version)
    {
        int metadataIndex = version.IndexOf('+');
        string displayVersion = metadataIndex >= 0
            ? version[..metadataIndex]
            : version;
        RunOnUiThread(() => VersionText.Text = $"版本 {displayVersion}");
    }

    public void SetDeveloperVisible(bool visible)
    {
        RunOnUiThread(() =>
        {
            DeveloperNavigationButton.Visibility =
                visible ? Visibility.Visible : Visibility.Collapsed;
            if (!visible && _activePage == "Developer")
                ShowPage("Device");
        });
    }

    public void ShowPage(string page)
    {
        RunOnUiThread(() =>
        {
            _activePage = page;
            DevicePage.Visibility = page == "Device"
                ? Visibility.Visible
                : Visibility.Collapsed;
            ServicePage.Visibility = page == "Service"
                ? Visibility.Visible
                : Visibility.Collapsed;
            StartupPage.Visibility = page == "Startup"
                ? Visibility.Visible
                : Visibility.Collapsed;
            FirmwarePage.Visibility = page == "Firmware"
                ? Visibility.Visible
                : Visibility.Collapsed;
            DeveloperPage.Visibility = page == "Developer"
                ? Visibility.Visible
                : Visibility.Collapsed;

            ApplyNavigationState(DeviceNavigationButton, page == "Device");
            ApplyNavigationState(ServiceNavigationButton, page == "Service");
            ApplyNavigationState(StartupNavigationButton, page == "Startup");
            ApplyNavigationState(FirmwareNavigationButton, page == "Firmware");
            ApplyNavigationState(DeveloperNavigationButton, page == "Developer");
        });
    }

    public void UpdateDevice(SolisDeviceViewState state)
    {
        RunOnUiThread(() =>
        {
            _synchronizing = true;
            try
            {
                DeviceStateText.Text = state.DeviceState.Replace("\r\n", "\n");
                PairingStatusText.Text = state.PairingStatus;
                ClearPairingRow.Visibility = state.IsPaired
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                BrightnessSlider.IsEnabled = state.SettingsAvailable;
                NightBacklightButton.IsEnabled = state.SettingsAvailable;
                RestartDeviceButton.IsEnabled = state.CanRestart;
                BrightnessSlider.Value = state.BrightnessPercent;
                BrightnessValueText.Text = state.SettingsAvailable
                    ? $"{state.BrightnessPercent}%"
                    : "--";
                NightBacklightSummaryText.Text = state.NightSummary;
                RestartDeviceButton.Content = state.RestartPending
                    ? "正在重启"
                    : "重新启动";
            }
            finally
            {
                _synchronizing = false;
            }
        });
    }

    public void UpdateStartup(SolisStartupViewState state)
    {
        RunOnUiThread(() =>
        {
            _synchronizing = true;
            try
            {
                SilentStartupToggle.IsChecked = state.SilentStartup;
                AutoStartToggle.IsChecked = state.AutoStart;
                StartupSummaryText.Text = state.Summary;
            }
            finally
            {
                _synchronizing = false;
            }
        });
    }

    public void UpdateFirmware(SolisFirmwareViewState state)
    {
        RunOnUiThread(() =>
        {
            FirmwareStatusText.Text = state.Status.Replace("\r\n", "\n");
            FirmwareProgressBar.Value = Math.Max(0, Math.Min(100, state.Progress));
            FirmwareSelectButton.IsEnabled = !state.IsBusy;
        });
    }

    private static void ApplyNavigationState(Button button, bool selected)
    {
        button.Background = selected ? SelectedNavigationBrush : TransparentBrush;
        button.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
    }

    private void LoadBrandImage()
    {
        Assembly assembly = Assembly.GetExecutingAssembly();
        string? resourceName = Array.Find(
            assembly.GetManifestResourceNames(),
            name => name.EndsWith(
                ".solis_monitor_icon.png",
                StringComparison.OrdinalIgnoreCase));
        if (resourceName is null)
            return;

        using Stream? stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
            return;

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        BrandImage.Source = image;
    }

    private void RunOnUiThread(Action action)
    {
        if (Dispatcher.CheckAccess())
            action();
        else
            Dispatcher.Invoke(action);
    }

    private void DeviceNavigationButton_Click(object sender, RoutedEventArgs e) =>
        ShowPage("Device");

    private void ServiceNavigationButton_Click(object sender, RoutedEventArgs e) =>
        ShowPage("Service");

    private void StartupNavigationButton_Click(object sender, RoutedEventArgs e) =>
        ShowPage("Startup");

    private void FirmwareNavigationButton_Click(object sender, RoutedEventArgs e) =>
        ShowPage("Firmware");

    private void DeveloperNavigationButton_Click(object sender, RoutedEventArgs e) =>
        ShowPage("Developer");

    private void DeviceWizardButton_Click(object sender, RoutedEventArgs e) =>
        DeviceWizardRequested?.Invoke(this, EventArgs.Empty);

    private void ClearPairingButton_Click(object sender, RoutedEventArgs e) =>
        ClearPairingRequested?.Invoke(this, EventArgs.Empty);

    private void BrightnessSlider_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (_synchronizing || BrightnessValueText is null)
            return;

        int value = (int)Math.Round(e.NewValue);
        BrightnessValueText.Text = $"{value}%";
        BrightnessChanged?.Invoke(this, value);
    }

    private void NightBacklightButton_Click(object sender, RoutedEventArgs e) =>
        NightBacklightRequested?.Invoke(this, EventArgs.Empty);

    private void RestartDeviceButton_Click(object sender, RoutedEventArgs e) =>
        RestartDeviceRequested?.Invoke(this, EventArgs.Empty);

    private void SilentStartupToggle_Click(object sender, RoutedEventArgs e)
    {
        if (!_synchronizing)
            SilentStartupChanged?.Invoke(this, SilentStartupToggle.IsChecked == true);
    }

    private void AutoStartToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_synchronizing)
            return;

        bool enabled = AutoStartToggle.IsChecked == true;
        if (!enabled)
        {
            _synchronizing = true;
            try
            {
                SilentStartupToggle.IsChecked = false;
            }
            finally
            {
                _synchronizing = false;
            }
        }

        Dispatcher.BeginInvoke(
            new Action(() => AutoStartChanged?.Invoke(this, enabled)));
    }

    private void FirmwareSelectButton_Click(object sender, RoutedEventArgs e) =>
        FirmwareSelectRequested?.Invoke(this, EventArgs.Empty);

    private void VersionText_MouseLeftButtonUp(
        object sender,
        System.Windows.Input.MouseButtonEventArgs e) =>
        VersionClicked?.Invoke(this, EventArgs.Empty);

    private void EnterDeveloperButton_Click(object sender, RoutedEventArgs e) =>
        DeveloperRequested?.Invoke(this, EventArgs.Empty);

    private void DisableDeveloperButton_Click(object sender, RoutedEventArgs e) =>
        DisableDeveloperRequested?.Invoke(this, EventArgs.Empty);
}
