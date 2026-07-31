#nullable enable

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace LibreHardwareMonitor.Solis.Codex;

public static class CodexAccountUsageReader
{
    private static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(20);

    public static long? ReadLifetimeTokens()
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
            return WaitForResponse(process, 2, ResponseTimeout, out string? response)
                ? ParseLifetimeTokensResponse(response)
                : null;
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
