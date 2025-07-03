using System;
using System.IO;
using System.Threading.Tasks;
using Atomos.UI.Interfaces;
using Avalonia.Controls;
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
            
            DisposeTrayIcon();

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
            
            // Add a small delay before making the icon visible
            // This helps prevent timing issues with Windows Shell
            Task.Delay(100).ContinueWith(_ =>
            {
                if (_trayIcon != null && !_disposed)
                {
                    _trayIcon.IsVisible = true;
                }
            });
            
            _isInitialized = true;
        }
    }
    
    public void ShowTrayIcon()
    {
        lock (_lock)
        {
            if (_trayIcon != null && !_disposed)
            {
                _trayIcon.IsVisible = true;
            }
        }
    }
    
    public void HideTrayIcon()
    {
        lock (_lock)
        {
            if (_trayIcon != null && !_disposed)
            {
                _trayIcon.IsVisible = false;
            }
        }
    }

    private void DisposeTrayIcon()
    {
        if (_trayIcon != null)
        {
            _trayIcon.IsVisible = false;
            Task.Delay(50).ContinueWith(_ =>
            {
                _trayIcon?.Dispose();
            });
            
            _trayIcon = null;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (!_disposed)
            {
                DisposeTrayIcon();
                _disposed = true;
                _isInitialized = false;
            }
        }
    }
}