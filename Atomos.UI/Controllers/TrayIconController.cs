using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reflection;
using System.Threading.Tasks;
using Atomos.UI.Extensions;
using Atomos.UI.Interfaces;
using Atomos.UI.Services;
using Atomos.UI.ViewModels;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CommonLib.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using NLog;
using PluginManager.Core.Extensions;
using ReactiveUI;

namespace Atomos.UI.Controllers
{
    public class TrayIconController : ITrayIconController
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        private readonly IServiceProvider _serviceProvider;
        private readonly INotificationService _notificationService;
        private readonly WebSocketClient _webSocketClient;
        private readonly IUpdateCheckService _updateCheckService;

        public ReactiveCommand<Unit, Unit> ShowCommand { get; }
        public ReactiveCommand<Unit, Unit> ExitCommand { get; }
        public ReactiveCommand<Unit, Unit> CheckUpdatesCommand { get; }
        public ReactiveCommand<Unit, Unit> RefreshPluginsCommand { get; }

        public TrayIconController(
            IServiceProvider serviceProvider,
            INotificationService notificationService,
            IWebSocketClient webSocketClient,
            IUpdateCheckService updateCheckService)
        {
            _serviceProvider = serviceProvider;
            _notificationService = notificationService;
            _webSocketClient = webSocketClient as WebSocketClient;
            _updateCheckService = updateCheckService;

            ShowCommand = ReactiveCommand.CreateFromTask(ShowWindow);
            ExitCommand = ReactiveCommand.CreateFromTask(ExitApplication);
            CheckUpdatesCommand = ReactiveCommand.CreateFromTask(CheckForUpdates);
            RefreshPluginsCommand = ReactiveCommand.CreateFromTask(RefreshPlugins);
        }

        private async Task ShowWindow()
        {
            try
            {
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    var mainWindow = desktop.MainWindow;
                    if (mainWindow != null)
                    {
                        mainWindow.ShowInTaskbar = true;
                        mainWindow.Show();
                        mainWindow.WindowState = Avalonia.Controls.WindowState.Normal;
                        mainWindow.Activate();
                        mainWindow.Topmost = true;
                        mainWindow.Topmost = false;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to show main window");
            }
        }

        private async Task ExitApplication()
        {
            try
            {
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    desktop.Shutdown();
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to exit application");
            }
        }

        private async Task CheckForUpdates()
        {
            try
            {
                await _notificationService.ShowNotification(
                    "Update Check", 
                    "Checking for updates...");

                var hasUpdate = await _updateCheckService.CheckForUpdatesAsync();
                
                if (!hasUpdate)
                {
                    await _notificationService.ShowNotification(
                        "Update Check", 
                        "You are running the latest version!");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to check for updates");
                await _notificationService.ShowErrorNotification(
                    "Update Check Failed", 
                    "Could not check for updates");
            }
        }

        private async Task RefreshPlugins()
        {
            try
            {
                _logger.Info("Starting plugin refresh from TrayIconController");
                
                await _notificationService.ShowNotification(
                    "Plugins", 
                    "Refreshing plugins...");
                
                await _serviceProvider.InitializePluginServicesAsync();
                
                var pluginViewModel = _serviceProvider.GetService<PluginViewModel>();
                if (pluginViewModel != null)
                {
                    _logger.Debug("Found PluginViewModel, calling RefreshAsync");
                    await pluginViewModel.RefreshAsync();
                }
                else
                {
                    _logger.Warn("PluginViewModel not found in service provider");
                }

                await _notificationService.ShowNotification(
                    "Plugins", 
                    "Plugins refreshed successfully!");
                    
                _logger.Info("Plugin refresh completed successfully");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to refresh plugins");
                await _notificationService.ShowErrorNotification(
                    "Plugin Refresh Failed", 
                    "Could not refresh plugins");
            }
        }

        public string GetConnectionStatus()
        {
            try
            {
                if (_webSocketClient == null)
                {
                    _logger.Debug("WebSocketClient is null");
                    return "Unknown";
                }
                
                return _webSocketClient.GetConnectionStatus();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error checking connection status");
                return "Unknown";
            }
        }

        public string GetVersionInfo()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var version = assembly.GetName().Version;
            return version == null ? "Local Build" : $"v{version.Major}.{version.Minor}.{version.Build}";
        }

        public int GetActiveNotificationsCount()
        {
            if (_notificationService is NotificationService notService)
            {
                return notService.Notifications.Count;
            }
            return 0;
        }
    }
}