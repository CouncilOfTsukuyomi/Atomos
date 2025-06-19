using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace PenumbraModForwarder.UI.Extensions;

public static class HiddenWindows
{
    public static void HideMainWindow()
    {
        if (App.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
            desktop.MainWindow != null)
        {
            // Minimize it (helpful on some platforms)
            desktop.MainWindow.WindowState = WindowState.Minimized;

            // Remove from taskbar
            desktop.MainWindow.ShowInTaskbar = false;

            // Finally hide
            desktop.MainWindow.Hide();
        }
    }
}