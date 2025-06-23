
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Atomos.UI.Extensions;
using Avalonia;
using Avalonia.ReactiveUI;
using Microsoft.Extensions.DependencyInjection;
using NLog;
using PluginManager.Core.Extensions;
using PluginManager.Core.Interfaces;
using PluginManager.Core.Models;
using PluginManager.Core.Services;

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
                var services = new ServiceCollection();
                services.AddApplicationServices();

                ServiceProvider = services.BuildServiceProvider();

                InitializeServicesAsync().GetAwaiter().GetResult();

                BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            }
            catch (Exception ex)
            {
                _logger.Fatal(ex, "Application failed to start");
                Environment.Exit(1);
            }
        }
    }
    
    private static async Task InitializeServicesAsync()
    {
        try
        {
            _logger.Info("Initializing application services...");
            
            _logger.Info("Initializing plugin services...");
            await ServiceProvider.InitializePluginServicesAsync();
            _logger.Info("Plugin services initialized successfully");
            
            _logger.Info("Starting auto-install process...");
            await AutoInstallMissingPluginsAsync();
            _logger.Info("Auto-install process completed");
            
            _logger.Info("Application services initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to initialize application services");
            throw; // Re-throw to prevent the app from continuing in a bad state
        }
    }

    private static async Task AutoInstallMissingPluginsAsync()
    {
        try
        {
            _logger.Info("Checking for new plugins to install...");
            
            _logger.Info("Getting plugin management service...");
            var pluginManagementService = ServiceProvider.GetRequiredService<IPluginManagementService>();
            
            _logger.Info("Getting default plugin registry service...");
            var defaultPluginRegistryService = ServiceProvider.GetRequiredService<IDefaultPluginRegistryService>();
            
            _logger.Info("Getting plugin downloader...");
            var pluginDownloader = ServiceProvider.GetRequiredService<IPluginDownloader>();
            
            _logger.Info("Getting available plugins...");
            var availablePlugins = await pluginManagementService.GetAvailablePluginsAsync();
            _logger.Info("Found {Count} available plugins", availablePlugins.Count());
            
            var installedPluginIds = availablePlugins.Select(p => p.PluginId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            
            _logger.Info("Getting registry plugins...");
            var registryPlugins = await defaultPluginRegistryService.GetAvailablePluginsAsync();
            _logger.Info("Found {Count} registry plugins", registryPlugins.Count());
            
            foreach (var registryPlugin in registryPlugins)
            {
                if (!installedPluginIds.Contains(registryPlugin.Id))
                {
                    _logger.Info("Installing new plugin: {PluginName} (v{Version})", 
                        registryPlugin.Name, registryPlugin.Version);

                    var installResult = await pluginDownloader.DownloadAndInstallAsync(registryPlugin);
                    
                    if (installResult.Success)
                    {
                        _logger.Info("Successfully installed plugin: {PluginName}", installResult.PluginName);
                    }
                    else
                    {
                        _logger.Error("Failed to install plugin {PluginName}: {Error}", 
                            installResult.PluginName, installResult.ErrorMessage);
                    }
                }
            }
            
            _logger.Info("New plugin installation check completed");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to auto-install new plugins");
            // Don't rethrow here - let the app continue even if plugin auto-install fails
        }
    }


    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .UseReactiveUI();
}