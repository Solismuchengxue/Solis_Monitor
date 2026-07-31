#nullable enable

using System;
using System.IO;
using System.Threading;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Solis.Hardware;
using LibreHardwareMonitor.Solis.Settings;
using LibreHardwareMonitor.Utilities;

namespace LibreHardwareMonitor.Solis.Desktop;

public sealed class SolisDesktopBackend : IDisposable
{
    private static readonly TimeSpan HardwareCollectionInterval =
        TimeSpan.FromSeconds(1);

    private readonly object _lifecycleSync = new();
    private readonly string _configurationFilePath;
    private readonly Computer _computer;
    private readonly DesktopHardwareMetricsPump _hardwareMetricsPump;
    private readonly DesktopHardwareUpdateVisitor _updateVisitor = new();
    private Timer? _hardwareTimer;
    private bool _disposed;
    private bool _ownsRuntime = true;
    private bool _started;

    public SolisDesktopBackend()
    {
        Settings = new PersistentSettings();

        string executablePath = Environment.ProcessPath ??
            typeof(SolisDesktopBackend).Assembly.Location;
        string executableConfigPath =
            Path.ChangeExtension(executablePath, ".config");
        string legacyConfigPath = Path.Combine(
            Path.GetDirectoryName(executablePath) ?? AppContext.BaseDirectory,
            "LibreHardwareMonitor.config");

        _configurationFilePath =
            SolisSettingsMigration.GetUserConfigPath();
        SolisSettingsMigration.CopyExecutableConfigToUserDirectory(
            _configurationFilePath,
            executableConfigPath,
            legacyConfigPath);
        Settings.Load(_configurationFilePath);

        string preferredGpuId =
            Settings.GetValue("solis.gpu.hardwareId", string.Empty);
        string preferredNvmeId =
            Settings.GetValue("solis.nvme.hardwareId", string.Empty);

        Runtime = new SolisRuntime(
            Settings.GetValue("solis.network.interfaceId", string.Empty));
        _computer = new Computer(Settings);
        SolisDesktopHardwareProfile.Apply(_computer);

        _hardwareMetricsPump = new DesktopHardwareMetricsPump(
            () => _computer.Accept(_updateVisitor),
            () => HardwareMetricMapper.Map(
                InProcessHardwareSnapshotReader.Read(
                    _computer,
                    DateTimeOffset.UtcNow),
                preferredGpuId,
                preferredNvmeId),
            Runtime.UpdateHardware);
    }

    public SolisRuntime Runtime { get; }

    public PersistentSettings Settings { get; }

    public Exception? LastHardwareCollectionError { get; private set; }

    public void Start()
    {
        lock (_lifecycleSync)
        {
            ThrowIfDisposed();
            if (_started)
                return;

            try
            {
                _computer.Open();
                Runtime.Start();
                CollectHardware();
                _started = true;
                _hardwareTimer = new Timer(
                    OnHardwareTimer,
                    null,
                    HardwareCollectionInterval,
                    HardwareCollectionInterval);
            }
            catch
            {
                Runtime.Dispose();
                _computer.Close();
                throw;
            }
        }
    }

    public void Save()
    {
        lock (_lifecycleSync)
        {
            ThrowIfDisposed();
            Settings.Save(_configurationFilePath);
        }
    }

    public void ReleaseRuntimeOwnership()
    {
        lock (_lifecycleSync)
        {
            ThrowIfDisposed();
            _ownsRuntime = false;
        }
    }

    public void Dispose()
    {
        lock (_lifecycleSync)
        {
            if (_disposed)
                return;

            _started = false;
            _hardwareTimer?.Dispose();
            _hardwareTimer = null;
            if (_ownsRuntime)
                Runtime.Dispose();
            _computer.Close();
            Settings.Save(_configurationFilePath);
            _disposed = true;
        }
    }

    private void OnHardwareTimer(object? state)
    {
        lock (_lifecycleSync)
        {
            if (!_started || _disposed)
                return;

            CollectHardware();
        }
    }

    private void CollectHardware()
    {
        try
        {
            _hardwareMetricsPump.CollectOnce();
            LastHardwareCollectionError = null;
        }
        catch (Exception exception)
        {
            LastHardwareCollectionError = exception;
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SolisDesktopBackend));
    }
}
