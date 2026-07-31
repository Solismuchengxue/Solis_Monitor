#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using LibreHardwareMonitor.Solis.DeviceControl;

namespace LibreHardwareMonitor.UI.WpfViews;

public partial class SolisDeviceSetupWizardWindow : Window
{
    private readonly Func<DeviceDiscoveryState> _deviceProvider;
    private readonly Func<IReadOnlyList<DiscoveredDevice>> _candidateProvider;
    private readonly Action _scanRequested;
    private readonly Func<
        DiscoveredDevice,
        string,
        CancellationToken,
        Task<DevicePairingResult>> _pairRequested;
    private readonly Action _beginProvisioningMaintenance;
    private readonly DispatcherTimer _refreshTimer;
    private IReadOnlyList<DiscoveredDevice> _displayedCandidates =
        Array.Empty<DiscoveredDevice>();

    public SolisDeviceSetupWizardWindow(
        Func<DeviceDiscoveryState> deviceProvider,
        Func<IReadOnlyList<DiscoveredDevice>> candidateProvider,
        Action scanRequested,
        Func<
            DiscoveredDevice,
            string,
            CancellationToken,
            Task<DevicePairingResult>> pairRequested,
        Action beginProvisioningMaintenance)
    {
        InitializeComponent();
        _deviceProvider = deviceProvider ??
            throw new ArgumentNullException(nameof(deviceProvider));
        _candidateProvider = candidateProvider ??
            throw new ArgumentNullException(nameof(candidateProvider));
        _scanRequested = scanRequested ??
            throw new ArgumentNullException(nameof(scanRequested));
        _pairRequested = pairRequested ??
            throw new ArgumentNullException(nameof(pairRequested));
        _beginProvisioningMaintenance = beginProvisioningMaintenance ??
            throw new ArgumentNullException(nameof(beginProvisioningMaintenance));

        _refreshTimer = new DispatcherTimer(
            TimeSpan.FromSeconds(1),
            DispatcherPriority.Background,
            (_, _) => RefreshDeviceStatus(),
            Dispatcher);
        _refreshTimer.Stop();
        Closed += (_, _) => _refreshTimer.Stop();
    }

    private void DiscoverButton_Click(object sender, RoutedEventArgs e) =>
        BeginDiscovery();

    private void ProvisionButton_Click(object sender, RoutedEventArgs e) =>
        BeginProvisioning();

    private void ScanButton_Click(object sender, RoutedEventArgs e)
    {
        _scanRequested();
        RefreshDeviceStatus();
    }

    private async void PairButton_Click(object sender, RoutedEventArgs e) =>
        await PairSelectedDeviceAsync();

    private void ContinueButton_Click(object sender, RoutedEventArgs e) =>
        BeginDiscovery();

    private void BackButton_Click(object sender, RoutedEventArgs e) =>
        ShowChoice();

