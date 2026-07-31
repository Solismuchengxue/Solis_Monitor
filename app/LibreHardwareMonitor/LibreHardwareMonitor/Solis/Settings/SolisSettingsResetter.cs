#nullable enable

using System;
using System.IO;
using LibreHardwareMonitor.Utilities;

namespace LibreHardwareMonitor.Solis.Settings;

public static class SolisSettingsResetter
{
    private const string OpenDevicePageAfterRestartKey =
        "solis.reset.openDevicePageAfterRestart";

    private static readonly string[] LocalConfigurationFiles =
    [
        "settings.json",
        "weather.json"
    ];

    public static void ClearPersistentSettings(PersistentSettings settings)
    {
        if (settings is null)
            throw new ArgumentNullException(nameof(settings));

        settings.RemoveByPrefix("solis.");
        settings.Remove("startMinMenuItem");
    }

    public static void RequestDevicePageAfterRestart(
        PersistentSettings settings)
    {
        if (settings is null)
            throw new ArgumentNullException(nameof(settings));

        settings.SetValue(OpenDevicePageAfterRestartKey, true);
    }

    public static bool ConsumeDevicePageAfterRestart(
        PersistentSettings settings)
    {
        if (settings is null)
            throw new ArgumentNullException(nameof(settings));

        bool requested = settings.GetValue(
            OpenDevicePageAfterRestartKey,
            false);
        settings.Remove(OpenDevicePageAfterRestartKey);
        return requested;
    }

    public static void ClearLocalData(string settingsDirectory)
    {
        if (string.IsNullOrWhiteSpace(settingsDirectory))
            throw new ArgumentException(
                "Settings directory is required.",
                nameof(settingsDirectory));

        string directory = Path.GetFullPath(settingsDirectory);
        foreach (string fileName in LocalConfigurationFiles)
            TryDeleteFile(Path.Combine(directory, fileName));

        string notificationsDirectory = Path.Combine(directory, "Notifications");
        if (Directory.Exists(notificationsDirectory))
            Directory.Delete(notificationsDirectory, true);
    }

    private static void TryDeleteFile(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }
}
