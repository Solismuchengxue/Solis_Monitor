#nullable enable

using System;
using System.IO;

namespace LibreHardwareMonitor.Solis.Settings;

public static class SolisSettingsMigration
{
    public static string GetUserConfigPath(string? localApplicationData = null)
    {
        string? root = string.IsNullOrWhiteSpace(localApplicationData)
            ? Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData)
            : localApplicationData;
        if (string.IsNullOrWhiteSpace(root))
            throw new InvalidOperationException(
                "Local application data directory is unavailable.");

        return Path.Combine(
            Path.GetFullPath(root),
            "SolisMonitor",
            "SolisMonitor.config");
    }

    public static bool CopyExecutableConfigToUserDirectory(
        string userConfigPath,
        string executableConfigPath,
        string legacyConfigPath)
    {
        if (string.IsNullOrWhiteSpace(userConfigPath))
            throw new ArgumentException(
                "User config path is required.",
                nameof(userConfigPath));
        if (string.IsNullOrWhiteSpace(executableConfigPath))
            throw new ArgumentException(
                "Executable config path is required.",
                nameof(executableConfigPath));
        if (string.IsNullOrWhiteSpace(legacyConfigPath))
            throw new ArgumentException(
                "Legacy config path is required.",
                nameof(legacyConfigPath));

        string user = Path.GetFullPath(userConfigPath);
        string executable = Path.GetFullPath(executableConfigPath);
        string legacy = Path.GetFullPath(legacyConfigPath);
        Directory.CreateDirectory(
            Path.GetDirectoryName(user)
            ?? throw new InvalidOperationException(
                "User config directory is unavailable."));

        if (File.Exists(user))
            return false;

        string? source = File.Exists(executable)
            ? executable
            : File.Exists(legacy)
                ? legacy
                : null;
        if (source is null)
            return false;

        File.Copy(source, user, false);
        string sourceBackup = source + ".backup";
        string userBackup = user + ".backup";
        if (File.Exists(sourceBackup) && !File.Exists(userBackup))
            File.Copy(sourceBackup, userBackup, false);
        return true;
    }

    public static bool CopyLegacyExecutableConfig(
        string currentConfigPath,
        string legacyConfigPath)
    {
        if (string.IsNullOrWhiteSpace(currentConfigPath))
            throw new ArgumentException(
                "Current config path is required.",
                nameof(currentConfigPath));
        if (string.IsNullOrWhiteSpace(legacyConfigPath))
            throw new ArgumentException(
                "Legacy config path is required.",
                nameof(legacyConfigPath));

        string current = Path.GetFullPath(currentConfigPath);
        string legacy = Path.GetFullPath(legacyConfigPath);
        if (File.Exists(current) || !File.Exists(legacy))
            return false;

        File.Copy(legacy, current, false);
        return true;
    }
}
