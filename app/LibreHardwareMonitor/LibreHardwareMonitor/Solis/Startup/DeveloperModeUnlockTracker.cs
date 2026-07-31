#nullable enable

using System;

namespace LibreHardwareMonitor.Solis.Startup;

public sealed class DeveloperModeUnlockTracker
{
    private const int RequiredClicks = 10;
    private static readonly TimeSpan UnlockWindow = TimeSpan.FromSeconds(10);

    private int _clickCount;
    private DateTimeOffset _firstClickAt;

    public bool RegisterClick(DateTimeOffset clickedAt)
    {
        if (_clickCount == 0 ||
            clickedAt < _firstClickAt ||
            clickedAt - _firstClickAt > UnlockWindow)
        {
            _clickCount = 1;
            _firstClickAt = clickedAt;
            return false;
        }

        _clickCount++;
        if (_clickCount < RequiredClicks)
            return false;

        Reset();
        return true;
    }

    public void Reset()
    {
        _clickCount = 0;
        _firstClickAt = default;
    }
}
