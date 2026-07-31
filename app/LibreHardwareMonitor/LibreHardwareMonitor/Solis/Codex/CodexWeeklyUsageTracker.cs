#nullable enable

using System;
using System.IO;
using System.Text.Json;

namespace LibreHardwareMonitor.Solis.Codex;

public sealed class CodexWeeklyUsageTracker
{
    private const string FileName = "codex-weekly-usage.json";
    private readonly string _path;
    private State? _state;

    public CodexWeeklyUsageTracker(string? settingsDirectory = null)
    {
        string directory = string.IsNullOrWhiteSpace(settingsDirectory)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SolisMonitor")
            : Path.GetFullPath(settingsDirectory);
        _path = Path.Combine(directory, FileName);
        _state = Load();
    }

    public long? Update(long lifetimeTokens, string? mainQuotaResetAt)
    {
        if (lifetimeTokens < 0 || string.IsNullOrWhiteSpace(mainQuotaResetAt))
            return null;

        string resetAt = mainQuotaResetAt.Trim();
        if (_state is null ||
            !string.Equals(_state.MainQuotaResetAt, resetAt, StringComparison.Ordinal) ||
            lifetimeTokens < _state.BaselineLifetimeTokens)
        {
            _state = new State(resetAt, lifetimeTokens);
            Save(_state);
            return 0;
        }

        return lifetimeTokens - _state.BaselineLifetimeTokens;
    }

    private State? Load()
    {
        try
        {
            if (!File.Exists(_path))
                return null;

            State? state = JsonSerializer.Deserialize<State>(File.ReadAllText(_path));
            return state is not null &&
                   !string.IsNullOrWhiteSpace(state.MainQuotaResetAt) &&
                   state.BaselineLifetimeTokens >= 0
                ? state
                : null;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private void Save(State state)
    {
        try
        {
            string? directory = Path.GetDirectoryName(_path);
            if (string.IsNullOrEmpty(directory))
                return;

            Directory.CreateDirectory(directory);
            string temporaryPath = _path + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(state));
            File.Move(temporaryPath, _path, true);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private sealed record State(
        string MainQuotaResetAt,
        long BaselineLifetimeTokens);
}
