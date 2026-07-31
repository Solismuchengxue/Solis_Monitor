#nullable enable

using System;
using System.Windows;
using LibreHardwareMonitor.Solis.DeviceControl;

namespace LibreHardwareMonitor.UI.WpfViews;

public partial class SolisNightBacklightSettingsWindow : Window
{
    private readonly DeviceDisplaySettings _original;

    public SolisNightBacklightSettingsWindow(DeviceDisplaySettings settings)
    {
        _original = settings ?? throw new ArgumentNullException(nameof(settings));
        Settings = settings;
        InitializeComponent();

        NightEnabledInput.IsChecked = settings.NightEnabled;
        PopulateTimeOptions();
        SelectTime(
            StartHourInput,
            StartMinuteInput,
            settings.NightStartMinute);
        SelectTime(
            EndHourInput,
            EndMinuteInput,
            settings.NightEndMinute);
    }

    public DeviceDisplaySettings Settings { get; private set; }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (ApplySelection())
            DialogResult = true;
    }

    private bool ApplySelection()
    {
        if (!TryReadTime(StartHourInput, StartMinuteInput, out int startMinute) ||
            !TryReadTime(EndHourInput, EndMinuteInput, out int endMinute))
        {
            ValidationText.Text = "请选择开始时间和结束时间。";
            return false;
        }

        if (startMinute == endMinute)
        {
            ValidationText.Text = "开始时间和结束时间不能相同。";
            return false;
        }

        Settings = _original with
        {
            NightEnabled = NightEnabledInput.IsChecked == true,
            NightStartMinute = startMinute,
            NightEndMinute = endMinute,
            UtcOffsetMinutes = (int)Math.Round(
                TimeZoneInfo.Local.GetUtcOffset(
                    DateTimeOffset.Now).TotalMinutes)
        };
        ValidationText.Text = string.Empty;
        return true;
    }

    private void PopulateTimeOptions()
    {
        for (int hour = 0; hour < 24; hour++)
        {
            string value = hour.ToString("00");
            StartHourInput.Items.Add(value);
            EndHourInput.Items.Add(value);
        }

        for (int minute = 0; minute < 60; minute++)
        {
            string value = minute.ToString("00");
            StartMinuteInput.Items.Add(value);
            EndMinuteInput.Items.Add(value);
        }
    }

    private static void SelectTime(
        System.Windows.Controls.ComboBox hourInput,
        System.Windows.Controls.ComboBox minuteInput,
        int totalMinutes)
    {
        int normalized = Math.Clamp(totalMinutes, 0, (24 * 60) - 1);
        hourInput.SelectedItem = (normalized / 60).ToString("00");
        minuteInput.SelectedItem = (normalized % 60).ToString("00");
    }

    private static bool TryReadTime(
        System.Windows.Controls.ComboBox hourInput,
        System.Windows.Controls.ComboBox minuteInput,
        out int totalMinutes)
    {
        totalMinutes = 0;
        if (!int.TryParse(hourInput.SelectedItem?.ToString(), out int hour) ||
            !int.TryParse(minuteInput.SelectedItem?.ToString(), out int minute))
        {
            return false;
        }

        totalMinutes = (hour * 60) + minute;
        return true;
    }
}
