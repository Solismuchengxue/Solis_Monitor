using System;
using System.Collections.Generic;

namespace LibreHardwareMonitor.Solis.Startup;

public static class DesktopHostLauncher
{
    public static int Run(
        IEnumerable<string> arguments,
        Func<int> runWpf,
        Func<int> runLegacyWinForms)
    {
        if (runWpf is null)
            throw new ArgumentNullException(nameof(runWpf));
        if (runLegacyWinForms is null)
            throw new ArgumentNullException(nameof(runLegacyWinForms));

        return DesktopHostSelector.Select(arguments) ==
               DesktopHostMode.LegacyWinForms
            ? runLegacyWinForms()
            : runWpf();
    }
}
