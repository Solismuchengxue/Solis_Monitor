#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LibreHardwareMonitor.Solis.Codex;

public sealed class CodexMetricsCollector
{
    private const int WeeklyWindowMinutes = 7 * 24 * 60;
    private const double WeeklyWindowToleranceMinutes = 1D;
    private const double UnixMillisecondsThreshold = 100_000_000_000D;
    private const string MainQuotaDisplayName = "主周额度";
    private const string SparkQuotaDisplayName = "GPT-5.3-Codex-Spark";
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan AccountUsageSuccessInterval = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan AccountUsageRetryInterval = TimeSpan.FromMinutes(5);

    private readonly string _codexRoot;
    private readonly TimeSpan _staleAfter;
    private readonly object _sync = new();
    private readonly Dictionary<string, SessionDescriptor> _sessionCache =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _weeklyReadLengths =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<long?>? _accountUsageReader;
    private readonly CodexWeeklyUsageTracker? _weeklyUsageTracker;
    private readonly CodexLocalWeeklyUsageReader _localWeeklyUsageReader;

    private DateTimeOffset _nextRefreshUtc = DateTimeOffset.MinValue;
    private string? _activePath;
    private string? _activeTask;
    private string? _activeProject;
    private string? _activeModel;
    private string? _activeEffort;
    private long _readLength;
    private TokenSample? _sample;
    private WeeklySample? _mainWeeklySample;
    private WeeklySample? _sparkWeeklySample;
    private string? _lastTokenDiagnostic;
    private string? _lastErrorCategory;
    private DateTimeOffset _nextAccountUsageRefreshUtc = DateTimeOffset.MinValue;
    private long? _accountLifetimeTokens;
    private long? _accountWeeklyUsedTokens;
    private long? _localWeeklyUsedTokens;
    private long? _weeklyUsedTokens;
    private int _accountUsageRefreshRunning;

    public CodexMetricsCollector()
        : this(null)
    {
    }

    public CodexMetricsCollector(string? settingsDirectory)
        : this(DefaultCodexRoot(), TimeSpan.FromMinutes(10),
               CodexAccountUsageReader.ReadLifetimeTokens,
               new CodexWeeklyUsageTracker(settingsDirectory))
    {
    }

    public CodexMetricsCollector(string codexRoot, TimeSpan staleAfter)
        : this(codexRoot, staleAfter, null, null)
    {
    }

    private CodexMetricsCollector(
        string codexRoot,
        TimeSpan staleAfter,
        Func<long?>? accountUsageReader,
        CodexWeeklyUsageTracker? weeklyUsageTracker)
    {
        if (string.IsNullOrWhiteSpace(codexRoot))
            throw new ArgumentException("Codex root is required.", nameof(codexRoot));
        if (staleAfter <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(staleAfter));

        _codexRoot = Path.GetFullPath(codexRoot);
        _staleAfter = staleAfter;
        _accountUsageReader = accountUsageReader;
        _weeklyUsageTracker = weeklyUsageTracker;
        _localWeeklyUsageReader = new CodexLocalWeeklyUsageReader(_codexRoot);
    }

    public string SessionsRoot => Path.Combine(_codexRoot, "sessions");

