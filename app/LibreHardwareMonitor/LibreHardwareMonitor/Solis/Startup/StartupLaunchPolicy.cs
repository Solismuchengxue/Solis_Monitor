using System;
using System.Collections.Generic;

namespace LibreHardwareMonitor.Solis.Startup;

public static class StartupLaunchPolicy
{
    public const string OpenDiagnosticsArgument = "--open-diagnostics";
    public const string WindowsStartupArgument = "--windows-startup";

    public static bool IsWindowsStartup(IEnumerable<string> arguments)
    {
        if (arguments == null)
            return false;

        foreach (string argument in arguments)
        {
            if (string.Equals(argument, WindowsStartupArgument, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public static bool ShouldOpenDiagnostics(IEnumerable<string> arguments)
    {
        if (arguments == null)
            return false;

        foreach (string argument in arguments)
        {
            if (string.Equals(argument, OpenDiagnosticsArgument, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public static bool ShouldStartHidden(
        IEnumerable<string> arguments,
        bool silentStartupEnabled) =>
        silentStartupEnabled && IsWindowsStartup(arguments);
}
