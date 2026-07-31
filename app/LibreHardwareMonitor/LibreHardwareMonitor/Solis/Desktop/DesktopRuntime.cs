using System;

namespace LibreHardwareMonitor.Solis.Desktop;

public sealed class DesktopRuntime : IDisposable
{
    private readonly DesktopRuntimeOptions _options;
    private bool _disposed;
    private bool _started;

    public DesktopRuntime(DesktopRuntimeOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public void Start()
    {
        ThrowIfDisposed();
        if (_started)
            return;

        _options.Start();
        _started = true;
    }

    public void Stop()
    {
        if (_disposed || !_started)
            return;

        _options.Stop();
        _started = false;
    }

    public void Save()
    {
        ThrowIfDisposed();
        _options.Save();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        Stop();
        _options.Dispose();
        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(DesktopRuntime));
    }
}
