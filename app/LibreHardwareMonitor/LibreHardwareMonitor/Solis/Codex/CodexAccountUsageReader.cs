#nullable enable

using System;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace LibreHardwareMonitor.Solis.Codex;

public sealed record CodexAccountReading(long? LifetimeTokens, double? CreditBalance, int? AvailableFullResets);

public static class CodexAccountUsageReader
{
    public static CodexAccountReading ParseAccountLimitsResponse(string? response)
    {
        var unknown = new CodexAccountReading(null, null, null);
        if (string.IsNullOrWhiteSpace(response)) return unknown;
        try
        {
            using JsonDocument document = JsonDocument.Parse(response);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.Number ||
                !id.TryGetInt32(out int responseId) || responseId != 3 ||
                !root.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Object)
                return unknown;
            JsonElement limits = default;
            if (result.TryGetProperty("rateLimitsByLimitId", out var buckets) &&
                buckets.ValueKind == JsonValueKind.Object && buckets.TryGetProperty("codex", out var main))
                limits = main;
            else if (result.TryGetProperty("rateLimits", out var legacy))
                limits = legacy;
            double? balance = null;
            if (limits.ValueKind == JsonValueKind.Object && limits.TryGetProperty("credits", out var credits) &&
                credits.ValueKind == JsonValueKind.Object && credits.TryGetProperty("balance", out var amount))
            {
                string? text = amount.ValueKind == JsonValueKind.String ? amount.GetString() :
                    amount.ValueKind == JsonValueKind.Number ? amount.GetRawText() : null;
                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) &&
                    double.IsFinite(value) && value >= 0)
                    balance = value;
            }
            int? resets = null;
            if (result.TryGetProperty("rateLimitResetCredits", out var resetCredits) &&
                resetCredits.ValueKind == JsonValueKind.Object &&
                resetCredits.TryGetProperty("availableCount", out var count) &&
                count.ValueKind == JsonValueKind.Number && count.TryGetInt32(out int available) && available >= 0)
                resets = available;
            return new(null, balance, resets);
        }
        catch (JsonException) { return unknown; }
    }

    private static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(20);

    public static long? ReadLifetimeTokens() => ReadAccount()?.LifetimeTokens;

    public static CodexAccountReading? ReadAccount()
    {
        string? executable = FindCodexExecutable();
        if (executable is null)
            return null;

        using Process process = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = "app-server --stdio",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        try
        {
            process.ErrorDataReceived += (_, _) => { };
            if (!process.Start())
                return null;
            process.BeginErrorReadLine();
            process.StandardInput.WriteLine(
                "{\"id\":1,\"method\":\"initialize\",\"params\":{\"clientInfo\":{\"name\":\"solis-monitor\",\"version\":\"0.1.0\"},\"capabilities\":{\"experimentalApi\":true}}}");
            process.StandardInput.Flush();
            if (!WaitForResponse(process, 1, ResponseTimeout, out _))
                return null;

            process.StandardInput.WriteLine("{\"method\":\"initialized\"}");
            process.StandardInput.WriteLine(
                "{\"id\":2,\"method\":\"account/usage/read\",\"params\":null}");
            process.StandardInput.Flush();
            if (!WaitForResponse(process, 2, ResponseTimeout, out string? response)) return null;
            long? lifetime = ParseLifetimeTokensResponse(response);
            process.StandardInput.WriteLine("{\"id\":3,\"method\":\"account/rateLimits/read\",\"params\":null}");
            process.StandardInput.Flush();
            CodexAccountReading limits = WaitForResponse(process, 3, ResponseTimeout, out string? limitResponse)
                ? ParseAccountLimitsResponse(limitResponse) : new(null, null, null);
            return limits with { LifetimeTokens = lifetime };
        }
        catch (Exception exception) when (
            exception is IOException or InvalidOperationException or JsonException or
            System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            return null;
        }
        finally
        {
            try
            {
                if (!process.HasExited)
                    process.Kill();
            }
            catch (InvalidOperationException)
            {
            }
        }
    }

    public static long? ParseLifetimeTokensResponse(string? line)
    {
        if (line is null || string.IsNullOrWhiteSpace(line))
            return null;
        using JsonDocument document = JsonDocument.Parse(line);
        JsonElement root = document.RootElement;
        if (!root.TryGetProperty("id", out JsonElement id) || id.GetInt32() != 2 ||
            !root.TryGetProperty("result", out JsonElement result) ||
            !result.TryGetProperty("summary", out JsonElement summary) ||
            !summary.TryGetProperty("lifetimeTokens", out JsonElement lifetime) ||
            lifetime.ValueKind != JsonValueKind.Number ||
            !lifetime.TryGetInt64(out long value) || value < 0)
        {
            return null;
        }
        return value;
    }

    private static bool WaitForResponse(
        Process process,
        int expectedId,
        TimeSpan timeout,
        out string? response)
    {
        DateTime deadline = DateTime.UtcNow.Add(timeout);
        response = null;
        while (!process.HasExited)
        {
            TimeSpan remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
                return false;
            Task<string?> read = process.StandardOutput.ReadLineAsync();
            if (!read.Wait(remaining))
                return false;
            string? line = read.Result;
            if (line is null)
                return false;
            try
            {
                using JsonDocument document = JsonDocument.Parse(line);
                if (document.RootElement.TryGetProperty("id", out JsonElement id) &&
                    id.TryGetInt32(out int value) && value == expectedId)
                {
                    response = line;
                    return true;
                }
            }
            catch (JsonException)
            {
            }
        }
        return false;
    }

    private static string? FindCodexExecutable()
    {
        string root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OpenAI", "Codex", "bin");
        if (!Directory.Exists(root))
            return null;
        return Directory.EnumerateFiles(root, "codex.exe", SearchOption.AllDirectories)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }
}