    public CodexMetricsReading Read(DateTimeOffset utcNow)
    {
        lock (_sync)
        {
            if (utcNow >= _nextRefreshUtc)
            {
                _nextRefreshUtc = utcNow.Add(RefreshInterval);
                Refresh();
                _localWeeklyUsedTokens = _localWeeklyUsageReader.Read(
                    _mainWeeklySample?.ResetAtLocal,
                    utcNow);
            }
            if (_accountLifetimeTokens is long lifetimeTokens)
            {
                _accountWeeklyUsedTokens = _weeklyUsageTracker?.Update(
                    lifetimeTokens,
                    _mainWeeklySample?.ResetAtLocal);
            }
            UpdateWeeklyUsedTokens();
            ScheduleAccountUsageRefresh(utcNow);

            bool online = _sample is not null &&
                          utcNow - _sample.TimestampUtc <= _staleAfter &&
                          utcNow >= _sample.TimestampUtc;
            return new CodexMetricsReading(
                online,
                _activeTask,
                _sample?.ContextUsedPercent,
                _sample?.ContextUsedK,
                _sample?.ContextWindowK,
                _mainWeeklySample?.WeeklyRemainingPercent ?? _sparkWeeklySample?.WeeklyRemainingPercent,
                _mainWeeklySample is null
                    ? null
                    : new CodexQuotaReading(
                        _mainWeeklySample.Name,
                        _mainWeeklySample.WeeklyRemainingPercent,
                        _mainWeeklySample.ResetAtLocal),
                _sparkWeeklySample is null
                    ? null
                    : new CodexQuotaReading(
                        _sparkWeeklySample.Name,
                        _sparkWeeklySample.WeeklyRemainingPercent,
                        _sparkWeeklySample.ResetAtLocal),
                _lastErrorCategory,
                _activeProject,
                _activeModel,
                _activeEffort,
                _accountUsageReader is null ? _sample?.TotalTokens : _accountLifetimeTokens,
                _weeklyUsedTokens);
        }
    }

    private void ScheduleAccountUsageRefresh(DateTimeOffset utcNow)
    {
        if (_accountUsageReader is null || utcNow < _nextAccountUsageRefreshUtc ||
            Interlocked.CompareExchange(ref _accountUsageRefreshRunning, 1, 0) != 0)
        {
            return;
        }

        _nextAccountUsageRefreshUtc = utcNow.Add(AccountUsageRetryInterval);
        _ = Task.Run(() =>
        {
            long? value = null;
            try
            {
                value = _accountUsageReader();
            }
            catch (Exception)
            {
            }

            lock (_sync)
            {
                if (value is >= 0)
                {
                    _accountLifetimeTokens = value;
                    _accountWeeklyUsedTokens = _weeklyUsageTracker?.Update(
                        value.Value,
                        _mainWeeklySample?.ResetAtLocal);
                    UpdateWeeklyUsedTokens();
                }
                _nextAccountUsageRefreshUtc = DateTimeOffset.UtcNow.Add(
                    value.HasValue ? AccountUsageSuccessInterval : AccountUsageRetryInterval);
            }
            Interlocked.Exchange(ref _accountUsageRefreshRunning, 0);
        });
    }

    private void UpdateWeeklyUsedTokens()
    {
        _weeklyUsedTokens = (_localWeeklyUsedTokens, _accountWeeklyUsedTokens) switch
        {
            (long local, long account) => Math.Max(local, account),
            (long local, null) => local,
            (null, long account) => account,
            _ => null
        };
    }

    private void Refresh()
    {
        try
        {
            string sessionsRoot = Path.Combine(_codexRoot, "sessions");
            if (!Directory.Exists(sessionsRoot))
            {
                _lastErrorCategory = "SessionsNotFound";
                return;
            }

            IReadOnlyDictionary<string, string> titles = ReadTitles(
                Path.Combine(_codexRoot, "session_index.jsonl"));
            IReadOnlyList<SessionFile> sessions = FindSessions(
                sessionsRoot, out bool invalidSessionMetadataSeen);
            SessionDescriptor? active = sessions
                .Where(session => session.Descriptor.IsMainThread)
                .Select(session => session.Descriptor)
                .FirstOrDefault();
            if (active is null)
            {
                _lastErrorCategory = invalidSessionMetadataSeen
                    ? "SessionMetadataInvalid"
                    : "SessionNotFound";
                return;
            }

            FileInfo file = new(active.Path);
            if (!string.Equals(_activePath, active.Path, StringComparison.OrdinalIgnoreCase) ||
                file.Length < _readLength)
            {
                _activePath = active.Path;
                _readLength = 0;
                _sample = null;
                _activeModel = null;
                _activeEffort = null;
                _lastTokenDiagnostic = null;
            }

            _activeTask = TaskName(active, titles);
            _activeProject = ProjectName(active);
            ReadNewTokenEvents(active.Path);
            ReadGlobalWeeklyEvents(sessions);
            _lastErrorCategory = _lastTokenDiagnostic ??
                                 (_sample is null ? "TokenCountNotFound" : null);
        }
        catch (Exception exception) when (IsExpectedReadFailure(exception))
        {
            _lastErrorCategory = exception.GetType().Name;
        }
    }

