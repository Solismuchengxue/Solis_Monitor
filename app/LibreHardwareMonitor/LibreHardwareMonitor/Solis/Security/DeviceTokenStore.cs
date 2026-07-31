#nullable enable

using System;
using System.IO;
using System.Text.Json;

namespace LibreHardwareMonitor.Solis.Security;

public sealed class DeviceTokenStore
{
    private const int SettingsSchema = 1;
    private readonly object _sync = new();
    private string _deviceToken = string.Empty;
    private string? _pairedHostName;
    private string? _pairedIpAddress;
    private bool _pairingRecordInitialized;

    public DeviceTokenStore(string? settingsDirectory = null)
    {
        SettingsDirectory = Path.GetFullPath(settingsDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SolisMonitor"));
        SettingsPath = Path.Combine(SettingsDirectory, "settings.json");
        LoadOrCreate();
    }

    public string DeviceToken
    {
        get
        {
            lock (_sync)
                return _deviceToken;
        }
    }

    public bool LegacyPairingDiscoveryAllowed
    {
        get
        {
            lock (_sync)
                return !_pairingRecordInitialized;
        }
    }

    public string SettingsDirectory { get; }

    public string SettingsPath { get; }

    public void MarkPaired(string hostName, string ipAddress)
    {
        if (string.IsNullOrWhiteSpace(hostName))
            throw new ArgumentException("Device host name is required.", nameof(hostName));
        if (string.IsNullOrWhiteSpace(ipAddress))
            throw new ArgumentException("Device IP address is required.", nameof(ipAddress));

        lock (_sync)
        {
            _pairedHostName = hostName;
            _pairedIpAddress = ipAddress;
            _pairingRecordInitialized = true;
            SaveCurrent();
        }
    }

    public bool MatchesPairedDevice(string hostName, string ipAddress)
    {
        lock (_sync)
        {
            return _pairingRecordInitialized &&
                   string.Equals(
                       _pairedHostName,
                       hostName,
                       StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(
                       _pairedIpAddress,
                       ipAddress,
                       StringComparison.OrdinalIgnoreCase);
        }
    }

    public bool TryGetPairedDevice(out string? hostName, out string? ipAddress)
    {
        lock (_sync)
        {
            hostName = _pairedHostName;
            ipAddress = _pairedIpAddress;
            return _pairingRecordInitialized &&
                   !string.IsNullOrWhiteSpace(hostName) &&
                   !string.IsNullOrWhiteSpace(ipAddress);
        }
    }

    public void ClearPairing()
    {
        lock (_sync)
        {
            string replacement;
            do
            {
                replacement =
                    global::LibreHardwareMonitor.Solis.Security.DeviceToken.Generate();
            }
            while (string.Equals(replacement, _deviceToken, StringComparison.Ordinal));

            _deviceToken = replacement;
            _pairedHostName = null;
            _pairedIpAddress = null;
            _pairingRecordInitialized = true;
            SaveCurrent();
        }
    }

    private void LoadOrCreate()
    {
        Directory.CreateDirectory(SettingsDirectory);
        if (File.Exists(SettingsPath))
        {
            try
            {
                DeviceApiSettings? settings = JsonSerializer.Deserialize<DeviceApiSettings>(
                    File.ReadAllText(SettingsPath));
                if (settings?.Schema == SettingsSchema &&
                    global::LibreHardwareMonitor.Solis.Security.DeviceToken.IsValid(
                        settings.DeviceToken))
                {
                    _deviceToken = settings.DeviceToken;
                    if (!string.IsNullOrWhiteSpace(settings.PairedHostName) &&
                        !string.IsNullOrWhiteSpace(settings.PairedIpAddress))
                    {
                        _pairedHostName = settings.PairedHostName;
                        _pairedIpAddress = settings.PairedIpAddress;
                    }

                    _pairingRecordInitialized =
                        settings.PairingRecordInitialized ||
                        _pairedHostName is not null;
                    return;
                }
            }
            catch (JsonException)
            {
            }
            catch (IOException)
            {
            }

            string corruptPath = Path.Combine(
                SettingsDirectory,
                $"settings.corrupt-{DateTime.UtcNow:yyyyMMddHHmmssfff}.json");
            File.Move(SettingsPath, corruptPath);
        }

        _deviceToken = global::LibreHardwareMonitor.Solis.Security.DeviceToken.Generate();
        _pairingRecordInitialized = true;
        SaveCurrent();
    }

    private void SaveCurrent()
    {
        string temporaryPath = $"{SettingsPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            var settings = new DeviceApiSettings
            {
                DeviceToken = _deviceToken,
                PairedHostName = _pairedHostName,
                PairedIpAddress = _pairedIpAddress,
                PairingRecordInitialized = _pairingRecordInitialized
            };
            string json = JsonSerializer.Serialize(settings);
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(json);
                writer.Flush();
                stream.Flush(true);
            }

            if (File.Exists(SettingsPath))
                File.Replace(temporaryPath, SettingsPath, null, true);
            else
                File.Move(temporaryPath, SettingsPath);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private sealed record DeviceApiSettings
    {
        public int Schema { get; init; } = SettingsSchema;

        public string DeviceToken { get; init; } = string.Empty;

        public string? PairedHostName { get; init; }

        public string? PairedIpAddress { get; init; }

        public bool PairingRecordInitialized { get; init; }
    }
}
