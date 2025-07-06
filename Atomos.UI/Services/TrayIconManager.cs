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
                ToolTipText = "Atomos",
                Menu = new NativeMenu()
            };
            
            var showMenuItem = new NativeMenuItem("Show");
            showMenuItem.Click += (sender, args) =>
            {
                _trayIconController.ShowCommand.Execute().Subscribe();
            };
            
            var exitMenuItem = new NativeMenuItem("Exit");
            exitMenuItem.Click += (sender, args) =>
            {
                _trayIconController.ExitCommand.Execute().Subscribe();
            };
            
            _trayIcon.Menu.Items.Add(showMenuItem);
            _trayIcon.Menu.Items.Add(exitMenuItem);
            
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
            // Hide the icon first
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