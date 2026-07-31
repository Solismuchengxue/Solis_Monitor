#nullable enable

using System;
using System.Threading;
using System.Windows;
using LibreHardwareMonitor.Solis.Startup;

namespace LibreHardwareMonitor.UI.WpfViews;

public sealed class SolisApplication : Application
{
    private readonly string[] _arguments;
    private SingleInstanceCoordinator? _instanceCoordinator;
    private RegisteredWaitHandle? _diagnosticsRegistration;
    private RegisteredWaitHandle? _showWindowRegistration;
    private SolisWpfDesktopHost? _desktopHost;
    private MainForm? _legacyDeveloperForm;

    public SolisApplication(string[] arguments)
    {
        _arguments = arguments is null
            ? Array.Empty<string>()
            : (string[])arguments.Clone();
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
    }

    public int RunSolis() => Run();

    protected override void OnStartup(StartupEventArgs eventArgs)
    {
        base.OnStartup(eventArgs);

        _instanceCoordinator = new SingleInstanceCoordinator();
        if (!_instanceCoordinator.IsPrimary)
        {
            if (StartupLaunchPolicy.ShouldOpenDiagnostics(_arguments))
                _instanceCoordinator.SignalDiagnosticsRequest();
            else
                _instanceCoordinator.SignalPrimaryInstance();

            if (!_instanceCoordinator.TryBecomePrimary(
                    TimeSpan.FromMilliseconds(1500)))
            {
                Shutdown();
                return;
            }
        }

        _desktopHost = new SolisWpfDesktopHost(RequestLegacyDeveloperMode);
        var requestDispatcher = new WpfSingleInstanceRequestDispatcher(
            action => Dispatcher.BeginInvoke(action),
            _desktopHost.ShowWindow,
            _desktopHost.ShowServicePage);
        _showWindowRegistration =
            _instanceCoordinator.RegisterShowWindowRequest(
                requestDispatcher.RequestShowWindow);
        _diagnosticsRegistration =
            _instanceCoordinator.RegisterDiagnosticsRequest(
                requestDispatcher.RequestDiagnostics);

        bool openDiagnostics =
            StartupLaunchPolicy.ShouldOpenDiagnostics(_arguments);
        bool openDevicePage = _desktopHost.ConsumeDevicePageAfterReset();
        bool showWindow = openDiagnostics ||
                          openDevicePage ||
                          !StartupLaunchPolicy.ShouldStartHidden(
                              _arguments,
                              _desktopHost.SilentStartup);

        _desktopHost.Start(showWindow);
        if (openDiagnostics)
            _desktopHost.ShowServicePage();
        else if (openDevicePage)
            _desktopHost.ShowDevicePage();
    }

    protected override void OnExit(ExitEventArgs eventArgs)
    {
        _diagnosticsRegistration?.Unregister(null);
        _diagnosticsRegistration = null;
        _showWindowRegistration?.Unregister(null);
        _showWindowRegistration = null;
        _desktopHost?.Dispose();
        _desktopHost = null;
        _legacyDeveloperForm?.Dispose();
        _legacyDeveloperForm = null;
        _instanceCoordinator?.Dispose();
        _instanceCoordinator = null;

        base.OnExit(eventArgs);
    }

    private void RequestLegacyDeveloperMode()
    {
        if (_legacyDeveloperForm is not null)
        {
            _legacyDeveloperForm.ShowDeveloperModeFromExternalLaunch();
            return;
        }

        _diagnosticsRegistration?.Unregister(null);
        _diagnosticsRegistration = null;
        _showWindowRegistration?.Unregister(null);
        _showWindowRegistration = null;

        System.Windows.Forms.Application.EnableVisualStyles();
        SolisWpfDesktopHost desktopHost = _desktopHost ??
            throw new InvalidOperationException("WPF desktop host is unavailable.");
        MainForm legacyDeveloperForm = new(
            new[] { DesktopHostSelector.LegacyUiArgument },
            desktopHost.Runtime);
        legacyDeveloperForm.FormClosed += (_, _) =>
        {
            _legacyDeveloperForm = null;
            Dispatcher.BeginInvoke(new Action(Shutdown));
        };
        _legacyDeveloperForm = legacyDeveloperForm;

        if (_instanceCoordinator is not null)
        {
            _showWindowRegistration =
                _instanceCoordinator.RegisterShowWindowRequest(() =>
                    Dispatcher.BeginInvoke(() =>
                        _legacyDeveloperForm?
                            .ShowControlCenterFromExternalLaunch()));
            _diagnosticsRegistration =
                _instanceCoordinator.RegisterDiagnosticsRequest(() =>
                    Dispatcher.BeginInvoke(() =>
                        _legacyDeveloperForm?
                            .ShowDiagnosticsFromExternalLaunch()));
        }

        legacyDeveloperForm.ShowDeveloperModeFromExternalLaunch();
        desktopHost.CloseForHostSwitch(preserveRuntime: true);
        _desktopHost = null;
    }
}
