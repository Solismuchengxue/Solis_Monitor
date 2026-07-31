using System;
using System.Collections.Generic;

namespace LibreHardwareMonitor.Solis.Startup;

public enum DesktopHostMode
{
    Wpf,
    LegacyWinForms
}

public static class DesktopHostSelector
{
    public const string LegacyUiArgument = "--legacy-ui";

    public static DesktopHostMode Select(IEnumerable<string> arguments)
    {
        if (arguments is null)
            return DesktopHostMode.Wpf;

        foreach (string argument in arguments)
        {
            if (string.Equals(
                    argument,
                    LegacyUiArgument,
                    StringComparison.OrdinalIgnoreCase))
            {
                return DesktopHostMode.LegacyWinForms;
            }
        }

        return DesktopHostMode.Wpf;
    }
}
