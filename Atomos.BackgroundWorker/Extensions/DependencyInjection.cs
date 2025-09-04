using Atomos.BackgroundWorker.Interfaces;
using Atomos.BackgroundWorker.Services;
using CommonLib.Consts;
using CommonLib.Extensions;
using CommonLib.Interfaces;
using CommonLib.Services;
using Atomos.FileMonitor.Interfaces;
using Atomos.FileMonitor.Services;
using Atomos.Statistics.Services;

namespace Atomos.BackgroundWorker.Extensions;

public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services, int port)
    {
        services.AddAutoMapper(cfg =>
        {
            cfg.AddProfile<ConvertConfiguration>();
        });
            
        services.SetupLogging();
            
        services.AddHostedService(provider => new Worker(
            provider.GetRequiredService<IWebSocketServer>(),
            provider.GetRequiredService<IStartupService>(),
            provider.GetRequiredService<IConfigurationService>(),
            port,
            provider.GetRequiredService<IHostApplicationLifetime>()
        ));
            
        services.AddSingleton<IWebSocketServer, WebSocketServer>();
        services.AddSingleton<IStartupService, StartupService>();
        services.AddSingleton<IFileWatcherService, FileWatcherService>();
        services.AddSingleton<ITexToolsHelper, TexToolsHelper>();
        services.AddSingleton<IRegistryHelper, RegistryHelper>();
        services.AddSingleton<IConfigurationService, ConfigurationService>();
        services.AddSingleton<IFileStorage, FileStorage>();
        services.AddSingleton<IFileSystemHelper, FileSystemHelper>();
        services.AddSingleton<IModHandlerService, ModHandlerService>();
        services.AddSingleton<IStatisticService, StatisticService>();
        services.AddSingleton<IPenumbraService, PenumbraService>();
        services.AddSingleton<IConfigurationListener, ConfigurationListener>();
        services.AddTransient<IFileWatcher, FileWatcher>();
        services.AddSingleton<IFileQueueProcessor, FileQueueProcessor>();
        services.AddSingleton<IFileProcessor, FileProcessor>();
        services.AddSingleton<IMemoryMetricsService, MemoryMetricsService>();
            
        services.AddHttpClient<IModInstallService, ModInstallService>((serviceProvider, client) =>
        {
            client.BaseAddress = new Uri(ApiConsts.BaseApiUrl);
    
            // Configure timeout using the PenumbraTimeOutInSeconds setting
            var configService = serviceProvider.GetRequiredService<IConfigurationService>();
            var timeoutSeconds = (int)configService.ReturnConfigValue(c => c.AdvancedOptions.PenumbraTimeOutInSeconds);
            client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
        });
            
        return services;
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
            
        MergedSentryLogging.MergeSentryLogging(sentryDns, "BackgroundWorker");
    }
        
    public static void DisableSentryLogging()
    {
        MergedSentryLogging.DisableSentryLogging();
    }

    private static void SetupLogging(this IServiceCollection services)
    {
        Logging.ConfigureLogging(services, "BackgroundWorker");
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