#nullable enable

using System;

namespace LibreHardwareMonitor.Solis.Diagnostics;

public enum BackgroundCollectionModule
{
    Metrics,
    Weather
}

public sealed record BackgroundCollectionFailure(
    BackgroundCollectionModule Module,
    string ExceptionType,
    int HResult);

public sealed class BackgroundCollectionGuard
{
    private readonly Action<BackgroundCollectionFailure, DateTimeOffset> _writeFailure;

    public BackgroundCollectionGuard(
        Action<BackgroundCollectionFailure, DateTimeOffset> writeFailure) =>
        _writeFailure = writeFailure ?? throw new ArgumentNullException(nameof(writeFailure));

    public bool Execute(
        BackgroundCollectionModule module,
        DateTimeOffset now,
        Action operation,
        Action<BackgroundCollectionFailure> onFailure)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(onFailure);
        try
        {
            operation();
            return true;
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            var failure = new BackgroundCollectionFailure(
                module,
                exception.GetType().FullName ?? exception.GetType().Name,
                exception.HResult);
            try
            {
                _writeFailure(failure, now);
            }
            catch (Exception logException) when (IsRecoverable(logException))
            {
            }

            onFailure(failure);
            return false;
        }
    }

    private static bool IsRecoverable(Exception exception) =>
        exception is not OutOfMemoryException and not AccessViolationException;
}
