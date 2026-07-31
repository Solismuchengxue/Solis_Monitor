#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using LibreHardwareMonitor.Solis.Security;

namespace LibreHardwareMonitor.Solis.DeviceControl;

public sealed class DeviceDiscoveryService : IDisposable
{
    private static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(30);
    private readonly object _lifecycleSync = new();
    private readonly object _sync = new();
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly DeviceTokenStore _tokenStore;
    private readonly SemaphoreSlim _probeLimit = new(24);
    private readonly CancellationTokenSource _shutdown = new();
    private Timer? _timer;
    private DeviceDiscoveryState _current = new(null, false, "NotScanned");
    private IReadOnlyList<DiscoveredDevice> _discoveryCandidates =
        Array.Empty<DiscoveredDevice>();
    private Task? _scanTask;
    private bool _disposed;
    private int _forceFullScan;
    private int _scanRunning;

    public DeviceDiscoveryService(
        DeviceTokenStore tokenStore,
        HttpClient? httpClient = null)
    {
        _tokenStore = tokenStore ?? throw new ArgumentNullException(nameof(tokenStore));
        _ownsHttpClient = httpClient is null;
        _httpClient = httpClient ??
            new HttpClient(new HttpClientHandler { UseProxy = false })
        {
            Timeout = TimeSpan.FromMilliseconds(800)
        };
    }

    public DeviceDiscoveryState Current
    {
        get
        {
            lock (_sync)
                return _current;
        }
    }

    public IReadOnlyList<DiscoveredDevice> DiscoveryCandidates
    {
        get
        {
            lock (_sync)
                return _discoveryCandidates;
        }
    }

    public void Start()
    {
        lock (_lifecycleSync)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(DeviceDiscoveryService));
            if (_timer is not null)
                return;