    private void DeviceList_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e) =>
        PairButton.IsEnabled =
            DeviceList.SelectedItem is DiscoveredDevice { PairingActive: true };

    private void ShowChoice()
    {
        _refreshTimer.Stop();
        ChoicePage.Visibility = Visibility.Visible;
        DiscoveryPage.Visibility = Visibility.Collapsed;
        ProvisioningPage.Visibility = Visibility.Collapsed;
        BackButton.Visibility = Visibility.Collapsed;
        ScanButton.Visibility = Visibility.Collapsed;
        PairButton.Visibility = Visibility.Collapsed;
        ContinueButton.Visibility = Visibility.Collapsed;
        SubtitleText.Text = "配置 Wi-Fi，发现并安全配对副屏。";
    }

    private void BeginDiscovery()
    {
        ChoicePage.Visibility = Visibility.Collapsed;
        DiscoveryPage.Visibility = Visibility.Visible;
        ProvisioningPage.Visibility = Visibility.Collapsed;
        BackButton.Visibility = Visibility.Visible;
        ScanButton.Visibility = Visibility.Visible;
        PairButton.Visibility = Visibility.Visible;
        ContinueButton.Visibility = Visibility.Collapsed;
        SubtitleText.Text = "让副屏保持在开启发现页面，再选择设备并输入配对码。";
        _scanRequested();
        RefreshDeviceStatus();
        _refreshTimer.Start();
    }

    private void BeginProvisioning()
    {
        _refreshTimer.Stop();
        _beginProvisioningMaintenance();
        ChoicePage.Visibility = Visibility.Collapsed;
        DiscoveryPage.Visibility = Visibility.Collapsed;
        ProvisioningPage.Visibility = Visibility.Visible;
        BackButton.Visibility = Visibility.Visible;
        ScanButton.Visibility = Visibility.Collapsed;
        PairButton.Visibility = Visibility.Collapsed;
        ContinueButton.Visibility = Visibility.Visible;
        SubtitleText.Text = "使用副屏热点完成 Wi-Fi 配置。";
    }

    private void RefreshDeviceStatus()
    {
        DeviceDiscoveryState state = _deviceProvider();
        IReadOnlyList<DiscoveredDevice> candidates = _candidateProvider()
            .Where(device => device.PairingActive)
            .OrderBy(device => device.HostName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        string? selectedIp =
            (DeviceList.SelectedItem as DiscoveredDevice)?.IpAddress;
        _displayedCandidates = candidates;
        DeviceList.ItemsSource = _displayedCandidates;

        if (!string.IsNullOrEmpty(selectedIp))
        {
            DeviceList.SelectedItem = _displayedCandidates.FirstOrDefault(
                device => string.Equals(
                    device.IpAddress,
                    selectedIp,
                    StringComparison.OrdinalIgnoreCase));
        }
        else if (_displayedCandidates.Count == 1)
        {
            DeviceList.SelectedIndex = 0;
        }

        DiscoveryStatusText.Text = state.Device?.Paired == true
            ? $"已连接并配对：{state.Device.HostName}（{state.Device.IpAddress}）"
            : state.IsScanning
                ? "正在扫描局域网中的副屏……"
                : _displayedCandidates.Count == 0
                    ? "尚未发现开启发现模式的副屏。请在副屏上双击 GPIO21 后重试。"
                    : $"发现 {_displayedCandidates.Count} 台可配对副屏。";
        PairButton.IsEnabled =
            DeviceList.SelectedItem is DiscoveredDevice { PairingActive: true };
    }

    private async Task PairSelectedDeviceAsync()
    {
        if (DeviceList.SelectedItem is not DiscoveredDevice device ||
            !device.PairingActive)
        {
            DiscoveryStatusText.Text = "请选择仍处于发现模式的副屏。";
            return;
        }

        var dialog = new SolisPairingCodeWindow(device.HostName)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
            return;

        PairButton.IsEnabled = false;
        ScanButton.IsEnabled = false;
        DiscoveryStatusText.Text = "正在验证配对码并同步设备令牌……";
        try
        {
            DevicePairingResult result = await PairDeviceAsync(
                device,
                dialog.PairingCode);
            if (!result.Success)
            {
                DiscoveryStatusText.Text =
                    result.ErrorMessage ?? "配对失败，请确认副屏仍处于发现页面。";
                return;
            }

            CompleteSuccessfulPairing(device);
        }
        catch (Exception exception)
        {
            DiscoveryStatusText.Text = "配对失败：" + exception.Message;
        }
        finally
        {
            ScanButton.IsEnabled = true;
            RefreshDeviceStatus();
        }
    }

    private void CompleteSuccessfulPairing(DiscoveredDevice device)
    {
        DiscoveryStatusText.Text =
            $"已成功配对：{device.HostName}。副屏可单击按键退出发现页面。";
        RefreshDeviceStatus();
    }

    private Task<DevicePairingResult> PairDeviceAsync(
        DiscoveredDevice device,
        string code) =>
        _pairRequested(device, code, CancellationToken.None);
}
