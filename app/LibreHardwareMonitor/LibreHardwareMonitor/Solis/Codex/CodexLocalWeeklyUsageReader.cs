#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace LibreHardwareMonitor.Solis.Codex;

public sealed class CodexLocalWeeklyUsageReader
{
    private readonly string _sessionsRoot;
    private readonly Dictionary<string, FileState> _files =
        new(StringComparer.OrdinalIgnoreCase);
    private string? _periodKey;
    private DateTimeOffset _periodStartUtc;
    private long _totalTokens;

    public CodexLocalWeeklyUsageReader(string codexRoot)
    {
        if (string.IsNullOrWhiteSpace(codexRoot))
            throw new ArgumentException("Codex root is required.", nameof(codexRoot));

        _sessionsRoot = Path.Combine(Path.GetFullPath(codexRoot), "sessions");
    }

    public long? Read(string? mainQuotaResetAt, DateTimeOffset utcNow)
    {
        if (string.IsNullOrWhiteSpace(mainQuotaResetAt) ||
            !TryGetPeriodStart(mainQuotaResetAt.Trim(), utcNow, out DateTimeOffset periodStartUtc))
        {
            return null;
        }

        string periodKey = mainQuotaResetAt.Trim();
        if (!string.Equals(_periodKey, periodKey, StringComparison.Ordinal) ||
            _periodStartUtc != periodStartUtc)
        {
            _periodKey = periodKey;
            _periodStartUtc = periodStartUtc;
            _files.Clear();
            _totalTokens = 0;
        }

        if (!Directory.Exists(_sessionsRoot))
            return null;

        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in Directory.EnumerateFiles(
                     _sessionsRoot, "*.jsonl", SearchOption.AllDirectories))
        {
            FileInfo file = new(path);
            if (file.LastWriteTimeUtc < _periodStartUtc.UtcDateTime)
                continue;

            seenPaths.Add(path);

            if (!_files.TryGetValue(path, out FileState? state))
            {
                state = new FileState(IsSubagentSession(path));
                _files[path] = state;
            }
            else if (file.Length < state.ReadLength)
            {
                _totalTokens -= state.Contribution;
                state = new FileState(IsSubagentSession(path));
                _files[path] = state;
            }

            if (state.IsSubagent)
                continue;

            ReadNewEvents(path, state);
        }

        var missingPaths = new List<string>();
        foreach (KeyValuePair<string, FileState> entry in _files)
        {
            if (!seenPaths.Contains(entry.Key))
                missingPaths.Add(entry.Key);
        }
        foreach (string path in missingPaths)
        {
            _totalTokens -= _files[path].Contribution;
            _files.Remove(path);
        }

        return Math.Max(0, _totalTokens);
    }

    private void ReadNewEvents(string path, FileState state)
    {
        try
        {
            state.ReadLength = JsonlMarkerReader.ReadMatchingLines(
                path,
                state.ReadLength,
                line =>
                {
                    if (!TryReadTokenEvent(
                            line,
                            out DateTimeOffset timestampUtc,
                            out long totalTokens))
                    {
                        return;
                    }

                    if (timestampUtc >= _periodStartUtc)
                    {
                        long delta = state.PreviousTotalTokens is long previous
                            ? totalTokens >= previous ? totalTokens - previous : totalTokens
                            : totalTokens;
                        state.Contribution += delta;
                        _totalTokens += delta;
                    }

                    state.PreviousTotalTokens = totalTokens;
                },
                "\"token_count\"");
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static bool TryReadTokenEvent(
        string line,
        out DateTimeOffset timestampUtc,
        out long totalTokens)
    {
        timestampUtc = default;
        totalTokens = 0;
        try
        {
            using JsonDocument document = JsonDocument.Parse(line);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("timestamp", out JsonElement timestampElement) ||
                timestampElement.ValueKind != JsonValueKind.String ||
                !DateTimeOffset.TryParse(
                    timestampElement.GetString(),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out DateTimeOffset timestamp) ||
                !root.TryGetProperty("payload", out JsonElement payload) ||
                payload.ValueKind != JsonValueKind.Object ||
                !payload.TryGetProperty("type", out JsonElement type) ||
                type.ValueKind != JsonValueKind.String ||
                type.GetString() != "token_count" ||
                !payload.TryGetProperty("info", out JsonElement info) ||
                info.ValueKind != JsonValueKind.Object ||
                !info.TryGetProperty("total_token_usage", out JsonElement usage) ||
                usage.ValueKind != JsonValueKind.Object ||
                !usage.TryGetProperty("total_tokens", out JsonElement total) ||
                total.ValueKind != JsonValueKind.Number ||
                !total.TryGetInt64(out totalTokens) ||
                totalTokens < 0)
            {
                return false;
            }

            timestampUtc = timestamp.ToUniversalTime();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsSubagentSession(string path)
    {
        try
        {
            using FileStream stream = new(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using StreamReader reader = new(stream);
            string? line = reader.ReadLine();
            if (string.IsNullOrWhiteSpace(line))
                return false;

            using JsonDocument document = JsonDocument.Parse(line);
            JsonElement root = document.RootElement;
            return root.TryGetProperty("payload", out JsonElement payload) &&
                   payload.TryGetProperty("source", out JsonElement source) &&
                   source.ValueKind == JsonValueKind.Object &&
                   source.TryGetProperty("subagent", out _);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }

    private static bool TryGetPeriodStart(
        string resetAt,
        DateTimeOffset utcNow,
        out DateTimeOffset periodStartUtc)
    {
        periodStartUtc = default;
        if (!DateTime.TryParseExact(
                resetAt,
                "MM-dd HH:mm",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTime parsed))
        {
            return false;
        }

        DateTime localNow = utcNow.ToLocalTime().DateTime;
        DateTime? nextResetLocal = null;
        for (int year = localNow.Year - 1; year <= localNow.Year + 1; year++)
        {
            DateTime candidate;
            try
            {
                candidate = new DateTime(
                    year, parsed.Month, parsed.Day, parsed.Hour, parsed.Minute, 0,
                    DateTimeKind.Unspecified);
            }
            catch (ArgumentOutOfRangeException)
            {
                continue;
            }

            TimeSpan distance = candidate - localNow;
            if (distance < TimeSpan.FromDays(-1) || distance > TimeSpan.FromDays(8))
                continue;
            if (nextResetLocal is null ||
                Math.Abs(distance.TotalMinutes) <
                Math.Abs((nextResetLocal.Value - localNow).TotalMinutes))
            {
                nextResetLocal = candidate;
            }
        }

        if (nextResetLocal is null)
            return false;

        DateTime periodStartLocal = nextResetLocal.Value.AddDays(-7);
        periodStartUtc = new DateTimeOffset(
            periodStartLocal,
            TimeZoneInfo.Local.GetUtcOffset(periodStartLocal)).ToUniversalTime();
        return true;
    }

    private sealed class FileState
    {
        public FileState(bool isSubagent)
        {
            IsSubagent = isSubagent;
        }

        public bool IsSubagent { get; }

        public long ReadLength { get; set; }

        public long? PreviousTotalTokens { get; set; }

        public long Contribution { get; set; }
    }
}
