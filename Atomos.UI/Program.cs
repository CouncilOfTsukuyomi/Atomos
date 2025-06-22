using System;
using System.Collections.Generic;
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
            
            // Auto-install and update all default plugins
            await AutoInstallAndUpdateDefaultPluginsAsync();
            
            _logger.Info("Application services initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to initialize application services");
        }
    }

    private static async Task AutoInstallAndUpdateDefaultPluginsAsync()
    {
        try
        {
            _logger.Info("Checking for plugins to install and update...");
            
            var pluginManagementService = ServiceProvider.GetRequiredService<IPluginManagementService>();
            var defaultPluginRegistryService = ServiceProvider.GetRequiredService<IDefaultPluginRegistryService>();
            var pluginDownloader = ServiceProvider.GetRequiredService<IPluginDownloader>();
            
            // Get all currently available (installed/discovered) plugins
            var availablePlugins = await pluginManagementService.GetAvailablePluginsAsync();
            var installedPluginDict = availablePlugins.ToDictionary(p => p.PluginId, StringComparer.OrdinalIgnoreCase);
            
            // Get all plugins from the registry
            var registryPlugins = await defaultPluginRegistryService.GetAvailablePluginsAsync();
            
            var installResults = new List<(string PluginName, bool Success, string Message)>();

            foreach (var registryPlugin in registryPlugins)
            {
                try
                {
                    bool needsAction = false;
                    string action = "";

                    if (!installedPluginDict.TryGetValue(registryPlugin.Id, out var installedPlugin))
                    {
                        // Plugin not installed - need to install
                        needsAction = true;
                        action = "install";
                    }
                    else if (IsUpdateNeeded(installedPlugin.Version, registryPlugin.Version))
                    {
                        // Plugin needs update
                        needsAction = true;
                        action = "update";
                        _logger.Info("Plugin update available: {PluginName} {CurrentVersion} -> {NewVersion}", 
                            registryPlugin.Name, installedPlugin.Version, registryPlugin.Version);
                    }

                    if (needsAction)
                    {
                        _logger.Info("Starting {Action} for plugin: {PluginName} (v{Version})", 
                            action, registryPlugin.Name, registryPlugin.Version);

                        // Unload existing plugin if updating
                        if (action == "update" && installedPlugin != null)
                        {
                            await pluginManagementService.SetPluginEnabledAsync(installedPlugin.PluginId, false);
                            _logger.Info("Disabled old version of plugin: {PluginName}", registryPlugin.Name);
                        }

                        // Download and install the plugin
                        var installResult = await pluginDownloader.DownloadAndInstallAsync(registryPlugin);
                        
                        if (installResult.Success)
                        {
                            _logger.Info("Successfully {Action}ed plugin: {PluginName} to {InstalledPath}", 
                                action, installResult.PluginName, installResult.InstalledPath);
                            installResults.Add((registryPlugin.Name, true, $"Successfully {action}ed"));
                        }
                        else
                        {
                            _logger.Error("Failed to {Action} plugin {PluginName}: {Error}", 
                                action, installResult.PluginName, installResult.ErrorMessage);
                            installResults.Add((registryPlugin.Name, false, installResult.ErrorMessage ?? $"Failed to {action}"));
                        }
                    }
                    else
                    {
                        _logger.Debug("Plugin {PluginName} is up to date (v{Version})", 
                            registryPlugin.Name, installedPlugin?.Version ?? "unknown");
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to process plugin: {PluginName} ({PluginId})", 
                        registryPlugin.Name, registryPlugin.Id);
                    installResults.Add((registryPlugin.Name, false, ex.Message));
                }
            }

            // Re-initialise plugins after installation/updates to discover changes
            if (installResults.Any(r => r.Success))
            {
                _logger.Info("Re-initializing plugin services after updates...");
                await ServiceProvider.InitializePluginServicesAsync();
            }
            
            // Log summary
            var successCount = installResults.Count(r => r.Success);
            var totalCount = installResults.Count;
            
            if (totalCount > 0)
            {
                _logger.Info("Plugin update summary: {SuccessCount}/{TotalCount} operations completed successfully", 
                    successCount, totalCount);
                
                foreach (var result in installResults.Where(r => !r.Success))
                {
                    _logger.Warn("Plugin operation failed: {PluginName} - {Message}", result.PluginName, result.Message);
                }
            }
            else
            {
                _logger.Info("All plugins are up to date");
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to auto-install and update default plugins");
        }
    }


    /// <summary>
    /// Compares version strings to determine if an update is needed
    /// </summary>
    private static bool IsUpdateNeeded(string currentVersion, string registryVersion)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(currentVersion) || string.IsNullOrWhiteSpace(registryVersion))
                return !string.IsNullOrWhiteSpace(registryVersion);
            
            var cleanCurrentVersion = CleanVersionString(currentVersion);
            var cleanRegistryVersion = CleanVersionString(registryVersion);
            
            if (Version.TryParse(cleanCurrentVersion, out var current) && 
                Version.TryParse(cleanRegistryVersion, out var registry))
            {
                return registry > current;
            }
            
            return !string.Equals(cleanCurrentVersion, cleanRegistryVersion, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Error comparing versions: current={CurrentVersion}, registry={RegistryVersion}", 
                currentVersion, registryVersion);
            return false; 
        }
    }

    /// <summary>
    /// Cleans version strings by removing common prefixes
    /// </summary>
    private static string CleanVersionString(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return version;
        
        var cleaned = version.Trim();
        if (cleaned.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            cleaned = cleaned.Substring(1);
        }

        return cleaned;
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .UseReactiveUI();
}