using Atomos.ConsoleTooling.Interfaces;
using Atomos.ConsoleTooling.Services;
using Atomos.Statistics.Services;
using CommonLib.Consts;
using CommonLib.Extensions;
using CommonLib.Interfaces;
using CommonLib.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Atomos.ConsoleTooling.Extensions;

public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddAutoMapper(cfg =>
        {
            cfg.AddProfile<ConvertConfiguration>();
        });
        
        services.SetupLogging();
        services.AddSingleton<ISoundManagerService, SoundManagerService>();
        services.AddSingleton<IInstallingService, InstallingService>();
        services.AddSingleton<IPenumbraService, PenumbraService>();
        services.AddSingleton<IFileStorage, FileStorage>();
        services.AddSingleton<IConfigurationService, ConfigurationService>();
        services.AddSingleton<IStatisticService, StatisticService>();
        services.AddSingleton<IPenumbraService, PenumbraService>();

        services.AddHttpClient<IModInstallService, ModInstallService>(client =>
        {
            client.BaseAddress = new Uri(ApiConsts.BaseApiUrl);
        });

        return services;
    }
    
    private static void SetupLogging(this IServiceCollection services)
    {
        Logging.ConfigureLogging(services, "ConsoleTool");
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

        MergedSentryLogging.MergeSentryLogging(sentryDsn, "ConsoleTool");
    }
}