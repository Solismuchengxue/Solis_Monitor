using System;
using System.IO;
using System.Text.Json;

namespace LibreHardwareMonitor.Solis.Notifications;

internal sealed class WindowsNotificationService : IUserNotificationService
{
    private const string NotificationHostDirectory = "NotificationHost";
    private const string NotificationHostExecutable = "SolisMonitor.NotificationHost.exe";

    public bool TryShow(string title, string body)
    {
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(body))
            return false;

        string executablePath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            NotificationHostDirectory,
            NotificationHostExecutable);
        if (!File.Exists(executablePath))
            return false;

        string requestDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SolisMonitor",
            "Notifications");
        Directory.CreateDirectory(requestDirectory);

        string requestPath = Path.Combine(
            requestDirectory,
            $"notification-{Guid.NewGuid():N}.json");
        try
        {
            string json = JsonSerializer.Serialize(new NotificationRequest
            {
                Title = title,
                Body = body,
                Target = "diagnostics"
            });
            File.WriteAllText(requestPath, json);

            if (UnelevatedProcessLauncher.TryStart(executablePath, requestPath))
                return true;
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        TryDelete(requestPath);
        return false;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // A unique file is used for every request.
        }
    }

    private sealed class NotificationRequest
    {
        public string Title { get; set; }

        public string Body { get; set; }

        public string Target { get; set; }
    }
}
