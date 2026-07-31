#nullable enable

namespace LibreHardwareMonitor.Solis.Codex;

public sealed record CodexQuotaReading(
    string? Name,
    double? RemainingPercent,
    string? ResetAtLocal);

public sealed record CodexMetricsReading(
    bool Online,
    string? LastActiveTask,
    double? ContextUsedPercent,
    double? ContextUsedK,
    double? ContextWindowK,
    double? WeeklyRemainingPercent,
    CodexQuotaReading? MainQuota,
    CodexQuotaReading? SparkQuota,
    string? ErrorCategory,
    string? ProjectName = null,
    string? Model = null,
    string? ReasoningEffort = null,
    double? TotalTokens = null,
    double? WeeklyUsedTokens = null)
{
    public static CodexMetricsReading Empty(string? errorCategory = null) =>
        new(false, null, null, null, null, null, null, null, errorCategory);
}
