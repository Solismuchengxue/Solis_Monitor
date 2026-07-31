using System;

namespace LibreHardwareMonitor.UI.WpfViews;

public sealed class WpfSingleInstanceRequestDispatcher
{
    private readonly Action<Action> _dispatch;
    private readonly Action _openDiagnostics;
    private readonly Action _showWindow;

    public WpfSingleInstanceRequestDispatcher(
        Action<Action> dispatch,
        Action showWindow,
        Action openDiagnostics)
    {
        _dispatch = dispatch ??
            throw new ArgumentNullException(nameof(dispatch));
        _showWindow = showWindow ??
            throw new ArgumentNullException(nameof(showWindow));
        _openDiagnostics = openDiagnostics ??
            throw new ArgumentNullException(nameof(openDiagnostics));
    }

    public void RequestShowWindow() => _dispatch(_showWindow);

    public void RequestDiagnostics() => _dispatch(_openDiagnostics);
}
