
using System;
using System.IO;
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

    public TrayIconManager(ITrayIconController trayIconController)
    {
        _trayIconController = trayIconController;
    }

    public void InitializeTrayIcon()
    {
        if (_isInitialized)
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
            ToolTipText = "Mod Forwarder",
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
        
        _trayIcon.IsVisible = true;
        _isInitialized = true;
    }
    
    public void ShowTrayIcon()
    {
        if (_trayIcon != null)
        {
            _trayIcon.IsVisible = true;
        }
    }
    
    public void HideTrayIcon()
    {
        if (_trayIcon != null)
        {
            _trayIcon.IsVisible = false;
        }
    }

    private void DisposeTrayIcon()
    {
        if (_trayIcon != null)
        {
            _trayIcon.IsVisible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            DisposeTrayIcon();
            _disposed = true;
            _isInitialized = false;
        }
    }
}