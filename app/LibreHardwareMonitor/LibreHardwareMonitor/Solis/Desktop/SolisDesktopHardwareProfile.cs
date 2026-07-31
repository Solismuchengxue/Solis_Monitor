#nullable enable

using System;
using LibreHardwareMonitor.Hardware;

namespace LibreHardwareMonitor.Solis.Desktop;

public static class SolisDesktopHardwareProfile
{
    public static void Apply(Computer computer)
    {
        if (computer is null)
            throw new ArgumentNullException(nameof(computer));

        computer.IsCpuEnabled = true;
        computer.IsGpuEnabled = true;
        computer.IsMemoryEnabled = true;
        computer.IsStorageEnabled = true;

        computer.IsMotherboardEnabled = false;
        computer.IsNetworkEnabled = false;
        computer.IsControllerEnabled = false;
        computer.IsPowerMonitorEnabled = false;
        computer.IsPsuEnabled = false;
        computer.IsBatteryEnabled = false;
    }
}
