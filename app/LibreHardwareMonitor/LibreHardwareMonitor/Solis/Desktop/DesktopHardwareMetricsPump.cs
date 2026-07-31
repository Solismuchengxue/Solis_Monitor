using System;
using System.Threading;
using LibreHardwareMonitor.Solis.Hardware;

namespace LibreHardwareMonitor.Solis.Desktop;

public sealed class DesktopHardwareMetricsPump
{
    private readonly Action _refreshHardware;
    private readonly Func<MappedHardwareMetrics> _readMetrics;
    private readonly Action<MappedHardwareMetrics> _publishMetrics;
    private int _collecting;

    public DesktopHardwareMetricsPump(
        Action refreshHardware,
        Func<MappedHardwareMetrics> readMetrics,
        Action<MappedHardwareMetrics> publishMetrics)
    {
        _refreshHardware = refreshHardware ??
            throw new ArgumentNullException(nameof(refreshHardware));
        _readMetrics = readMetrics ??
            throw new ArgumentNullException(nameof(readMetrics));
        _publishMetrics = publishMetrics ??
            throw new ArgumentNullException(nameof(publishMetrics));
    }

    public bool CollectOnce()
    {
        if (Interlocked.Exchange(ref _collecting, 1) != 0)
            return false;

        try
        {
            _refreshHardware();
            _publishMetrics(_readMetrics());
            return true;
        }
        finally
        {
            Volatile.Write(ref _collecting, 0);
        }
    }
}
