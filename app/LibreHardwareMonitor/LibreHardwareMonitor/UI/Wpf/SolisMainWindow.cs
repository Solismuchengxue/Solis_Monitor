using System.Windows;

namespace LibreHardwareMonitor.UI.WpfViews;

public sealed class SolisMainWindow : Window
{
    public SolisMainWindow()
        : this(new SolisControlCenterView())
    {
    }

    public SolisMainWindow(SolisControlCenterView content)
    {
        Title = "Solis Monitor";
        Width = 1100;
        Height = 840;
        MinWidth = 980;
        MinHeight = 720;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Content = content ??
            throw new System.ArgumentNullException(nameof(content));
    }
}