    private IReadOnlyList<SessionFile> FindSessions(
        string sessionsRoot,
        out bool invalidSessionMetadataSeen)
    {
        var sessions = new List<SessionFile>();
        invalidSessionMetadataSeen = false;

        foreach (string path in Directory.EnumerateFiles(
                     sessionsRoot, "*.jsonl", SearchOption.AllDirectories))
        {
            if (!_sessionCache.TryGetValue(path, out SessionDescriptor? descriptor))
            {
                descriptor = ReadSessionDescriptor(path);
                if (descriptor is null)
                {
                    invalidSessionMetadataSeen = true;
                    continue;
                }
                _sessionCache[path] = descriptor;
            }

            sessions.Add(new SessionFile(descriptor, File.GetLastWriteTimeUtc(path)));
        }

        sessions.Sort((left, right) => right.WriteTimeUtc.CompareTo(left.WriteTimeUtc));
        return sessions;
    }

    private static SessionDescriptor? ReadSessionDescriptor(string path)
    {
        using FileStream stream = OpenShared(path);
        using StreamReader reader = new(stream, Encoding.UTF8, true, 4096, false);
        string? line = reader.ReadLine();
        if (string.IsNullOrWhiteSpace(line))
            return null;

        try
        {
            using JsonDocument document = JsonDocument.Parse(line);
            JsonElement root = document.RootElement;
            if (!TryString(root, "type", out string? type) || type != "session_meta" ||
                !root.TryGetProperty("payload", out JsonElement payload) ||
                !TryString(payload, "id", out string? id) || id is null || id.Length == 0)
            {
                return null;
            }

            string? cwd = TryString(payload, "cwd", out string? value) ? value : null;
            bool isSubagent = payload.TryGetProperty("source", out JsonElement source) &&
                              source.ValueKind == JsonValueKind.Object &&
                              source.TryGetProperty("subagent", out _);
            return new SessionDescriptor(path, id, cwd, !isSubagent);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void ReadNewTokenEvents(string path)
    {
        long fileLength = new FileInfo(path).Length;
        if (_readLength > fileLength)
            _readLength = 0;

        _readLength = JsonlMarkerReader.ReadMatchingLines(
            path,
            _readLength,
            line =>
            {
                if (line.IndexOf("\"turn_context\"", StringComparison.Ordinal) >= 0)
                {
                    ReadTurnContext(line);
                    return;
                }
                if (line.IndexOf("\"token_count\"", StringComparison.Ordinal) < 0)
                    return;

                ParsedTokenEvent? parsed = ParseTokenCount(line, out string? diagnostic);
                if (parsed is null)
                {
                    if (diagnostic is not null)
                        _lastTokenDiagnostic = diagnostic;
                    return;
                }

                if (parsed.ContextUsedPercent.HasValue || parsed.ContextUsedK.HasValue ||
                    parsed.ContextWindowK.HasValue || parsed.TotalTokens.HasValue)
                {
                    _sample = new TokenSample(
                        parsed.TimestampUtc,
                        parsed.ContextUsedPercent,
                        parsed.ContextUsedK,
                        parsed.ContextWindowK,
                        parsed.TotalTokens);
                    _lastTokenDiagnostic = diagnostic;
                }

                foreach (ParsedQuotaSnapshot quota in parsed.Quotas)
                    UpdateWeeklySample(quota);
            },
            "\"turn_context\"",
            "\"token_count\"");

        _weeklyReadLengths[path] = _readLength;
    }

    private void ReadGlobalWeeklyEvents(IReadOnlyList<SessionFile> sessions)
    {
        foreach (SessionFile session in sessions)
        {
            DateTimeOffset? oldestCompleteTracked = OldestCompleteWeeklyTimestamp;
            if (oldestCompleteTracked is not null &&
                session.WriteTimeUtc < oldestCompleteTracked.Value.UtcDateTime)
            {
                break;
            }

            FileInfo file = new(session.Descriptor.Path);
            long readLength = _weeklyReadLengths.TryGetValue(file.FullName, out long cachedLength) &&
                              cachedLength <= file.Length
                ? cachedLength
                : 0;
            if (readLength == file.Length)
                continue;

            _weeklyReadLengths[file.FullName] = JsonlMarkerReader.ReadMatchingLines(
                file.FullName,
                readLength,
                line =>
                {
                    ParsedTokenEvent? parsed = ParseTokenCount(line, out _);
                    if (parsed is null)
                        return;

                    foreach (ParsedQuotaSnapshot quota in parsed.Quotas)
                        UpdateWeeklySample(quota);
                },
                "\"token_count\"");
        }
    }

    private DateTimeOffset? OldestCompleteWeeklyTimestamp =>
        _mainWeeklySample is null || _sparkWeeklySample is null
            ? null
            : (_mainWeeklySample.TimestampUtc < _sparkWeeklySample.TimestampUtc
                ? _mainWeeklySample.TimestampUtc
                : _sparkWeeklySample.TimestampUtc);

    private void UpdateWeeklySample(ParsedQuotaSnapshot sample)
    {
        if (!sample.WeeklyRemainingPercent.HasValue)
            return;

        WeeklySample? newSample = new(sample.TimestampUtc, sample.Name, sample.WeeklyRemainingPercent, sample.ResetAtLocal);
        if (sample.Kind == QuotaCategory.Spark)
        {
            if (_sparkWeeklySample is null || sample.TimestampUtc > _sparkWeeklySample.TimestampUtc)
                _sparkWeeklySample = newSample;
            return;
        }

        if (sample.Kind == QuotaCategory.Main)
        {
            if (_mainWeeklySample is null || sample.TimestampUtc > _mainWeeklySample.TimestampUtc)
                _mainWeeklySample = newSample;
            return;
        }

        if (sample.Kind == QuotaCategory.Unspecified)
        {
            if (sample.Source.Equals("primary", StringComparison.OrdinalIgnoreCase))
            {
                if (_mainWeeklySample is null || sample.TimestampUtc > _mainWeeklySample.TimestampUtc)
                    _mainWeeklySample = newSample;
                return;
            }
            if (sample.Source.Equals("secondary", StringComparison.OrdinalIgnoreCase) &&
                (_sparkWeeklySample is null || sample.TimestampUtc > _sparkWeeklySample.TimestampUtc))
            {
                _sparkWeeklySample = newSample;
            }
        }
    }

    private static ParsedTokenEvent? ParseTokenCount(
        string line,
        out string? diagnostic)
    {
        diagnostic = null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(line);
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("payload", out JsonElement payload) ||
                payload.ValueKind != JsonValueKind.Object ||
                !TryString(payload, "type", out string? type) || type != "token_count")
            {
                diagnostic = "TokenCountEnvelopeInvalid";
                return null;
            }

            if (!TryString(root, "timestamp", out string? timestampText) ||
                !DateTimeOffset.TryParse(timestampText, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out DateTimeOffset timestamp))
            {
                diagnostic = "TokenCountTimestampInvalid";
                return null;
            }

            double? contextUsedPercent = null;
            double? contextUsedK = null;
            double? contextWindowK = null;
            double? totalTokens = null;
            if (payload.TryGetProperty("info", out JsonElement info) &&
                info.ValueKind == JsonValueKind.Object)
            {
                if (info.TryGetProperty("total_token_usage", out JsonElement totalUsage) &&
                    totalUsage.ValueKind == JsonValueKind.Object &&
                    TryDouble(totalUsage, "total_tokens", out double parsedTotalTokens) &&
                    parsedTotalTokens >= 0)
                {
                    totalTokens = parsedTotalTokens;
                }

                if (info.TryGetProperty("last_token_usage", out JsonElement lastUsage) &&
                    lastUsage.ValueKind == JsonValueKind.Object &&
                    TryDouble(lastUsage, "input_tokens", out double inputTokens) &&
                    TryDouble(info, "model_context_window", out double contextWindow) &&
                    contextWindow > 0)
                {
                    contextUsedPercent = ClampPercent(inputTokens * 100D / contextWindow);
                    contextUsedK = Math.Round(inputTokens / 1000D, 2, MidpointRounding.AwayFromZero);
                    contextWindowK = Math.Round(contextWindow / 1000D, 2, MidpointRounding.AwayFromZero);
                }
            }

            if (!payload.TryGetProperty("rate_limits", out JsonElement limits))
            {
                bool hasMetrics = contextUsedPercent.HasValue || contextUsedK.HasValue ||
                                  contextWindowK.HasValue || totalTokens.HasValue;
                if (!hasMetrics)
                    diagnostic = "TokenCountFieldsMissing";
                return hasMetrics
                    ? new ParsedTokenEvent(timestamp.ToUniversalTime(), contextUsedPercent,
                        contextUsedK, contextWindowK, totalTokens, Array.Empty<ParsedQuotaSnapshot>())
                    : null;
            }

            if (limits.ValueKind != JsonValueKind.Object)
            {
                diagnostic = "RateLimitsInvalid";
                bool hasMetrics = contextUsedPercent.HasValue || contextUsedK.HasValue ||
                                  contextWindowK.HasValue || totalTokens.HasValue;
                return hasMetrics
                    ? new ParsedTokenEvent(timestamp.ToUniversalTime(), contextUsedPercent,
                        contextUsedK, contextWindowK, totalTokens, Array.Empty<ParsedQuotaSnapshot>())
                    : null;
            }

            string? rootLimitId = limits.TryGetProperty("limit_id", out JsonElement rootLimitIdElement) &&
                                  rootLimitIdElement.ValueKind == JsonValueKind.String
                ? rootLimitIdElement.GetString()
                : null;
            string? rootLimitName = limits.TryGetProperty("limit_name", out JsonElement rootLimitNameElement) &&
                                    rootLimitNameElement.ValueKind == JsonValueKind.String
                ? rootLimitNameElement.GetString()
                : null;

            var quotas = new List<ParsedQuotaSnapshot>(2);
            bool invalidRateLimitSeen = false;
            foreach (string source in new[] { "primary", "secondary" })
            {
                if (!limits.TryGetProperty(source, out JsonElement limit) ||
                    limit.ValueKind == JsonValueKind.Null)
                {
                    continue;
                }

                if (limit.ValueKind != JsonValueKind.Object)
                {
                    invalidRateLimitSeen = true;
                    continue;
                }

                if (!limit.TryGetProperty("window_minutes", out JsonElement windowMinutesElement) ||
                    !TryDouble(windowMinutesElement, out double windowMinutes))
                {
                    invalidRateLimitSeen = true;
                    continue;
                }
                if (!IsWeeklyWindow(windowMinutes))
                    continue;

                string? slotLimitId = limit.TryGetProperty("limit_id", out JsonElement slotLimitIdElement) &&
                                      slotLimitIdElement.ValueKind == JsonValueKind.String
                    ? slotLimitIdElement.GetString()
                    : null;
                string? slotLimitName = limit.TryGetProperty("limit_name", out JsonElement slotLimitNameElement) &&
                                       slotLimitNameElement.ValueKind == JsonValueKind.String
                    ? slotLimitNameElement.GetString()
                    : null;

                string? limitId = slotLimitId ?? rootLimitId;
                string? limitName = slotLimitName ?? rootLimitName;
                if (!TryDouble(limit, "used_percent", out double usedPercent))
                {
                    invalidRateLimitSeen = true;
                    continue;
                }

                string? resetAt = ParseResetAt(limit);
                double remaining = ClampPercent(100D - usedPercent);
                QuotaCategory category = ResolveQuotaCategory(limitId, limitName, source);
                quotas.Add(new ParsedQuotaSnapshot(
                    source,
                    category,
                    QuotaDisplayName(category),
                    remaining,
                    resetAt,
                    timestamp.ToUniversalTime()));
            }

            bool hasParsedData = contextUsedPercent.HasValue || contextUsedK.HasValue ||
                                 contextWindowK.HasValue || totalTokens.HasValue || quotas.Count > 0;
            if (invalidRateLimitSeen)
                diagnostic = "RateLimitsInvalid";
            else if (!hasParsedData)
                diagnostic = "TokenCountFieldsMissing";

            return hasParsedData
                ? new ParsedTokenEvent(timestamp.ToUniversalTime(), contextUsedPercent,
                    contextUsedK, contextWindowK, totalTokens, quotas)
                : null;
        }
        catch (JsonException)
        {
            diagnostic = "TokenCountInvalidJson";
            return null;
        }
    }

