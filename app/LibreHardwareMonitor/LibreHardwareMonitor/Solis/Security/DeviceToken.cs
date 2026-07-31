#nullable enable

using System;
using System.Security.Cryptography;
using System.Text;

namespace LibreHardwareMonitor.Solis.Security;

public static class DeviceToken
{
    public const int HexLength = 64;

    public static string Generate()
    {
        byte[] bytes = new byte[HexLength / 2];
        using (RandomNumberGenerator generator = RandomNumberGenerator.Create())
            generator.GetBytes(bytes);

        var result = new StringBuilder(HexLength);
        foreach (byte value in bytes)
            result.Append(value.ToString("x2"));

        return result.ToString();
    }

    public static bool IsValid(string? token) => TryDecode(token, out _);

    public static bool IsAuthorized(string? authorization, string expectedHex)
    {
        const string prefix = "Bearer ";
        if (authorization is null || !authorization.StartsWith(prefix, StringComparison.Ordinal))
            return false;

        if (!TryDecode(authorization.Substring(prefix.Length), out byte[] supplied) ||
            !TryDecode(expectedHex, out byte[] expected))
        {
            return false;
        }

        int difference = 0;
        for (int index = 0; index < supplied.Length; index++)
            difference |= supplied[index] ^ expected[index];

        return difference == 0;
    }

    private static bool TryDecode(string? value, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (value is null || value.Length != HexLength)
            return false;

        var decoded = new byte[HexLength / 2];
        for (int index = 0; index < decoded.Length; index++)
        {
            int high = HexValue(value[index * 2]);
            int low = HexValue(value[index * 2 + 1]);
            if (high < 0 || low < 0)
                return false;

            decoded[index] = (byte)((high << 4) | low);
        }

        bytes = decoded;
        return true;
    }

    private static int HexValue(char value)
    {
        if (value >= '0' && value <= '9')
            return value - '0';
        if (value >= 'a' && value <= 'f')
            return value - 'a' + 10;
        if (value >= 'A' && value <= 'F')
            return value - 'A' + 10;
        return -1;
    }
}
