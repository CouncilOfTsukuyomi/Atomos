﻿using Atomos.Watchdog.Interfaces;
using Atomos.Watchdog.Services;
using CommonLib.Extensions;
using CommonLib.Interfaces;
using CommonLib.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using DownloadUpdater = CommonLib.Services.DownloadUpdater;

namespace Atomos.Watchdog.Extensions;

public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddAutoMapper(cfg =>
        {
            cfg.AddProfile<ConvertConfiguration>();
        });
        
        services.SetupLogging();
        services.AddSingleton<IConfigurationService, ConfigurationService>();
        services.AddSingleton<IUpdateService, UpdateService>();
        services.AddSingleton<IConfigurationSetup, ConfigurationSetup>();
        services.AddSingleton<IProcessManager, ProcessManager>();
        services.AddSingleton<IFileStorage, FileStorage>();
        services.AddSingleton<IAria2Service>(_ => new Aria2Service(AppContext.BaseDirectory));
        services.AddSingleton<IDownloadUpdater, DownloadUpdater>();

        return services;
    }
    
    private static void SetupLogging(this IServiceCollection services)
    {
        Logging.ConfigureLogging(services, "Launcher");
    }
    

    public static void EnableSentryLogging()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Production.json", optional: true)
            .AddUserSecrets<Program>()
            .AddEnvironmentVariables()
            .Build();

        var sentryDsn = configuration["SENTRY_DSN"];
        if (string.IsNullOrWhiteSpace(sentryDsn))
        {
            Console.WriteLine("No SENTRY_DSN provided. Skipping Sentry enablement.");
            return;
        }

        MergedSentryLogging.MergeSentryLogging(sentryDsn, "Launcher");
    }
    
    public static void DisableSentryLogging()
    {
        MergedSentryLogging.DisableSentryLogging();
    }
}