// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// Partial Copyright (C) Michael Möller <mmoeller@openhardwaremonitor.org> and Contributors.
// All Rights Reserved.

using System;
using System.IO;
using System.Linq;
using System.Security;
using System.Security.Principal;
using System.Windows.Forms;
using LibreHardwareMonitor.Solis.Startup;
using Microsoft.Win32;
using Microsoft.Win32.TaskScheduler;
using Action = Microsoft.Win32.TaskScheduler.Action;

namespace LibreHardwareMonitor.UI;

public class StartupManager
{
    private const string CurrentStartupName = "SolisMonitor";
    private const string LegacyStartupName = "LibreHardwareMonitor";
    private const string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private bool _startup;

    public StartupManager()
    {
        if (Environment.OSVersion.Platform >= PlatformID.Unix)
        {
            IsAvailable = false;
            return;
        }

        if (IsAdministrator() && TaskService.Instance.Connected)
        {
            IsAvailable = true;

            Task task = GetTask(CurrentStartupName) ?? GetTask(LegacyStartupName);
            if (task != null)
            {
                foreach (Action action in task.Definition.Actions)
                {
                    if (action.ActionType == TaskActionType.Execute && action is ExecAction execAction)
                    {
                        if (IsCurrentInstallationPath(execAction.Path))
                            _startup = true;
                    }
                }
            }
        }
        else
        {
            try
            {
                using (RegistryKey registryKey = Registry.CurrentUser.OpenSubKey(RegistryPath))
                {
                    string value = (string)registryKey?.GetValue(CurrentStartupName) ??
                                   (string)registryKey?.GetValue(LegacyStartupName);

                    if (value != null)
                    {
                        string startupCommand = BuildRegistryCommand();
                        _startup = value == Application.ExecutablePath ||
                                   value == startupCommand ||
                                   IsCurrentInstallationPath(ParseExecutablePath(value));
                    }
                }

                IsAvailable = true;
            }
            catch (SecurityException)
            {
                IsAvailable = false;
            }
        }
    }

    public void EnsureStartupArguments()
    {
        if (!_startup || !IsAvailable)
            return;

        if (IsAdministrator() && TaskService.Instance.Connected)
        {
            Task task = GetTask(CurrentStartupName);
            if (task == null)
            {
                CreateTask();
                DeleteTask(LegacyStartupName);
                return;
            }

            bool changed = false;
            foreach (Action action in task.Definition.Actions)
            {
                if (action.ActionType != TaskActionType.Execute ||
                    action is not ExecAction execAction ||
                    !execAction.Path.Equals(Application.ExecutablePath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!string.Equals(
                        execAction.Arguments,
                        StartupLaunchPolicy.WindowsStartupArgument,
                        StringComparison.Ordinal))
                {
                    execAction.Arguments = StartupLaunchPolicy.WindowsStartupArgument;
                    changed = true;
                }
            }

            if (changed)
                task.RegisterChanges();
            DeleteTask(LegacyStartupName);
        }
        else
        {
            using RegistryKey registryKey = Registry.CurrentUser.CreateSubKey(RegistryPath);
            registryKey?.SetValue(CurrentStartupName, BuildRegistryCommand());
            registryKey?.DeleteValue(LegacyStartupName, false);
        }
    }

    public bool IsAvailable { get; }

    public bool Startup
    {
        get { return _startup; }
        set
        {
            if (_startup != value)
            {
                if (IsAvailable)
                {
                    if (IsAdministrator() && TaskService.Instance.Connected)
                    {
                        if (value)
                            CreateTask();
                        else
                            DeleteTask();

                        _startup = value;
                    }
                    else
                    {
                        try
                        {
                            if (value)
                                CreateRegistryKey();
                            else
                                DeleteRegistryKey();

                            _startup = value;
                        }
                        catch (UnauthorizedAccessException)
                        {
                            throw new InvalidOperationException();
                        }
                    }
                }
                else
                {
                    throw new InvalidOperationException();
                }
            }
        }
    }

    private static bool IsAdministrator()
    {
        try
        {
            WindowsIdentity identity = WindowsIdentity.GetCurrent();
            WindowsPrincipal principal = new WindowsPrincipal(identity);

            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    private static Task GetTask(string name)
    {
        try
        {
            return TaskService.Instance.AllTasks.FirstOrDefault(
                x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return null;
        }
    }

    private void CreateTask()
    {
        TaskDefinition taskDefinition = TaskService.Instance.NewTask();
        taskDefinition.RegistrationInfo.Description = "Starts LibreHardwareMonitor on Windows startup.";

        taskDefinition.Triggers.Add(new LogonTrigger());

        taskDefinition.Settings.StartWhenAvailable = true;
        taskDefinition.Settings.DisallowStartIfOnBatteries = false;
        taskDefinition.Settings.StopIfGoingOnBatteries = false;
        taskDefinition.Settings.ExecutionTimeLimit = TimeSpan.Zero;
        taskDefinition.Settings.AllowHardTerminate = false;

        taskDefinition.Principal.RunLevel = TaskRunLevel.Highest;
        taskDefinition.Principal.LogonType = TaskLogonType.InteractiveToken;

        taskDefinition.Actions.Add(new ExecAction(
            Application.ExecutablePath,
            StartupLaunchPolicy.WindowsStartupArgument,
            Path.GetDirectoryName(Application.ExecutablePath)));

        TaskService.Instance.RootFolder.RegisterTaskDefinition(CurrentStartupName, taskDefinition);
    }

    private static void DeleteTask()
    {
        DeleteTask(CurrentStartupName);
        DeleteTask(LegacyStartupName);
    }

    private static void DeleteTask(string name)
    {
        Task task = GetTask(name);
        task?.Folder.DeleteTask(task.Name, false);
    }

    private static void CreateRegistryKey()
    {
        RegistryKey registryKey = Registry.CurrentUser.CreateSubKey(RegistryPath);
        registryKey?.SetValue(CurrentStartupName, BuildRegistryCommand());
        registryKey?.DeleteValue(LegacyStartupName, false);
    }

    private static string BuildRegistryCommand() =>
        $"\"{Application.ExecutablePath}\" {StartupLaunchPolicy.WindowsStartupArgument}";

    private static void DeleteRegistryKey()
    {
        RegistryKey registryKey = Registry.CurrentUser.CreateSubKey(RegistryPath);
        registryKey?.DeleteValue(CurrentStartupName, false);
        registryKey?.DeleteValue(LegacyStartupName, false);
    }

    private static bool IsCurrentInstallationPath(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            return false;

        string expectedDirectory = Path.GetDirectoryName(Application.ExecutablePath);
        string actualDirectory = Path.GetDirectoryName(executablePath.Trim('"'));
        return string.Equals(
            expectedDirectory,
            actualDirectory,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string ParseExecutablePath(string command)
    {
        string trimmed = command.Trim();
        if (trimmed.StartsWith("\"", StringComparison.Ordinal))
        {
            int end = trimmed.IndexOf('"', 1);
            return end > 1 ? trimmed.Substring(1, end - 1) : trimmed;
        }

        int separator = trimmed.IndexOf(' ');
        return separator > 0 ? trimmed.Substring(0, separator) : trimmed;
    }
}
