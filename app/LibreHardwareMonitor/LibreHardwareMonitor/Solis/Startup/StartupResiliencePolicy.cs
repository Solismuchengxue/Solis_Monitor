#nullable enable

using System;

namespace LibreHardwareMonitor.Solis.Startup;

public enum TrayIconCreationResult
{
    Created,
    RetryPending,
    FallbackVisible
}

public static class StartupResiliencePolicy
{
    public const int TrayIconMaximumAttempts = 30;

    public static TimeSpan LogonDelay { get; } = TimeSpan.FromSeconds(15);

    public static TimeSpan TrayIconRetryInterval { get; } =
        TimeSpan.FromSeconds(2);

    public static bool EnsureLogonDelay(
        TimeSpan currentDelay,
        Action<TimeSpan> applyDelay)
    {
        ArgumentNullException.ThrowIfNull(applyDelay);

        if (currentDelay == LogonDelay)
            return false;

        applyDelay(LogonDelay);
        return true;
    }
}

public sealed class TrayIconStartupRetryController
{
    private readonly Action _createTrayIcon;
    private readonly Action _showFallback;
    private readonly int _maximumAttempts;
    private int _attemptCount;
    private TrayIconCreationResult? _terminalResult;

    public TrayIconStartupRetryController(
        Action createTrayIcon,
        Action showFallback,
        int maximumAttempts)
    {
        _createTrayIcon = createTrayIcon ??
            throw new ArgumentNullException(nameof(createTrayIcon));
        _showFallback = showFallback ??
            throw new ArgumentNullException(nameof(showFallback));
        if (maximumAttempts <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumAttempts));

        _maximumAttempts = maximumAttempts;
    }

    public TrayIconCreationResult TryCreate()
    {
        if (_terminalResult is TrayIconCreationResult terminalResult)
            return terminalResult;

        try
        {
            _createTrayIcon();
            _terminalResult = TrayIconCreationResult.Created;
        }
        catch (InvalidOperationException)
        {
            _attemptCount++;
            if (_attemptCount < _maximumAttempts)
                return TrayIconCreationResult.RetryPending;

            _terminalResult = TrayIconCreationResult.FallbackVisible;
            _showFallback();
        }

        return _terminalResult.Value;
    }
}
