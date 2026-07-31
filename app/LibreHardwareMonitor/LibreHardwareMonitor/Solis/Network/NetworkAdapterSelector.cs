#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace LibreHardwareMonitor.Solis.Network;

public sealed record NetworkAdapterOption(
    string Id,
    string Name,
    string Description,
    int Ipv4Index,
    bool HasDefaultGateway);

public readonly record struct NetworkAdapterSelection(
    NetworkAdapterOption? Adapter,
    string? ErrorCategory);

public static class NetworkAdapterSelector
{
    public static NetworkAdapterSelection Select(
        IReadOnlyList<NetworkAdapterOption> adapters,
        string? preferredInterfaceId,
        int? bestInterfaceIndex)
    {
        if (!string.IsNullOrWhiteSpace(preferredInterfaceId))
        {
            NetworkAdapterOption? preferred = adapters.FirstOrDefault(adapter =>
                string.Equals(adapter.Id, preferredInterfaceId, StringComparison.OrdinalIgnoreCase));
            return preferred is null
                ? new NetworkAdapterSelection(null, "PreferredInterfaceUnavailable")
                : new NetworkAdapterSelection(preferred, null);
        }

        if (bestInterfaceIndex is int index)
        {
            NetworkAdapterOption? best = adapters.FirstOrDefault(adapter => adapter.Ipv4Index == index);
            if (best is not null)
                return new NetworkAdapterSelection(best, null);
        }

        NetworkAdapterOption? gateway = adapters
            .Where(adapter => adapter.HasDefaultGateway)
            .OrderBy(adapter => adapter.Name, StringComparer.CurrentCultureIgnoreCase)
            .FirstOrDefault();
        if (gateway is not null)
            return new NetworkAdapterSelection(gateway, null);

        return new NetworkAdapterSelection(null, "NoEligibleInterface");
    }
}
