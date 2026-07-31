using System;

namespace LibreHardwareMonitor.Solis.Desktop;

public sealed class DesktopStartupSettingsController
{
    private readonly Func<bool> _getAutoStart;
    private readonly Func<bool> _getSilentStartup;
    private readonly Action<bool> _setAutoStart;
    private readonly Action<bool> _setSilentStartup;

    public DesktopStartupSettingsController(
        Func<bool> getSilentStartup,
        Action<bool> setSilentStartup,
        Func<bool> getAutoStart,
        Action<bool> setAutoStart)
    {
        _getSilentStartup = getSilentStartup ??
            throw new ArgumentNullException(nameof(getSilentStartup));
        _setSilentStartup = setSilentStartup ??
            throw new ArgumentNullException(nameof(setSilentStartup));
        _getAutoStart = getAutoStart ??
            throw new ArgumentNullException(nameof(getAutoStart));
        _setAutoStart = setAutoStart ??
            throw new ArgumentNullException(nameof(setAutoStart));
    }

    public bool AutoStart => _getAutoStart();

    public bool SilentStartup => _getSilentStartup();

    public string GetSummary()
    {
        if (!AutoStart)
            return "未启用开机启动";

        return SilentStartup
            ? "开机启动 · 静默进入托盘"
            : "开机启动 · 显示控制台";
    }

    public void SetAutoStart(bool enabled)
    {
        if (!enabled)
            _setSilentStartup(false);

        _setAutoStart(enabled);
    }

    public void SetSilentStartup(bool enabled)
    {
        _setSilentStartup(enabled);
    }
}
