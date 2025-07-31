using System;
using System.Threading.Tasks;
using Atomos.UI.Interfaces;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using NLog;
using OsNotifications;

namespace Atomos.UI.Services;

public class SystemNotificationService : ISystemNotificationService
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
    
    public SystemNotificationService()
    {
        Notifications.BundleIdentifier = "Atomos";
        Notifications.SetGuiApplication(true);
    }
    
    public bool IsWindowHidden
    {
        get
        {
            try
            {
                if (Dispatcher.UIThread.CheckAccess())
                {
                    return GetWindowHiddenState();
                }
                else
                {
                    return Dispatcher.UIThread.Invoke(GetWindowHiddenState);
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to check window hidden state, defaulting to false");
                return false;
            }
        }
    }
    
    private bool GetWindowHiddenState()
    {
        if (App.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
            desktop.MainWindow != null)
        {
            return !desktop.MainWindow.IsVisible ||
                   desktop.MainWindow.WindowState == WindowState.Minimized ||
                   !desktop.MainWindow.ShowInTaskbar;
        }
        return false;
    }
    
    public async Task ShowSystemNotificationAsync(string title, string message, NotificationType type = NotificationType.Information)
    {
        try
        {
            _logger.Debug("Showing system notification: {Title} - {Message} (Type: {Type})", title, message, type);
            
            await Task.Run(() =>
            {
                try
                {
                    Notifications.ShowNotification(title, message);
                    _logger.Debug("System notification shown successfully");
                }
                catch (PlatformNotSupportedException ex)
                {
                    _logger.Warn(ex, "System notifications not supported on this platform");
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to show system notification");
                }
            });
            
            // Small delay to ensure notification is processed (especially important on Windows)
            await Task.Delay(100);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to show system notification");
        }
    }
    
    private void RestoreMainWindow()
    {
        try
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (App.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
                    desktop.MainWindow != null)
                {
                    desktop.MainWindow.Show();
                    desktop.MainWindow.WindowState = WindowState.Normal;
                    desktop.MainWindow.ShowInTaskbar = true;
                    desktop.MainWindow.Activate();
                    desktop.MainWindow.Focus();
                }
            });
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to restore main window");
        }
    }
}