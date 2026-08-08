#nullable enable

using System;
using System.IO;
using System.Text;

namespace LibreHardwareMonitor.Solis.Diagnostics;

public sealed class RuntimeErrorLog
{
    private const int ModuleCount = 2;
    private const int MaximumExceptionTypeLength = 160;
    private const int MaximumFileBytes = 524288;
    private readonly object _sync = new();
    private readonly ThrottleState[] _states = new ThrottleState[ModuleCount];
    private readonly string _settingsDirectory;
    private readonly int _maximumFileBytes;
    private readonly TimeSpan _minimumInterval;

    public RuntimeErrorLog(
        string settingsDirectory,
        int maximumFileBytes = 524288,
        TimeSpan? minimumInterval = null)
    {
        _settingsDirectory = settingsDirectory ?? throw new ArgumentNullException(nameof(settingsDirectory));
        _maximumFileBytes = Math.Clamp(maximumFileBytes, 0, MaximumFileBytes);
        _minimumInterval = minimumInterval ?? TimeSpan.FromMinutes(5);
        LogPath = Path.Combine(_settingsDirectory, "runtime-errors.log");
    }

    public string LogPath { get; }

    public void TryWrite(BackgroundCollectionFailure failure, DateTimeOffset now)
    {
        lock (_sync)
        {
            int index = failure.Module == BackgroundCollectionModule.Metrics ? 0 : 1;
            string exceptionType = FormatExceptionType(failure.ExceptionType);
            if (_states[index].ExceptionType == exceptionType &&
                now - _states[index].LastWrittenAt < _minimumInterval)
            {
                return;
            }

            try
            {
                WriteLine(failure, now, exceptionType);
                _states[index] = new ThrottleState(exceptionType, now);
            }
            catch (Exception exception) when (IsRecoverable(exception))
            {
            }
        }
    }

    private void WriteLine(BackgroundCollectionFailure failure, DateTimeOffset now, string exceptionType)
    {
        string module = failure.Module == BackgroundCollectionModule.Metrics ? "metrics" : "weather";
        string line = $"{now.ToUniversalTime():O} {module} {exceptionType} {unchecked((uint)failure.HResult):X8}\n";
        int lineLength = Encoding.ASCII.GetByteCount(line);

        Directory.CreateDirectory(_settingsDirectory);
        DeleteIfOversized(LogPath);
        DeleteIfOversized(LogPath + ".1");
        if (lineLength > _maximumFileBytes)
        {
            return;
        }

        if (File.Exists(LogPath) && new FileInfo(LogPath).Length + lineLength > _maximumFileBytes)
        {
            string backupPath = LogPath + ".1";
            if (File.Exists(backupPath))
            {
                File.Delete(backupPath);
            }

            File.Move(LogPath, backupPath);
        }

        File.AppendAllText(LogPath, line, Encoding.ASCII);
    }

    private void DeleteIfOversized(string path)
    {
        if (File.Exists(path) && new FileInfo(path).Length > _maximumFileBytes)
        {
            File.Delete(path);
        }
    }

    private static string FormatExceptionType(string exceptionType)
    {
        if (string.IsNullOrEmpty(exceptionType))
        {
            return "unknown";
        }

        var builder = new StringBuilder(Math.Min(exceptionType.Length, MaximumExceptionTypeLength));
        foreach (char value in exceptionType)
        {
            if (value == '\r' || value == '\n')
            {
                continue;
            }

            if (!IsAsciiTypeCharacter(value))
            {
                return "unknown";
            }

            if (builder.Length == MaximumExceptionTypeLength)
            {
                break;
            }

            builder.Append(value);
        }

        return builder.Length == 0 ? "unknown" : builder.ToString();
    }

    private static bool IsAsciiTypeCharacter(char value) =>
        value is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '+' or '_' or '`';

    private static bool IsRecoverable(Exception exception) =>
        exception is not OutOfMemoryException and not AccessViolationException;

    private readonly record struct ThrottleState(string? ExceptionType, DateTimeOffset LastWrittenAt);
}
