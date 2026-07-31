#nullable enable

using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LibreHardwareMonitor.Solis.DeviceControl;

public sealed record DeviceDisplaySettings(
    int BrightnessPercent,
    bool NightEnabled,
    int NightStartMinute,
    int NightEndMinute,
    int UtcOffsetMinutes)
{
    public const int MinimumBrightness = 10;
    public const int MaximumBrightness = 100;

    public bool IsValid =>
        BrightnessPercent is >= MinimumBrightness and <= MaximumBrightness &&
        NightStartMinute is >= 0 and < 24 * 60 &&
        NightEndMinute is >= 0 and < 24 * 60 &&
        NightStartMinute != NightEndMinute &&
        UtcOffsetMinutes is >= -12 * 60 and <= 14 * 60;
}

public static class DeviceControlProtocol
{
    public static bool TryParseSettings(
        string json,
        out DeviceDisplaySettings? settings)
    {
        settings = null;
        if (string.IsNullOrWhiteSpace(json))
            return false;
        try
        {
            DeviceControlResponse? response =
                JsonSerializer.Deserialize<DeviceControlResponse>(json);
            if (response is null)
                return false;
            var candidate = new DeviceDisplaySettings(
                response.Brightness,
                response.NightEnabled,
                response.NightStart,
                response.NightEnd,
                response.UtcOffset);
            if (!candidate.IsValid)
                return false;
            settings = candidate;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private sealed record DeviceControlResponse
    {
        [JsonPropertyName("brightness")]
        public int Brightness { get; init; }

        [JsonPropertyName("night_enabled")]
        public bool NightEnabled { get; init; }

        [JsonPropertyName("night_start")]
        public int NightStart { get; init; }

        [JsonPropertyName("night_end")]
        public int NightEnd { get; init; }

        [JsonPropertyName("utc_offset")]
        public int UtcOffset { get; init; }
    }
}