            _timer = new Timer(Scan, null, TimeSpan.Zero, ScanInterval);
        }
    }

    public void ScanNow()
    {
        lock (_lifecycleSync)
        {
            if (_disposed)
                return;

            Interlocked.Exchange(ref _forceFullScan, 1);
            _timer?.Change(TimeSpan.Zero, ScanInterval);
        }
    }

    public async Task<bool> RefreshPairedDeviceAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_tokenStore.TryGetPairedDevice(out _, out string? pairedIpAddress) ||
            !IPAddress.TryParse(pairedIpAddress, out IPAddress? pairedAddress)) {
            return false;
        }

        DiscoveredDevice? refreshed = await ProbeAsync(
            pairedAddress,
            cancellationToken).ConfigureAwait(false);
        if (refreshed is null || !IsLocallyPaired(refreshed))
            return false;

        SetCurrent(new DeviceDiscoveryState(refreshed, false, null));
        return true;
    }

    public async Task<DevicePairingResult> PairAsync(
        DiscoveredDevice device,
        string code,
        CancellationToken cancellationToken = default)
    {
        if (device is null)
            throw new ArgumentNullException(nameof(device));
        if (!DevicePairingProtocol.IsValidCode(code))
            return new DevicePairingResult(false, "请输入副屏显示的 6 位数字配对码。");
        string pairingToken = _tokenStore.DeviceToken;
        if (!DeviceToken.IsValid(pairingToken))
            return new DevicePairingResult(false, "PC 设备令牌不可用，请重启 Solis Monitor 后重试。");
        if (!IPAddress.TryParse(device.IpAddress, out IPAddress? address) ||
            !DeviceDiscoveryProtocol.IsPrivateIpv4(address)) {
            return new DevicePairingResult(false, "副屏 IPv4 地址无效。");
        }

        IPAddress? localAddress = GetLocalAddressFor(address);
        if (localAddress is null)
            return new DevicePairingResult(false, "无法确定连接副屏所用的本机 IPv4 地址。");

        try
        {
            using var content = new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("code", code),
                new KeyValuePair<string, string>("host", localAddress.ToString()),
                new KeyValuePair<string, string>("token", pairingToken)
            ]);
            using HttpResponseMessage response = await _httpClient.PostAsync(
                $"http://{address}/api/pair",
                content,
                cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return new DevicePairingResult(false, "配对失败，请核对配对码后重试。");

            DiscoveredDevice paired = device with
            {
                Paired = true,
                PairingActive = false
            };
            _tokenStore.MarkPaired(paired.HostName, paired.IpAddress);
            lock (_sync)
            {
                _current = new DeviceDiscoveryState(paired, false, null);
                _discoveryCandidates = _discoveryCandidates
                    .Select(candidate =>
                        string.Equals(
                            candidate.IpAddress,
                            device.IpAddress,
                            StringComparison.Ordinal)
                            ? paired
                            : candidate)
                    .ToArray();
            }
            return new DevicePairingResult(true);
        }
        catch (HttpRequestException)
        {
            return new DevicePairingResult(false, "无法连接副屏，请确认设备仍处于开启发现页面。");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new DevicePairingResult(false, "连接副屏超时，请重试。");
        }
    }

    public void ClearPairing()
    {
        _tokenStore.ClearPairing();
        SetDiscovery(
            new DeviceDiscoveryState(null, false, "NotPaired"),
            Array.Empty<DiscoveredDevice>());
    }

    public void ConfirmAuthorizedDevice(string remoteAddress)
    {
        if (!_tokenStore.LegacyPairingDiscoveryAllowed)
            return;

        DiscoveredDevice? device = Current.Device;
        if (device is null ||
            !device.Paired ||
            !string.Equals(
                device.IpAddress,
                remoteAddress,
                StringComparison.OrdinalIgnoreCase)) {
            return;
        }

        _tokenStore.MarkPaired(device.HostName, device.IpAddress);
    }

    private void Scan(object? state)
    {
        lock (_lifecycleSync)
        {
            if (_disposed ||
                Interlocked.Exchange(ref _scanRunning, 1) != 0)
            {
                return;
            }

            _scanTask = ScanAsync();
        }
    }

    private async Task ScanAsync()
    {
        bool forceFullScan = Interlocked.Exchange(ref _forceFullScan, 0) != 0;
        SetCurrent(Current with { IsScanning = true });
        try
        {
            DiscoveredDevice? known = Current.Device;
            if (!forceFullScan &&
                _tokenStore.TryGetPairedDevice(out _, out string? pairedIpAddress) &&
                IPAddress.TryParse(pairedIpAddress, out IPAddress? pairedAddress))
            {
                DiscoveredDevice? refreshed = await ProbeAsync(
                    pairedAddress,
                    _shutdown.Token).ConfigureAwait(false);
                if (refreshed is not null && IsLocallyPaired(refreshed))
                {
                    SetCurrent(new DeviceDiscoveryState(refreshed, false, null));
                }
                else
                {
                    SetDiscovery(
                        new DeviceDiscoveryState(null, false, "NotFound"),
                        Array.Empty<DiscoveredDevice>());
                }

                return;
            }

            if (known is not null && !forceFullScan)
            {
                DiscoveredDevice? refreshed = await ProbeAsync(
                    IPAddress.Parse(known.IpAddress),
                    _shutdown.Token).ConfigureAwait(false);
                if (refreshed is not null && IsLocallyPaired(refreshed))
                {
                    SetCurrent(new DeviceDiscoveryState(refreshed, false, null));
                    return;
                }
            }

            IReadOnlyList<IPAddress> candidates = GetLocalCandidates();
            var found = new ConcurrentBag<DiscoveredDevice>();
            Task[] probes = candidates
                .Select(address => ProbeAndCollectAsync(address, found, _shutdown.Token))
                .ToArray();
            await Task.WhenAll(probes).ConfigureAwait(false);

            DiscoveredDevice[] devices = found
                .GroupBy(device => device.HostName, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToArray();
            DiscoveredDevice[] pairingCandidates = devices
                .Where(device => device.PairingActive)
                .OrderBy(device => device.HostName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            DiscoveredDevice? currentDevice = known is null
                ? devices.FirstOrDefault(IsLocallyPaired)
                : devices.FirstOrDefault(device =>
                      IsLocallyPaired(device) &&
                      string.Equals(
                          device.IpAddress,
                          known.IpAddress,
                          StringComparison.Ordinal)) ??
                  devices.FirstOrDefault(IsLocallyPaired);
            DeviceDiscoveryState next = currentDevice is not null
                ? new DeviceDiscoveryState(currentDevice, false, null)
                : pairingCandidates.Length switch
            {
                0 => new DeviceDiscoveryState(null, false, "NotFound"),
                1 => new DeviceDiscoveryState(null, false, null),
                _ => new DeviceDiscoveryState(null, false, "MultipleDevices")
            };
            SetDiscovery(next, pairingCandidates);
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        catch
        {
            SetCurrent(new DeviceDiscoveryState(null, false, "ScanFailed"));
        }
        finally
        {
            Interlocked.Exchange(ref _scanRunning, 0);
            lock (_lifecycleSync)
            {
                if (!_disposed &&
                    Volatile.Read(ref _forceFullScan) != 0)
                {
                    _timer?.Change(TimeSpan.Zero, ScanInterval);
                }
            }
        }
    }

    private async Task ProbeAndCollectAsync(
        IPAddress address,
        ConcurrentBag<DiscoveredDevice> found,
        CancellationToken cancellationToken)
    {
        DiscoveredDevice? device = await ProbeAsync(address, cancellationToken)
            .ConfigureAwait(false);
        if (device is not null)
            found.Add(device);
    }

    private async Task<DiscoveredDevice?> ProbeAsync(
        IPAddress address,
        CancellationToken cancellationToken)
    {
        await _probeLimit.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using HttpResponseMessage response = await _httpClient.GetAsync(
                $"http://{address}/api/device",
                cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;
            string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!DeviceDiscoveryProtocol.TryParse(json, out DiscoveredDevice? device))
                return null;
            return device;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        finally
        {
            _probeLimit.Release();
        }
    }

    private static IReadOnlyList<IPAddress> GetLocalCandidates()
    {
        var addresses = new HashSet<IPAddress>();
        foreach (NetworkInterface networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up ||
                networkInterface.NetworkInterfaceType is
                    NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) {
                continue;
            }

            IPInterfaceProperties properties = networkInterface.GetIPProperties();
            bool hasIpv4Gateway = properties.GatewayAddresses.Any(
                gateway => gateway.Address.AddressFamily == AddressFamily.InterNetwork);
            if (!hasIpv4Gateway)
                continue;

            foreach (UnicastIPAddressInformation unicast in properties.UnicastAddresses)
            {
                if (!DeviceDiscoveryProtocol.IsPrivateIpv4(unicast.Address))
                    continue;
                foreach (IPAddress candidate in DeviceDiscoveryProtocol.BuildSubnetCandidates(
                             unicast.Address,
                             unicast.PrefixLength)) {
                    addresses.Add(candidate);
                }
            }
        }
        return addresses.ToArray();
    }

    private void SetCurrent(DeviceDiscoveryState state)
    {
        lock (_sync)
            _current = state;
    }

    private bool IsLocallyPaired(DiscoveredDevice device) =>
        device.Paired &&
        (_tokenStore.MatchesPairedDevice(device.HostName, device.IpAddress) ||
         _tokenStore.LegacyPairingDiscoveryAllowed);

    private void SetDiscovery(
        DeviceDiscoveryState state,
        IReadOnlyList<DiscoveredDevice> candidates)
    {
        lock (_sync)
        {
            _current = state;
            _discoveryCandidates = candidates;
        }
    }

    private static IPAddress? GetLocalAddressFor(IPAddress remoteAddress)
    {
        try
        {
            using var socket = new Socket(
                AddressFamily.InterNetwork,
                SocketType.Dgram,
                ProtocolType.Udp);
            socket.Connect(new IPEndPoint(remoteAddress, 80));
            return (socket.LocalEndPoint as IPEndPoint)?.Address;
        }
        catch (SocketException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        Timer? timer;
        Task? scanTask;
        lock (_lifecycleSync)
        {
            if (_disposed)
                return;

            _disposed = true;
            timer = _timer;
            _timer = null;
            scanTask = _scanTask;
        }

        _shutdown.Cancel();
        timer?.Dispose();
        try
        {
            scanTask?.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
        }

        if (_ownsHttpClient)
            _httpClient.Dispose();
        _probeLimit.Dispose();
        _shutdown.Dispose();
    }
}
