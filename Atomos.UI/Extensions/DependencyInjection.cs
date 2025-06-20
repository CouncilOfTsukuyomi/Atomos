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
using CommonLib.Factory;
using CommonLib.Interfaces;
using CommonLib.Models;
using CommonLib.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
        
        // Register ConfigurationModel as a singleton
        services.AddSingleton<ConfigurationModel>();

        // Services
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
        services.AddSingleton<IXmaHttpClientFactory, XmaHttpClientFactory>();
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
        services.AddSingleton<IXmaModDisplay, XmaModDisplay>();
        services.AddSingleton<ITrayIconController, TrayIconController>();
        services.AddSingleton<ITrayIconManager, TrayIconManager>();
        services.AddSingleton<ITaskbarFlashService, TaskbarFlashService>();

        // ViewModels
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<ErrorWindowViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<ModsViewModel>();
        services.AddSingleton<HomeViewModel>();
        services.AddSingleton<PluginsViewModel>();
        services.AddTransient<PluginDataViewModel>();

        // Views
        services.AddSingleton<MainWindow>();
        services.AddSingleton<ErrorWindowViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<ModsViewModel>();
        services.AddSingleton<HomeViewModel>();
        services.AddSingleton<PluginsView>();
        
        services.AddPluginServices();
        
        services.SetupLogging();

        return services;
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

        var sentryDns = configuration["SENTRY_DNS"];
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
}