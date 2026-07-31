#nullable enable

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LibreHardwareMonitor.Solis.Weather;

public sealed record QWeatherSettings(
    bool Enabled,
    string ApiHost,
    string ApiKey,
    string Location,
    string? LocationId,
    double? Longitude = null,
    double? Latitude = null);

public sealed class QWeatherSettingsStore
{
    private static readonly byte[] ProtectionEntropy =
        Encoding.UTF8.GetBytes("SolisMonitor.QWeather.v1");

    public QWeatherSettingsStore(string? settingsDirectory = null)
    {
        SettingsDirectory = Path.GetFullPath(settingsDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SolisMonitor"));
        SettingsPath = Path.Combine(SettingsDirectory, "weather.json");
    }

    public string SettingsDirectory { get; }

    public string SettingsPath { get; }

    public QWeatherSettings Load()
    {
        if (!File.Exists(SettingsPath))
            return Disabled;

        try
        {
            StoredWeatherSettings? stored = JsonSerializer.Deserialize<StoredWeatherSettings>(
                File.ReadAllText(SettingsPath));
            if (stored?.Schema != 1)
                return Disabled;

            string? locationId = stored.LocationId?.Trim();
            var settings = new QWeatherSettings(
                stored.Enabled,
                stored.ApiHost?.Trim() ?? string.Empty,
                LoadApiKey(stored),
                stored.Location?.Trim() ?? string.Empty,
                string.IsNullOrWhiteSpace(locationId) ? null : locationId,
                stored.Longitude,
                stored.Latitude);
            if (string.IsNullOrWhiteSpace(stored.ApiKeyProtected) &&
                !string.IsNullOrWhiteSpace(stored.ApiKey))
            {
                TryMigrateLegacy(settings);
            }

            return settings;
        }
        catch (JsonException)
        {
            return Disabled;
        }
        catch (IOException)
        {
            return Disabled;
        }
        catch (UnauthorizedAccessException)
        {
            return Disabled;
        }
        catch (CryptographicException)
        {
            return Disabled;
        }
        catch (FormatException)
        {
            return Disabled;
        }
    }

    public void Save(QWeatherSettings settings)
    {
        if (settings is null)
            throw new ArgumentNullException(nameof(settings));

        Directory.CreateDirectory(SettingsDirectory);
        var stored = new StoredWeatherSettings
        {
            Schema = 1,
            Enabled = settings.Enabled,
            ApiHost = settings.ApiHost.Trim(),
            ApiKeyProtected = Protect(settings.ApiKey.Trim()),
            Location = settings.Location.Trim(),
            LocationId = string.IsNullOrWhiteSpace(settings.LocationId)
                ? null
                : settings.LocationId!.Trim(),
            Longitude = settings.Longitude,
            Latitude = settings.Latitude
        };

        string temporaryPath = $"{SettingsPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            string json = JsonSerializer.Serialize(stored);
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
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

    private static QWeatherSettings Disabled { get; } = new(false, string.Empty, string.Empty, string.Empty, null);

    private static string LoadApiKey(StoredWeatherSettings stored)
    {
        if (string.IsNullOrWhiteSpace(stored.ApiKeyProtected))
            return stored.ApiKey?.Trim() ?? string.Empty;

        byte[] plainBytes = ProtectedData.Unprotect(
            Convert.FromBase64String(stored.ApiKeyProtected),
            ProtectionEntropy,
            DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(plainBytes);
    }

    private static string Protect(string value)
    {
        byte[] protectedBytes = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(value),
            ProtectionEntropy,
            DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(protectedBytes);
    }

    private void TryMigrateLegacy(QWeatherSettings settings)
    {
        try
        {
            Save(settings);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (CryptographicException)
        {
        }
    }

    private sealed record StoredWeatherSettings
    {
        [JsonPropertyName("schema")]
        public int Schema { get; init; }

        [JsonPropertyName("enabled")]
        public bool Enabled { get; init; }

        [JsonPropertyName("apiHost")]
        public string? ApiHost { get; init; }

        [JsonPropertyName("apiKey")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ApiKey { get; init; }

        [JsonPropertyName("apiKeyProtected")]
        public string? ApiKeyProtected { get; init; }

        [JsonPropertyName("location")]
        public string? Location { get; init; }

        [JsonPropertyName("locationId")]
        public string? LocationId { get; init; }

        [JsonPropertyName("longitude")]
        public double? Longitude { get; init; }

        [JsonPropertyName("latitude")]
        public double? Latitude { get; init; }
    }
}
