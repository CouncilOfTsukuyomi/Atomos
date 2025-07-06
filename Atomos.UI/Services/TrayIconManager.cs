
using System;
using System.IO;
using Atomos.UI.Interfaces;
using Avalonia.Controls;
using Avalonia.Threading;
using SharedResources;

namespace Atomos.UI.Services;

public class TrayIconManager : ITrayIconManager, IDisposable
{
    private TrayIcon _trayIcon;
    private readonly ITrayIconController _trayIconController;
    private bool _isInitialized = false;
    private bool _disposed = false;
    private readonly object _lock = new object();

    public TrayIconManager(ITrayIconController trayIconController)
    {
        _trayIconController = trayIconController;
    }

    public void InitializeTrayIcon()
    {
        lock (_lock)
        {
            if (_isInitialized || _disposed)
            {
                return;
            }
            
            DisposeTrayIconSync();

            var iconStream = ResourceLoader.GetResourceStream("Purple_arrow_cat_icon.ico");
            if (iconStream == null)
            {
                throw new FileNotFoundException("Tray icon resource not found.");
            }

            _trayIcon = new TrayIcon
            {
                Icon = new WindowIcon(iconStream),
                ToolTipText = GetMinimalTooltipText(),
                Menu = CreateMinimalTrayMenu()
            };
            
            if (Dispatcher.UIThread.CheckAccess())
            {
                _trayIcon.IsVisible = true;
            }
            else
            {
                Dispatcher.UIThread.Post(() => _trayIcon.IsVisible = true);
            }
            
            _isInitialized = true;
        }
    }

    private string GetMinimalTooltipText()
    {
        var connectionStatus = _trayIconController.GetConnectionStatus();
        var notificationCount = _trayIconController.GetActiveNotificationsCount();
        
        var statusIndicator = connectionStatus switch
        {
            "Connected" => "🟢",
            "Disconnected" => "🔴", 
            "Partial" when connectionStatus.Contains("/") => "🟡",
            _ => "⚪"
        };

        var tooltip = $"Atomos {statusIndicator}";
        
        if (notificationCount > 0)
        {
            tooltip += $" ({notificationCount})";
        }

        return tooltip;
    }

    private NativeMenu CreateMinimalTrayMenu()
    {
        var menu = new NativeMenu();
        var connectionStatus = _trayIconController.GetConnectionStatus();
        var notificationCount = _trayIconController.GetActiveNotificationsCount();
        
        var showMenuItem = new NativeMenuItem("Open Atomos");
        showMenuItem.Click += (sender, args) => _trayIconController.ShowCommand.Execute().Subscribe();
        
        var statusIcon = connectionStatus switch
        {
            "Connected" => "🟢",
            "Disconnected" => "🔴",
            "Partial" when connectionStatus.Contains("/") => "🟡", 
            _ => "⚪"
        };
        
        var statusMenuItem = new NativeMenuItem($"{statusIcon} {connectionStatus}")
        {
            IsEnabled = false
        };
        
        if (notificationCount > 0)
        {
            var notificationMenuItem = new NativeMenuItem($"🔔 {notificationCount} notification{(notificationCount == 1 ? "" : "s")}")
            {
                IsEnabled = false
            };
            menu.Items.Add(notificationMenuItem);
        }
        
        var actionsMenu = new NativeMenuItem("Actions");
        var actionsSubMenu = new NativeMenu();
        
        var checkUpdatesMenuItem = new NativeMenuItem("🔄 Check Updates");
        checkUpdatesMenuItem.Click += (sender, args) => _trayIconController.CheckUpdatesCommand.Execute().Subscribe();

        var refreshPluginsMenuItem = new NativeMenuItem("🔌 Refresh Plugins");
        refreshPluginsMenuItem.Click += (sender, args) => _trayIconController.RefreshPluginsCommand.Execute().Subscribe();

        actionsSubMenu.Items.Add(checkUpdatesMenuItem);
        actionsSubMenu.Items.Add(refreshPluginsMenuItem);
        actionsMenu.Menu = actionsSubMenu;
        
        menu.Items.Add(showMenuItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(statusMenuItem);
        
        if (notificationCount > 0)
        {
            menu.Items.Add(new NativeMenuItemSeparator());
        }
        
        menu.Items.Add(actionsMenu);
        menu.Items.Add(new NativeMenuItemSeparator());
        
        var exitMenuItem = new NativeMenuItem("❌ Exit");
        exitMenuItem.Click += (sender, args) => _trayIconController.ExitCommand.Execute().Subscribe();
        menu.Items.Add(exitMenuItem);

        return menu;
    }
    
    private NativeMenu CreateUltraMinimalTrayMenu()
    {
        var menu = new NativeMenu();

        // Just the essentials
        var showMenuItem = new NativeMenuItem("Open");
        showMenuItem.Click += (sender, args) => _trayIconController.ShowCommand.Execute().Subscribe();

        var refreshMenuItem = new NativeMenuItem("Refresh");
        refreshMenuItem.Click += (sender, args) => _trayIconController.RefreshPluginsCommand.Execute().Subscribe();

        var exitMenuItem = new NativeMenuItem("Exit");
        exitMenuItem.Click += (sender, args) => _trayIconController.ExitCommand.Execute().Subscribe();

        menu.Items.Add(showMenuItem);
        menu.Items.Add(refreshMenuItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(exitMenuItem);

        return menu;
    }

    public void RefreshMenu()
    {
        lock (_lock)
        {
            if (_trayIcon != null && !_disposed)
            {
                if (Dispatcher.UIThread.CheckAccess())
                {
                    _trayIcon.Menu = CreateMinimalTrayMenu();
                    _trayIcon.ToolTipText = GetMinimalTooltipText();
                }
                else
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        _trayIcon.Menu = CreateMinimalTrayMenu();
                        _trayIcon.ToolTipText = GetMinimalTooltipText();
                    });
                }
            }
        }
    }

    public void ShowTrayIcon()
    {
        lock (_lock)
        {
            if (_trayIcon != null && !_disposed)
            {
                if (Dispatcher.UIThread.CheckAccess())
                {
                    _trayIcon.IsVisible = true;
                }
                else
                {
                    Dispatcher.UIThread.Post(() => _trayIcon.IsVisible = true);
                }
            }
        }
    }
    
    public void HideTrayIcon()
    {
        lock (_lock)
        {
            if (_trayIcon != null && !_disposed)
            {
                if (Dispatcher.UIThread.CheckAccess())
                {
                    _trayIcon.IsVisible = false;
                }
                else
                {
                    Dispatcher.UIThread.Post(() => _trayIcon.IsVisible = false);
                }
            }
        }
    }

    private void DisposeTrayIconSync()
    {
        if (_trayIcon != null)
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                _trayIcon.IsVisible = false;
                _trayIcon.Dispose();
            }
            else
            {
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    _trayIcon.IsVisible = false;
                    _trayIcon.Dispose();
                }).Wait();
            }
            
            _trayIcon = null;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (!_disposed)
            {
                DisposeTrayIconSync();
                _disposed = true;
                _isInitialized = false;
            }
        }
    }
}