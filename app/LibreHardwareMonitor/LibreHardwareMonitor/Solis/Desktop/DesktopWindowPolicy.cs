namespace LibreHardwareMonitor.Solis.Desktop;

public enum DesktopWindowRequest
{
    Close,
    Minimize,
    Exit
}

public enum DesktopWindowAction
{
    Hide,
    Shutdown
}

public static class DesktopWindowPolicy
{
    public static DesktopWindowAction Decide(DesktopWindowRequest request)
    {
        return request == DesktopWindowRequest.Exit
            ? DesktopWindowAction.Shutdown
            : DesktopWindowAction.Hide;
    }
}
