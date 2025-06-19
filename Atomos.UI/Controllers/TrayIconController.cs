using System.Reactive;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using CommonLib.Interfaces;
using NLog;
using PenumbraModForwarder.UI.Interfaces;
using PenumbraModForwarder.UI.Views;
using ReactiveUI;

namespace PenumbraModForwarder.UI.Controllers;

public class TrayIconController : ITrayIconController
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
    
    private readonly IConfigurationService _configurationService;
    
    public ReactiveCommand<Unit, Unit> ShowCommand { get; }
    public ReactiveCommand<Unit, Unit> ExitCommand { get; }
    

    /// <summary>
    /// Constructor.
    /// </summary>
    public TrayIconController(IConfigurationService configurationService)
    {
        _configurationService = configurationService;
        _logger.Info("Initializing TrayIconController.");
        ShowCommand = ReactiveCommand.Create(ShowMainWindow);
        ExitCommand = ReactiveCommand.Create(ExitApplication);
    }

    /// <summary>
    /// Shows the main window if it doesn't exist, or is hidden/minimized.
    /// </summary>
    public void ShowMainWindow()
    {
        if (App.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _logger.Info("Request to show MainWindow.");

            if (desktop.MainWindow == null)
            {
                _logger.Info("MainWindow is null, creating new instance.");
                desktop.MainWindow = new MainWindow(_configurationService);
            }

            desktop.MainWindow.ShowInTaskbar = true;
            desktop.MainWindow.Show();
            desktop.MainWindow.WindowState = WindowState.Normal;
            desktop.MainWindow.Activate();
            desktop.MainWindow.Focus();
            _logger.Info("MainWindow shown or restored to normal state.");
        }
        else
        {
            _logger.Warn("Could not show MainWindow because ApplicationLifetime is not a IClassicDesktopStyleApplicationLifetime.");
        }
    }

    /// <summary>
    /// Shuts down the application.
    /// </summary>
    private void ExitApplication()
    {
        _logger.Info("Request to exit application.");

        if (App.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _logger.Info("Shutting down application via desktop lifetime.");
            desktop.Shutdown();
        }
        else
        {
            _logger.Warn("Could not shut down application; no classic desktop lifetime found.");
        }
    }
}