#nullable enable

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace LibreHardwareMonitor.Solis.DeviceControl;

public static class DeviceDiscoveryProtocol
{
    public static bool TryParse(string json, out DiscoveredDevice? device)
    {
        device = null;
        if (string.IsNullOrWhiteSpace(json))
            return false;

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (!TryString(root, "product", out string? product) ||
                !string.Equals(product, "Solis Monitor", StringComparison.Ordinal) ||
                !TryString(root, "hostname", out string? hostname) ||
                !TryString(root, "firmware", out string? firmware) ||
                !TryString(root, "ip", out string? ip) ||
                !IPAddress.TryParse(ip, out IPAddress? parsedIp) ||
                parsedIp.AddressFamily != AddressFamily.InterNetwork ||
                !root.TryGetProperty("paired", out JsonElement pairedElement) ||
                (pairedElement.ValueKind != JsonValueKind.True &&
                 pairedElement.ValueKind != JsonValueKind.False)) {
                return false;
            }

            int? rssi = null;
            if (root.TryGetProperty("rssi", out JsonElement rssiElement) &&
                rssiElement.ValueKind != JsonValueKind.Null) {
                if (rssiElement.ValueKind != JsonValueKind.Number ||
                    !rssiElement.TryGetInt32(out int parsedRssi) ||
                    parsedRssi < -127 || parsedRssi > 0) {
                    return false;
                }
                rssi = parsedRssi;
            }

            bool pairingActive = false;
            if (root.TryGetProperty("pairing", out JsonElement pairingElement))
            {
                if (pairingElement.ValueKind != JsonValueKind.True &&
                    pairingElement.ValueKind != JsonValueKind.False) {
                    return false;
                }
                pairingActive = pairingElement.GetBoolean();
            }

            device = new DiscoveredDevice(
                hostname!,
                firmware!,
                parsedIp.ToString(),
                rssi,
                pairedElement.GetBoolean(),
                pairingActive);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static IReadOnlyList<IPAddress> BuildSubnetCandidates(
        IPAddress localAddress,
        int prefixLength)
    {
        if (localAddress is null)
            throw new ArgumentNullException(nameof(localAddress));
        if (localAddress.AddressFamily != AddressFamily.InterNetwork ||
            prefixLength < 0 || prefixLength > 32) {
            return Array.Empty<IPAddress>();
        }

        int effectivePrefix = Math.Max(prefixLength, 24);
        if (effectivePrefix >= 31)
            return Array.Empty<IPAddress>();

        byte[] bytes = localAddress.GetAddressBytes();
        uint address = ((uint)bytes[0] << 24) |
                       ((uint)bytes[1] << 16) |
                       ((uint)bytes[2] << 8) |
                       bytes[3];
        uint mask = uint.MaxValue << (32 - effectivePrefix);
        uint network = address & mask;
        uint broadcast = network | ~mask;
        var candidates = new List<IPAddress>((int)(broadcast - network - 2));
        for (uint candidate = network + 1; candidate < broadcast; candidate++)
        {
            if (candidate == address)
                continue;
            candidates.Add(ToAddress(candidate));
        }
        return candidates;
    }

    public static bool IsPrivateIpv4(IPAddress address)
    {
        if (address is null || address.AddressFamily != AddressFamily.InterNetwork)
            return false;
        byte[] bytes = address.GetAddressBytes();
        return bytes[0] == 10 ||
               bytes[0] == 192 && bytes[1] == 168 ||
               bytes[0] == 172 && bytes[1] is >= 16 and <= 31;
    }

    private static bool TryString(JsonElement parent, string name, out string? value)
    {
        value = null;
        if (!parent.TryGetProperty(name, out JsonElement element) ||
            element.ValueKind != JsonValueKind.String) {
            return false;
        }
        value = element.GetString();
        return !string.IsNullOrWhiteSpace(value);
    }

    private static IPAddress ToAddress(uint value) => new(
    [
        (byte)(value >> 24),
        (byte)(value >> 16),
        (byte)(value >> 8),
        (byte)value
    ]);
}
