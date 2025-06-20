
using System;
using System.IO;
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
            // In debug mode, append a default port if none is provided
            if (args.Length == 0)
            {
                args = new string[] { "12345" }; // Default port for debugging
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
            
            // Initialize plugin services first
            await ServiceProvider.InitializePluginServicesAsync();
            
            // Auto-install all default plugins
            await AutoInstallDefaultPluginsAsync();
            
            _logger.Info("Application services initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to initialize application services");
        }
    }

    private static async Task AutoInstallDefaultPluginsAsync()
    {
        try
        {
            _logger.Info("Checking for default plugins to install...");
            
            var pluginManagementService = ServiceProvider.GetRequiredService<IPluginManagementService>();
            var defaultPluginRegistryService = ServiceProvider.GetRequiredService<IDefaultPluginRegistryService>();
            var pluginDownloader = ServiceProvider.GetRequiredService<IPluginDownloader>();
            
            // Get all currently available (installed/discovered) plugins
            var availablePlugins = await pluginManagementService.GetAvailablePluginsAsync();
            var installedPluginIds = availablePlugins.Select(p => p.PluginId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            
            // Get all plugins from the registry
            var registryPlugins = await defaultPluginRegistryService.GetAvailablePluginsAsync();
            
            // Find plugins that need to be downloaded and installed
            var pluginsToInstall = registryPlugins.Where(p => !installedPluginIds.Contains(p.Id)).ToList();
            
            if (pluginsToInstall.Any())
            {
                _logger.Info("Found {Count} plugins to install: {PluginNames}", 
                    pluginsToInstall.Count, 
                    string.Join(", ", pluginsToInstall.Select(p => p.Name)));
                
                foreach (var plugin in pluginsToInstall)
                {
                    try
                    {
                        // Use the new DownloadAndInstallAsync method
                        var installResult = await pluginDownloader.DownloadAndInstallAsync(plugin);
                        
                        if (installResult.Success)
                        {
                            _logger.Info("Successfully installed plugin: {PluginName} to {InstalledPath}", 
                                installResult.PluginName, installResult.InstalledPath);
                        }
                        else
                        {
                            _logger.Error("Failed to install plugin {PluginName}: {Error}", 
                                installResult.PluginName, installResult.ErrorMessage);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Failed to install plugin: {PluginName} ({PluginId})", 
                            plugin.Name, plugin.Id);
                    }
                }
                
                // Re-initialize plugins after installation to discover the new ones
                await ServiceProvider.InitializePluginServicesAsync();
                
                _logger.Info("Completed plugin installation process");
            }
            else
            {
                _logger.Info("All plugins from registry are already installed");
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to auto-install default plugins");
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .UseReactiveUI();
}