    private static string? ParseResetAt(JsonElement limit)
    {
        if (!limit.TryGetProperty("resets_at", out JsonElement resetsAtElement))
            return null;

        if (resetsAtElement.ValueKind == JsonValueKind.Number &&
            resetsAtElement.TryGetDouble(out double numericReset))
        {
            return FormatUnixReset(numericReset);
        }

        if (resetsAtElement.ValueKind == JsonValueKind.String &&
            resetsAtElement.GetString() is { Length: >0 } resetText)
        {
            if (double.TryParse(resetText, NumberStyles.Float, CultureInfo.InvariantCulture,
                    out double numericTextReset))
            {
                return FormatUnixReset(numericTextReset);
            }

            if (DateTimeOffset.TryParse(resetText, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out DateTimeOffset resetAt))
            {
                return FormatLocalReset(resetAt);
            }
        }

        return null;
    }

    private static string? FormatUnixReset(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return null;

        double milliseconds = Math.Abs(value) >= UnixMillisecondsThreshold
            ? value
            : value * 1000D;
        if (milliseconds < long.MinValue || milliseconds > long.MaxValue)
            return null;

        try
        {
            return FormatLocalReset(DateTimeOffset.FromUnixTimeMilliseconds(
                checked((long)Math.Round(milliseconds, MidpointRounding.AwayFromZero))));
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    private static string FormatLocalReset(DateTimeOffset resetAt) =>
        resetAt.ToLocalTime().ToString("MM-dd HH:mm", CultureInfo.InvariantCulture);

    private static QuotaCategory ResolveQuotaCategory(string? limitId, string? limitName, string source)
    {
        if (MatchesLimit(limitId, limitName, "codex_bengalfox", SparkQuotaDisplayName))
            return QuotaCategory.Spark;
        if (MatchesLimit(limitId, limitName, "codex", MainQuotaDisplayName))
            return QuotaCategory.Main;

        return source.Equals("secondary", StringComparison.OrdinalIgnoreCase)
            ? QuotaCategory.Spark
            : QuotaCategory.Main;
    }

    private static bool MatchesLimit(
        string? limitId,
        string? limitName,
        string expectedId,
        string expectedName) =>
        string.Equals(limitId, expectedId, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(limitName, expectedName, StringComparison.OrdinalIgnoreCase);

    private static string QuotaDisplayName(QuotaCategory category) => category == QuotaCategory.Spark
        ? SparkQuotaDisplayName
        : MainQuotaDisplayName;

    private static bool IsWeeklyWindow(double windowMinutes) =>
        Math.Abs(windowMinutes - WeeklyWindowMinutes) <= WeeklyWindowToleranceMinutes;

    private static IReadOnlyDictionary<string, string> ReadTitles(string path)
    {
        var titles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path))
            return titles;

        using FileStream stream = OpenShared(path);
        using StreamReader reader = new(stream, Encoding.UTF8, true, 4096, false);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(line);
                JsonElement root = document.RootElement;
                if (TryString(root, "id", out string? id) && id is not null && id.Length > 0 &&
                    TryString(root, "thread_name", out string? title) &&
                    title is not null && title.Length > 0)
                {
                    titles[id] = title.Trim();
                }
            }
            catch (JsonException)
            {
                // Ignore malformed index entries; later valid entries still win.
            }
        }

