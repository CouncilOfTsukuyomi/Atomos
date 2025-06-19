using Atomos.ConsoleTooling.Extensions;
using Atomos.ConsoleTooling.Interfaces;
using CommonLib.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Atomos.ConsoleTooling;

public class Program
{
    public static void Main(string[] args)
    {
        using var mutex = new Mutex(true, "Atomos.ConsoleTooling", out var isNewInstance);
        if (!isNewInstance)
        {
            Console.WriteLine("Another instance is already running. Exiting...");
            return;
        }
        
        var services = new ServiceCollection();
        services.AddApplicationServices();

        // Build the service provider
        using var serviceProvider = services.BuildServiceProvider();

        if (args.Length > 0)
        {
            var filePath = args[0];
            var installingService = serviceProvider.GetRequiredService<IInstallingService>();
            var configService = serviceProvider.GetRequiredService<IConfigurationService>();

            if ((bool) configService.ReturnConfigValue(c => c.Common.EnableSentry))
            {
                DependencyInjection.EnableSentryLogging();
            }
            installingService.HandleFileAsync(filePath).GetAwaiter().GetResult();
        }
        else
        {
            Console.WriteLine("No file path was provided via the command line arguments.");
        }
    }
}