#nullable enable

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LibreHardwareMonitor.Solis.Diagnostics;

namespace LibreHardwareMonitor.UI.WpfViews;

public sealed record SolisServiceViewState(
    string OverallStatus,
    DiagnosticCheckState OverallState,
    string LastCommunication,
    string ProcessDetail,
    string ApiStatus,
    DiagnosticCheckState ApiState,
    string ApiDetail,
    string DeviceStatus,
    DiagnosticCheckState DeviceState,
    string DeviceDetail,
    string CodexStatus,
    DiagnosticCheckState CodexState,
    string CodexDetail,
    string WeatherStatus,
    DiagnosticCheckState WeatherState,
    string WeatherDetail);

public partial class SolisServiceView : UserControl
{
    private static readonly Brush HealthyBrush =
        new SolidColorBrush(Color.FromRgb(62, 207, 142));
    private static readonly Brush FaultBrush =
        new SolidColorBrush(Color.FromRgb(255, 103, 117));
    private static readonly Brush CheckingBrush =
        new SolidColorBrush(Color.FromRgb(22, 154, 247));

    public SolisServiceView()
    {
        InitializeComponent();
    }

    public event EventHandler? RestartRequested;

    public event EventHandler? CopyDiagnosticsRequested;

    public event EventHandler? EditWeatherRequested;

    public event EventHandler? OpenCodexRequested;

    public void UpdateState(SolisServiceViewState state)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => UpdateState(state));
            return;
        }

        SetStatus(HeaderOverallStatus, state.OverallStatus, state.OverallState);
        HeaderLastCommunication.Text = state.LastCommunication;
        ProcessDetail.Text = state.ProcessDetail;
        SetStatus(ApiStatus, state.ApiStatus, state.ApiState);
        ApiDetail.Text = state.ApiDetail;
        SetStatus(DeviceStatus, state.DeviceStatus, state.DeviceState);
        DeviceDetail.Text = state.DeviceDetail;
        SetStatus(CodexStatus, state.CodexStatus, state.CodexState);
        CodexDetail.Text = state.CodexDetail;
        SetStatus(WeatherStatus, state.WeatherStatus, state.WeatherState);
        WeatherDetail.Text = state.WeatherDetail;
    }

    public void SetRestartBusy(bool busy)
    {
        RestartButtonText.Text = busy ? "正在重启" : "重启服务";
    }

    private static void SetStatus(
        TextBlock target,
        string text,
        DiagnosticCheckState state)
    {
        target.Text = text;
        target.Foreground = state switch
        {
            DiagnosticCheckState.Normal => HealthyBrush,
            DiagnosticCheckState.Fault => FaultBrush,
            _ => CheckingBrush
        };
    }

    private void RestartButton_Click(object sender, RoutedEventArgs e) =>
        RestartRequested?.Invoke(this, EventArgs.Empty);

    private void CopyDiagnosticsButton_Click(object sender, RoutedEventArgs e) =>
        CopyDiagnosticsRequested?.Invoke(this, EventArgs.Empty);

    private void EditWeatherButton_Click(object sender, MouseButtonEventArgs e) =>
        EditWeatherRequested?.Invoke(this, EventArgs.Empty);

    private void OpenCodexButton_Click(object sender, MouseButtonEventArgs e) =>
        OpenCodexRequested?.Invoke(this, EventArgs.Empty);
}
