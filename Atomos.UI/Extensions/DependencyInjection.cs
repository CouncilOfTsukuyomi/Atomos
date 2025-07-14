using System;
using System.IO;
using System.Threading.Tasks;
using Atomos.Statistics.Services;
using Atomos.UI.Controllers;
using Atomos.UI.Interfaces;
using Atomos.UI.Services;
using Atomos.UI.ViewModels;
using Atomos.UI.Views;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CommonLib.Extensions;
using CommonLib.Interfaces;
using CommonLib.Models;
using CommonLib.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PluginManager.Core.Extensions;
using IRegistryHelper = Atomos.UI.Interfaces.IRegistryHelper;
using RegistryHelper = Atomos.UI.Services.RegistryHelper;

namespace Atomos.UI.Extensions;

public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddAutoMapper(cfg =>
        {
            cfg.AddProfile<ConvertConfiguration>();
        });
        
        services.AddSingleton<ConfigurationModel>();
        
        services.AddSingleton<IUpdateCheckService, UpdateCheckService>();
        services.AddSingleton<ISoundManagerService, SoundManagerService>();
        services.AddSingleton<IAria2Service>(_ => new Aria2Service(AppContext.BaseDirectory));
        services.AddSingleton<IRegistryHelper, RegistryHelper>();
        services.AddSingleton<IFileLinkingService, FileLinkingService>();
        services.AddSingleton<INotificationService, NotificationService>();
        services.AddSingleton<IDownloadManagerService, DownloadManagerService>();
        services.AddSingleton<IWebSocketClient, WebSocketClient>();
        services.AddSingleton<IConfigurationService, ConfigurationService>();
        services.AddSingleton<IXivLauncherService, XivLauncherService>();
        services.AddSingleton<IConfigurationListener, ConfigurationListener>();
        services.AddSingleton<IFileStorage, FileStorage>();
        services.AddSingleton<IStatisticService, StatisticService>();
        services.AddSingleton<IFileSizeService, FileSizeService>();
        services.AddSingleton<IUpdateService, UpdateService>();
        services.AddSingleton<IDownloadUpdater, DownloadUpdater>();
        services.AddSingleton<IRunUpdater, RunUpdater>();
        services.AddSingleton<IPluginDataService, PluginDataService>();
        services.AddSingleton<IFileDialogService>(provider =>
        {
            var applicationLifetime = Application.Current.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            var mainWindow = applicationLifetime?.MainWindow;

            if (mainWindow == null)
            {
                throw new InvalidOperationException("MainWindow is not initialized.");
            }

            return new FileDialogService(mainWindow);
        });
        services.AddSingleton<ITrayIconController, TrayIconController>();
        services.AddSingleton<ITrayIconManager, TrayIconManager>();
        services.AddSingleton<ITaskbarFlashService, TaskbarFlashService>();
        
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<ErrorWindowViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<ModsViewModel>();
        services.AddSingleton<HomeViewModel>();
        services.AddScoped<PluginViewModel>();
        services.AddTransient<PluginDataViewModel>();
        
        services.AddSingleton<MainWindow>();
        services.AddSingleton<ErrorWindowViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<ModsViewModel>();
        services.AddSingleton<HomeViewModel>();
        services.AddScoped<PluginView>();
        
        // Replace the individual plugin service registrations with the comprehensive method
        services.AddPluginServicesWithUpdates(options =>
        {
            options.RegistryUrl = "https://raw.githubusercontent.com/CouncilOfTsukuyomi/StaticResources/refs/heads/main/plugins.json";
            options.PluginBasePath = Path.Combine(AppContext.BaseDirectory, "plugins");
        });
        
        services.SetupLogging();

        return services;
    }
    
    /// <summary>
    /// Initialise application services including early plugin services
    /// Call this method after building the service provider but before showing the main window
    /// </summary>
    public static async Task InitializeApplicationServicesAsync(this IServiceProvider serviceProvider)
    {
        try
        {
            _ = Task.Run(async () =>
            {
                var logger = serviceProvider.GetService<ILogger<Program>>();
                var notificationService = serviceProvider.GetService<INotificationService>();
                
                try
                {
                    logger?.LogInformation("Starting background plugin installation and updates...");
                    
                    if (notificationService != null)
                    {
                        await notificationService.ShowNotification(
                            "Setting Up Plugins", 
                            "Checking for new plugins and updates...", 
                            null, 
                            3);
                    }
                    
                    await serviceProvider.InitializePluginServicesAsync();
                    
                    if (notificationService != null)
                    {
                        await notificationService.ShowNotification(
                            "Plugins Ready", 
                            "All plugins have been installed and updated successfully, refresh to view.", 
                            null, 
                            5);
                    }

                    logger?.LogInformation("Background plugin installation and updates completed successfully");
                }
                catch (Exception ex)
                {
                    logger?.LogError(ex, "Failed to initialize plugin services in background");
                    
                    if (notificationService != null)
                    {
                        await notificationService.ShowErrorNotification(
                            "Plugin Setup Failed", 
                            "Some plugins may not have installed or updated properly. Check logs for details.", 
                            null, 
                            6);
                    }
                }
            });
            
            var logger = serviceProvider.GetService<ILogger<Program>>();
            logger?.LogInformation("Application services initialized. Plugin installation and updates running in background.");
        }
        catch (Exception ex)
        {
            var logger = serviceProvider.GetService<ILogger<Program>>();
            logger?.LogError(ex, "Failed to start background plugin initialization");
        }
    }
    
    private static void SetupLogging(this IServiceCollection services)
    {
        Logging.ConfigureLogging(services, "UI");
    }
    
    public static void EnableSentryLogging()
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets<Program>()
            .AddEnvironmentVariables()
            .Build();

        var sentryDns = configuration["SENTRY_DSN"];
        if (string.IsNullOrWhiteSpace(sentryDns))
        {
            Console.WriteLine("No SENTRY_DSN provided. Skipping Sentry enablement.");
            return;
        }

        MergedSentryLogging.MergeSentryLogging(sentryDns, "UI");
    }

    public static void DisableSentryLogging()
    {
        MergedSentryLogging.DisableSentryLogging();
    }
    
    public static void EnableDebugLogging()
    {
        MergedDebugLogging.EnableDebugLogging();
    }

    public static void DisableDebugLogging()
    {
        MergedDebugLogging.DisableDebugLogging();
    }

}