        return titles;
    }

    private static string TaskName(
        SessionDescriptor session,
        IReadOnlyDictionary<string, string> titles)
    {
        if (titles.TryGetValue(session.Id, out string? title) &&
            !string.IsNullOrWhiteSpace(title))
        {
            return title;
        }

        return ProjectName(session) ?? "Codex";
    }

    private static string? ProjectName(SessionDescriptor session)
    {
        string? cwd = session.Cwd;
        if (cwd is not null && cwd.Length > 0)
        {
            string trimmed = cwd.TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string directoryName = Path.GetFileName(trimmed);
            if (!string.IsNullOrWhiteSpace(directoryName))
                return directoryName;
        }

        return null;
    }

    private void ReadTurnContext(string line)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(line);
            JsonElement root = document.RootElement;
            if (!TryString(root, "type", out string? type) || type != "turn_context" ||
                !root.TryGetProperty("payload", out JsonElement payload))
            {
                return;
            }

            if (TryString(payload, "model", out string? model) &&
                model is not null &&
                !string.IsNullOrWhiteSpace(model))
                _activeModel = model.Trim();
            if (TryString(payload, "effort", out string? effort) &&
                effort is not null &&
                !string.IsNullOrWhiteSpace(effort))
                _activeEffort = effort.Trim();
        }
        catch (JsonException)
        {
            // Ignore malformed context records; a later valid record still wins.
        }
    }

    private static bool TryString(JsonElement element, string name, out string? value)
    {
        value = null;
        if (!element.TryGetProperty(name, out JsonElement property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString();
        return value is not null;
    }

    private static bool TryDouble(JsonElement element, string name, out double value)
    {
        value = 0;
        return element.TryGetProperty(name, out JsonElement property) &&
               property.ValueKind == JsonValueKind.Number &&
               property.TryGetDouble(out value) &&
               !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private static bool TryDouble(JsonElement element, out double value)
    {
        value = 0;
        return element.ValueKind == JsonValueKind.Number &&
               element.TryGetDouble(out value) &&
               !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private static FileStream OpenShared(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    private static double ClampPercent(double value) => Math.Max(0D, Math.Min(100D, value));

    private static bool IsExpectedReadFailure(Exception exception) => exception is
        IOException or
        UnauthorizedAccessException or
        JsonException or
        ArgumentException;

    private static string DefaultCodexRoot()
    {
        string? configured = Environment.GetEnvironmentVariable("CODEX_HOME");
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".codex");
    }

    private enum QuotaCategory
    {
        Unspecified,
        Main,
        Spark
    }

    private sealed record SessionDescriptor(
        string Path,
        string Id,
        string? Cwd,
        bool IsMainThread);

    private sealed record SessionFile(SessionDescriptor Descriptor, DateTime WriteTimeUtc);

    private sealed record TokenSample(
        DateTimeOffset TimestampUtc,
        double? ContextUsedPercent,
        double? ContextUsedK,
        double? ContextWindowK,
        double? TotalTokens);

    private sealed record WeeklySample(
        DateTimeOffset TimestampUtc,
        string? Name,
        double? WeeklyRemainingPercent,
        string? ResetAtLocal);

    private sealed record ParsedTokenEvent(
        DateTimeOffset TimestampUtc,
        double? ContextUsedPercent,
        double? ContextUsedK,
        double? ContextWindowK,
        double? TotalTokens,
        IReadOnlyList<ParsedQuotaSnapshot> Quotas);

    private sealed record ParsedQuotaSnapshot(
        string Source,
        QuotaCategory Kind,
        string? Name,
        double? WeeklyRemainingPercent,
        string? ResetAtLocal,
        DateTimeOffset TimestampUtc);
}
