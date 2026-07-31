#nullable enable

using System;
using System.Net;

namespace LibreHardwareMonitor.Solis.DeviceControl;

public sealed record DeviceTrayPresentation(
    string StatusText,
    string? WebUiUrl)
{
    public bool CanOpenWebUi => WebUiUrl is not null;

    public static DeviceTrayPresentation From(DeviceDiscoveryState state)
    {
        if (state.Device is DiscoveredDevice device &&
            IPAddress.TryParse(device.IpAddress, out IPAddress? address) &&
            address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            return new DeviceTrayPresentation(
                $"副屏：{device.HostName} · {device.IpAddress}",
                new UriBuilder(Uri.UriSchemeHttp, device.IpAddress).Uri.AbsoluteUri);
        }

        string status = state.IsScanning
            ? "副屏：正在发现…"
            : state.ErrorCategory == "MultipleDevices"
                ? "副屏：发现多个设备"
                : "副屏：未连接";
        return new DeviceTrayPresentation(status, null);
    }
}
