using System;
using Atomos.UI.ViewModels;
using Atomos.UI.Views;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CommonLib.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using NLog;

namespace Atomos.UI;

public partial class App : Avalonia.Application
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
    private readonly IServiceProvider _serviceProvider;

    public App()
    {
        try
        {
            _serviceProvider = Program.ServiceProvider;
            AvaloniaXamlLoader.Load(this);
        }
        catch (Exception ex)
        {
            _logger.Fatal(ex, "Failed to initialize ServiceProvider");
            Environment.Exit(1);
        }
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownRequested += OnShutdownRequested;
            
            bool isInitialized = Environment.GetEnvironmentVariable("WATCHDOG_INITIALIZED") == "true";
            _logger.Debug("Application initialized by watchdog: {IsInitialized}", isInitialized);

            var args = Environment.GetCommandLineArgs();
            _logger.Debug("Command-line arguments: {Args}", string.Join(", ", args));

            if (!isInitialized)
            {
                _logger.Warn("Application not initialized by watchdog, showing error window");
                desktop.MainWindow = new ErrorWindow
                {
                    DataContext = ActivatorUtilities.CreateInstance<ErrorWindowViewModel>(_serviceProvider)
                };
            }
            else
            {
                _logger.Debug("Showing main window");

                if (args.Length < 2)
                {
                    _logger.Fatal("No port specified for the UI.");
                    Environment.Exit(1);
                }

                int port = int.Parse(args[1]);
                _logger.Info("Listening on port {Port}", port);

                try
                {
                    var mainWindow = ActivatorUtilities.CreateInstance<MainWindow>(_serviceProvider);
                    desktop.MainWindow = mainWindow;
                    var mainViewModel = ActivatorUtilities.CreateInstance<MainWindowViewModel>(_serviceProvider, port);
                    mainWindow.DataContext = mainViewModel;
                    _logger.Info("Main window and view model created successfully");
                }
                catch (Exception ex)
                {
                    _logger.Fatal(ex, "Failed to create main window or view model");
                    Environment.Exit(1);
                }
            }
        }
        
        base.OnFrameworkInitializationCompleted();
    }

    private void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        _logger.Info("Application shutdown requested - flushing configuration changes...");
        
        try
        {
            var configService = _serviceProvider.GetService<IConfigurationService>();
            if (configService != null)
            {
                _logger.Info("Flushing pending configuration changes before UI shutdown...");
                configService.FlushPendingChangesSync();
                _logger.Info("Configuration flush completed successfully");
            }
            else
            {
                _logger.Warn("Configuration service not available during shutdown");
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error flushing configuration during UI shutdown");
        }
    }
}