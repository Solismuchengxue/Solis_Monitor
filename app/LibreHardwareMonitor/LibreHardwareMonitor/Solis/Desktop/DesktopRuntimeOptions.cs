using System;

namespace LibreHardwareMonitor.Solis.Desktop;

public sealed class DesktopRuntimeOptions
{
    public DesktopRuntimeOptions(
        Action start,
        Action stop,
        Action save,
        Action dispose)
    {
        Start = start ?? throw new ArgumentNullException(nameof(start));
        Stop = stop ?? throw new ArgumentNullException(nameof(stop));
        Save = save ?? throw new ArgumentNullException(nameof(save));
        Dispose = dispose ?? throw new ArgumentNullException(nameof(dispose));
    }

    internal Action Start { get; }

    internal Action Stop { get; }

    internal Action Save { get; }

    internal Action Dispose { get; }
}
