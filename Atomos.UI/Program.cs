using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Atomos.UI.Extensions;
using Avalonia;
using Avalonia.ReactiveUI;
using Microsoft.Extensions.DependencyInjection;
using NLog;

namespace Atomos.UI;

public class Program
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

    public static IServiceProvider ServiceProvider { get; private set; } = null!;

    [STAThread]
    public static void Main(string[] args)
    {
        bool isNewInstance;
        using (var mutex = new Mutex(true, "Atomos.UI", out isNewInstance))
        {
            if (!isNewInstance)
            {
                Console.WriteLine("Another instance of Atomos.UI is already running. Exiting...");
                return;
            }

#if DEBUG
            if (args.Length == 0)
            {
                args = new string[] { "12345" };
            }
#endif

            try
            {
                _logger.Info("Starting application on platform: {Platform}, Runtime: {Runtime}", 
                    Environment.OSVersion.Platform, 
                    RuntimeInformation.FrameworkDescription);

                var services = new ServiceCollection();
                services.AddApplicationServices();

                ServiceProvider = services.BuildServiceProvider();
                
                BuildAvaloniaApp()
                    .AfterSetup(builder =>
                    {
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await InitializeServicesAsync();
                            }
                            catch (Exception ex)
                            {
                                _logger.Error(ex, "Failed to initialize services after setup");
                            }
                        });
                    })
                    .StartWithClassicDesktopLifetime(args);
            }
            catch (Exception ex)
            {
                _logger.Fatal(ex, "Application failed to start");
                Console.WriteLine($"Application failed to start: {ex.Message}");
                Environment.Exit(1);
            }
        }
    }
    
    private static async Task InitializeServicesAsync()
    {
        try
        {
            _logger.Info("Initializing application services...");
            
            await ServiceProvider.InitializeApplicationServicesAsync();
            
            _logger.Info("Application services initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to initialize application services");
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .UseReactiveUI();
}