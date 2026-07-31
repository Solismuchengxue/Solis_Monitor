// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// Partial Copyright (C) Michael Möller <mmoeller@openhardwaremonitor.org> and Contributors.
// All Rights Reserved.

using System;
using System.IO;
using System.Threading;
using LibreHardwareMonitor.Solis.Startup;
using LibreHardwareMonitor.UI;
using LibreHardwareMonitor.UI.WpfViews;
using Forms = System.Windows.Forms;

namespace LibreHardwareMonitor;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        Environment.ExitCode = DesktopHostLauncher.Run(
            args,
            () => RunWpf(args),
            () => RunLegacyWinForms(args));
    }

    private static int RunWpf(string[] args)
    {
        var application = new SolisApplication(args);
        return application.RunSolis();
    }

    private static int RunLegacyWinForms(string[] args)
    {
        Forms.Application.SetHighDpiMode(Forms.HighDpiMode.SystemAware);
        if (!AllRequiredFilesAvailable())
            return 0;

        using var instanceCoordinator = new SingleInstanceCoordinator();
        if (!instanceCoordinator.IsPrimary)
        {
            if (StartupLaunchPolicy.ShouldOpenDiagnostics(args))
                instanceCoordinator.SignalDiagnosticsRequest();
            else
                instanceCoordinator.SignalPrimaryInstance();

            if (!instanceCoordinator.TryBecomePrimary(
                    TimeSpan.FromMilliseconds(1500)))
            {
                return 0;
            }
        }

        Forms.Application.EnableVisualStyles();
        Forms.Application.SetCompatibleTextRenderingDefault(false);
        using (MainForm form = new MainForm(args))
        {
            _ = form.Handle;
            form.FormClosed += delegate
            {
                Forms.Application.Exit();
            };

            RegisteredWaitHandle showWindowRegistration =
                instanceCoordinator.RegisterShowWindowRequest(() =>
                {
                    if (form.IsDisposed)
                        return;

                    try
                    {
                        form.BeginInvoke(new Action(form.ShowControlCenterFromExternalLaunch));
                    }
                    catch (InvalidOperationException)
                    {
                        // The form is already shutting down.
                    }
                });
            RegisteredWaitHandle diagnosticsRegistration =
                instanceCoordinator.RegisterDiagnosticsRequest(() =>
                {
                    if (form.IsDisposed)
                        return;

                    try
                    {
                        form.BeginInvoke(new Action(form.ShowDiagnosticsFromExternalLaunch));
                    }
                    catch (InvalidOperationException)
                    {
                        // The form is already shutting down.
                    }
                });

            try
            {
                Forms.Application.Run();
            }
            finally
            {
                diagnosticsRegistration.Unregister(null);
                showWindowRegistration.Unregister(null);
            }
        }

        return 0;
    }

    private static bool IsFileAvailable(string fileName)
    {
        string path = Path.Combine(AppContext.BaseDirectory, fileName);
        if (!File.Exists(path))
        {
            Forms.MessageBox.Show("找不到以下文件：" + fileName +
                                  "\n请从压缩包中解压全部文件。", "错误",
                                  Forms.MessageBoxButtons.OK,
                                  Forms.MessageBoxIcon.Error);
            return false;
        }
        return true;
    }

    private static bool AllRequiredFilesAvailable()
    {
        if (!IsFileAvailable("Aga.Controls.dll"))
            return false;

        if (!IsFileAvailable("LibreHardwareMonitorLib.dll"))
            return false;

        if (!IsFileAvailable("OxyPlot.dll"))
            return false;

        if (!IsFileAvailable("OxyPlot.WindowsForms.dll"))
            return false;

        return true;
    }
}
