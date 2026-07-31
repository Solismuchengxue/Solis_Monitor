#nullable enable

using System;

namespace LibreHardwareMonitor.Solis.DeviceControl;

public static class DevicePairingProtocol
{
    public const int CodeLength = 6;

    public static bool IsValidCode(string? code)
    {
        if (code is null || code.Length != CodeLength)
            return false;

        foreach (char character in code)
        {
            if (character is < '0' or > '9')
                return false;
        }

        return true;
    }
}
