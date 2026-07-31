using System.Diagnostics;
using System.Text.Json;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace SolisMonitor.NotificationHost;

internal static class Program
{
    private static ManualResetEventSlim? _activationCompleted;
    private static bool _activationSucceeded;

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 1 &&
            string.Equals(args[0], "--unregister-all", StringComparison.Ordinal))
        {
            try
            {
                AppNotificationManager.Default.UnregisterAll();
                return 0;
            }
            catch
            {
                return 1;
            }
        }

        if (args.Length == 1 &&
            !string.IsNullOrWhiteSpace(args[0]) &&
            File.Exists(args[0]))
        {
            return SendNotification(args[0]);
        }

        return HandleNotificationActivation();
    }

    private static int SendNotification(string requestArgument)
    {
        string requestPath = Path.GetFullPath(requestArgument);
        string resultPath = requestPath + ".result";
        bool registered = false;

        try
        {
            NotificationRequest? request = JsonSerializer.Deserialize<NotificationRequest>(
                File.ReadAllText(requestPath));
            if (request is null ||
                string.IsNullOrWhiteSpace(request.Title) ||
                string.IsNullOrWhiteSpace(request.Body))
            {
                throw new InvalidDataException("Notification title and body are required.");
            }

            AppNotificationManager manager = AppNotificationManager.Default;
            manager.NotificationInvoked += OnNotificationInvoked;
            manager.Register();
            registered = true;

            var notification = new AppNotificationBuilder()
                .AddArgument(
                    "target",
                    string.IsNullOrWhiteSpace(request.Target)
                        ? "diagnostics"
                        : request.Target)
                .AddText(request.Title)
                .AddText(request.Body)
                .BuildNotification();
            manager.Show(notification);

            File.WriteAllText(resultPath, "ok");
            return 0;
        }
        catch (Exception exception)
        {
            File.WriteAllText(resultPath, exception.ToString());
            return 1;
        }
        finally
        {
            if (registered)
            {
                try
                {
                    AppNotificationManager.Default.Unregister();
                }
                catch
                {
                    // Delivery already succeeded; cleanup must not change the exit code.
                }
            }
            AppNotificationManager.Default.NotificationInvoked -= OnNotificationInvoked;

            try
            {
                File.Delete(requestPath);
            }
            catch
            {
                // The next request uses a unique file, so cleanup failure is harmless.
            }
        }
    }

    private static int HandleNotificationActivation()
    {
        AppNotificationManager manager = AppNotificationManager.Default;
        using var activationCompleted = new ManualResetEventSlim();
        _activationCompleted = activationCompleted;
        _activationSucceeded = false;
        bool registered = false;

        try
        {
            manager.NotificationInvoked += OnNotificationInvoked;
            manager.Register();
            registered = true;

            AppActivationArguments activatedArgs =
                AppInstance.GetCurrent().GetActivatedEventArgs();
            if (activatedArgs.Kind == ExtendedActivationKind.AppNotification &&
                activatedArgs.Data is AppNotificationActivatedEventArgs notificationArgs)
            {
                OnNotificationInvoked(manager, notificationArgs);
            }

            activationCompleted.Wait(TimeSpan.FromSeconds(10));
            return _activationSucceeded ? 0 : 1;
        }
        catch
        {
            return 1;
        }
        finally
        {
            _activationCompleted = null;
            if (registered)
            {
                try
                {
                    manager.Unregister();
                }
                catch
                {
                }
            }

            manager.NotificationInvoked -= OnNotificationInvoked;
        }
    }

    private static void OnNotificationInvoked(
        AppNotificationManager sender,
        AppNotificationActivatedEventArgs args)
    {
        string target = args.Arguments.TryGetValue("target", out string? value)
            ? value
            : "diagnostics";
        _activationSucceeded =
            string.Equals(target, "diagnostics", StringComparison.OrdinalIgnoreCase) &&
            TryOpenDiagnostics();
        _activationCompleted?.Set();
    }

    private static bool TryOpenDiagnostics()
    {
        string eventName =
            $@"Local\SolisMonitor.{Process.GetCurrentProcess().SessionId}.OpenDiagnostics";
        try
        {
            using EventWaitHandle requestEvent =
                EventWaitHandle.OpenExisting(eventName);
            return requestEvent.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            return TryStartMainApplication();
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool TryStartMainApplication()
    {
        string executablePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "SolisMonitor.exe"));
        if (!File.Exists(executablePath))
            return false;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = "--open-diagnostics",
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(executablePath)!
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    private sealed class NotificationRequest
    {
        public string Title { get; set; } = string.Empty;

        public string Body { get; set; } = string.Empty;

        public string Target { get; set; } = "diagnostics";
    }
}
