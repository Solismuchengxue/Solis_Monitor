namespace LibreHardwareMonitor.Solis.Notifications;

internal interface IUserNotificationService
{
    bool TryShow(string title, string body);
}
