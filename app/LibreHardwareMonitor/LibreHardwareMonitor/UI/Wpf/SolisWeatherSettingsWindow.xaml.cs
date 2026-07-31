#nullable enable

using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using LibreHardwareMonitor.Solis.Weather;

namespace LibreHardwareMonitor.UI.WpfViews;

public partial class SolisWeatherSettingsWindow : Window
{
    private static readonly Brush MutedBrush =
        new SolidColorBrush(Color.FromRgb(164, 175, 189));
    private static readonly Brush HealthyBrush =
        new SolidColorBrush(Color.FromRgb(62, 207, 142));
    private static readonly Brush DangerBrush =
        new SolidColorBrush(Color.FromRgb(255, 103, 117));

    private readonly SolisWeatherSettingsEditor _editor;
    private bool _initializing;
    private QWeatherSettings? _testedSettings;
    private WeatherMetricsReading? _testedReading;

    public SolisWeatherSettingsWindow(
        QWeatherSettings existingSettings,
        Func<QWeatherSettings, WeatherMetricsReading> testSettings,
        Action<QWeatherSettings, WeatherMetricsReading> saveSettings)
    {
        InitializeComponent();
        _editor = new SolisWeatherSettingsEditor(
            existingSettings,
            testSettings,
            saveSettings);

        _initializing = true;
        ApiHostInput.Text = existingSettings.ApiHost;
        CoordinatesInput.Text = _editor.FormatCoordinates();
        _initializing = false;
    }

    private void InputChanged(object sender, RoutedEventArgs e)
    {
        if (_initializing)
            return;

        _testedSettings = null;
        _testedReading = null;
        SaveButton.IsEnabled = false;
        SetStatus("配置已修改，请重新测试连接。", MutedBrush);
    }

    private async void TestButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_editor.TryCreateSettings(
                ApiHostInput.Text,
                ApiKeyInput.Password,
                CoordinatesInput.Text,
                out QWeatherSettings? settings,
                out string validationError) ||
            settings is null)
        {
            SetStatus(validationError, DangerBrush);
            return;
        }

        SetInputsEnabled(false);
        SetStatus("正在测试天气 API……", MutedBrush);

        try
        {
            WeatherMetricsReading reading = await Task.Run(() => _editor.Test(settings));
            if (!reading.Available)
            {
                SetStatus(
                    "测试失败：" +
                    SolisWeatherSettingsEditor.DescribeError(reading.ErrorCategory),
                    DangerBrush);
                return;
            }

            _testedSettings = settings;
            _testedReading = reading;
            SaveButton.IsEnabled = true;
            SetStatus(
                $"测试成功：{reading.Location} · {reading.Description} · " +
                $"{reading.OutdoorLowC:0.#}–{reading.OutdoorHighC:0.#}°C；现在可以保存。",
                HealthyBrush);
        }
        catch (Exception ex)
        {
            SetStatus("测试失败：" + ex.Message, DangerBrush);
        }
        finally
        {
            SetInputsEnabled(true);
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_testedSettings is null || _testedReading is null)
        {
            SetStatus("请先测试当前配置。", DangerBrush);
            return;
        }

        try
        {
            _editor.Save(_testedSettings, _testedReading);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            SetStatus("保存失败：" + ex.Message, DangerBrush);
        }
    }

    private void OpenCoordinatePicker_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://lbs.amap.com/tools/picker")
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            SetStatus("无法打开坐标查询页面：" + ex.Message, DangerBrush);
        }
    }

    private void SetInputsEnabled(bool enabled)
    {
        ApiHostInput.IsEnabled = enabled;
        ApiKeyInput.IsEnabled = enabled;
        CoordinatesInput.IsEnabled = enabled;
        TestButton.IsEnabled = enabled;
    }

    private void SetStatus(string message, Brush brush)
    {
        StatusText.Text = message;
        StatusText.Foreground = brush;
    }
}
