using System;
using System.Diagnostics;
using System.Threading;

namespace LibreHardwareMonitor.Solis.Startup;

public sealed class SingleInstanceCoordinator : IDisposable
{
    private readonly EventWaitHandle _diagnosticsEvent;
    private readonly EventWaitHandle _showWindowEvent;
    private readonly Mutex _singleInstanceMutex;
    private bool _disposed;

    public SingleInstanceCoordinator(string instanceName = "SolisMonitor")
    {
        if (string.IsNullOrWhiteSpace(instanceName))
            throw new ArgumentException("Instance name is required.", nameof(instanceName));

        string sessionName = $"{instanceName}.{Process.GetCurrentProcess().SessionId}";
        _singleInstanceMutex = new Mutex(
            initiallyOwned: true,
            name: $@"Local\{sessionName}.SingleInstance",
            createdNew: out bool createdNew);
        IsPrimary = createdNew;
        _showWindowEvent = new EventWaitHandle(
            initialState: false,
            mode: EventResetMode.AutoReset,
            name: $@"Local\{sessionName}.ShowWindow");
        _diagnosticsEvent = new EventWaitHandle(
            initialState: false,
            mode: EventResetMode.AutoReset,
            name: $@"Local\{sessionName}.OpenDiagnostics");
    }

    public bool IsPrimary { get; private set; }

    public bool TryBecomePrimary(TimeSpan timeout)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SingleInstanceCoordinator));
        if (IsPrimary)
            return true;
        if (timeout < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout));

        try
        {
            IsPrimary = _singleInstanceMutex.WaitOne(timeout);
        }
        catch (AbandonedMutexException)
        {
            IsPrimary = true;
        }

        return IsPrimary;
    }

    public RegisteredWaitHandle RegisterShowWindowRequest(Action callback)
        => RegisterRequest(_showWindowEvent, callback);

    public RegisteredWaitHandle RegisterDiagnosticsRequest(Action callback)
        => RegisterRequest(_diagnosticsEvent, callback);

    private RegisteredWaitHandle RegisterRequest(
        EventWaitHandle requestEvent,
        Action callback)
    {
        if (!IsPrimary)
            throw new InvalidOperationException("Only the primary instance can receive requests.");
        if (callback == null)
            throw new ArgumentNullException(nameof(callback));

        return ThreadPool.RegisterWaitForSingleObject(
            requestEvent,
            (_, timedOut) =>
            {
                if (!timedOut)
                    callback();
            },
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);
    }

    public void SignalPrimaryInstance() => _showWindowEvent.Set();

    public void SignalDiagnosticsRequest() => _diagnosticsEvent.Set();

    public void Dispose()
    {
        if (_disposed)
            return;

        _diagnosticsEvent.Dispose();
        _showWindowEvent.Dispose();
        if (IsPrimary)
            _singleInstanceMutex.ReleaseMutex();
        _singleInstanceMutex.Dispose();
        _disposed = true;
    }
}
