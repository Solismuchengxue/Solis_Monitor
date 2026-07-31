#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading;

namespace LibreHardwareMonitor.Solis.Network;

public sealed class WindowsNetworkCounterSource : INetworkCounterSource
{
    private static readonly uint ProbeAddress = BitConverter.ToUInt32(
        IPAddress.Parse("1.1.1.1").GetAddressBytes(), 0);

    private string? _preferredInterfaceId;

    public string? PreferredInterfaceId
    {
        get => Volatile.Read(ref _preferredInterfaceId);
        set => Volatile.Write(ref _preferredInterfaceId, string.IsNullOrWhiteSpace(value) ? null : value);
    }

    public IReadOnlyList<NetworkAdapterOption> GetSelectableInterfaces()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return Array.Empty<NetworkAdapterOption>();

        return EligibleInterfaces()
            .Select(item => item.Option)
            .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public NetworkCounterReadResult ReadSelected()
    {
        try
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return new(null, "PlatformNotSupported");

            List<(NetworkInterface Interface, NetworkAdapterOption Option)> candidates = EligibleInterfaces();
            int? bestInterfaceIndex = GetBestInterface(ProbeAddress, out uint index) == 0
                ? checked((int)index)
                : null;
            NetworkAdapterSelection selection = NetworkAdapterSelector.Select(
                candidates.Select(item => item.Option).ToArray(),
                PreferredInterfaceId,
                bestInterfaceIndex);
            if (selection.Adapter is null)
                return new(null, selection.ErrorCategory);

            NetworkInterface selected = candidates.First(item =>
                string.Equals(item.Option.Id, selection.Adapter.Id, StringComparison.OrdinalIgnoreCase)).Interface;
            IPv4InterfaceStatistics statistics = selected.GetIPv4Statistics();
            return new(new NetworkCounterSnapshot(
                selected.Id,
                selected.Name,
                statistics.BytesReceived,
                statistics.BytesSent,
                selected.Speed), null);
        }
        catch (Exception exception) when (IsExpectedReadFailure(exception))
        {
            return new(null, exception.GetType().Name);
        }
    }

    private static List<(NetworkInterface Interface, NetworkAdapterOption Option)> EligibleInterfaces()
    {
        var result = new List<(NetworkInterface, NetworkAdapterOption)>();
        foreach (NetworkInterface networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            try
            {
                if (networkInterface.OperationalStatus != OperationalStatus.Up ||
                    networkInterface.NetworkInterfaceType is not (NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211))
                    continue;

                IPInterfaceProperties properties = networkInterface.GetIPProperties();
                IPv4InterfaceProperties? ipv4 = properties.GetIPv4Properties();
                if (ipv4 is null)
                    continue;

                bool hasDefaultGateway = properties.GatewayAddresses.Any(gateway =>
                    gateway.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
                    !gateway.Address.Equals(IPAddress.Any));
                result.Add((networkInterface, new NetworkAdapterOption(
                    networkInterface.Id,
                    networkInterface.Name,
                    networkInterface.Description,
                    ipv4.Index,
                    hasDefaultGateway)));
            }
            catch (NetworkInformationException)
            {
                // Some virtual or stale Windows adapters fail while reading IPv4 properties.
                // Skip only that adapter so it cannot hide the real default-route interface.
            }
        }

        return result;
    }

    private static bool IsExpectedReadFailure(Exception exception) => exception is
        DllNotFoundException or
        EntryPointNotFoundException or
        BadImageFormatException or
        OverflowException or
        PlatformNotSupportedException or
        NetworkInformationException;

    [DllImport("iphlpapi.dll")]
    private static extern uint GetBestInterface(uint destinationAddress, out uint bestInterfaceIndex);
}
