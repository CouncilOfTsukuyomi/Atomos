
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using DynamicData;
using DynamicData.Binding;
using NLog;
using PluginManager.Core.Interfaces;
using PluginManager.Core.Models;
using ReactiveUI;

namespace Atomos.UI.ViewModels;

public class PluginViewModel : ViewModelBase, IDisposable
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

    private readonly IPluginManagementService _pluginManagementService;
    private readonly IPluginDiscoveryService _pluginDiscoveryService;
    private readonly CompositeDisposable _disposables = new();
    
    private readonly SourceList<PluginInfo> _availablePluginsSource = new();
    private readonly ReadOnlyObservableCollection<PluginInfo> _filteredPlugins;

    private string _searchTerm = string.Empty;
    public string SearchTerm
    {
        get => _searchTerm;
        set => this.RaiseAndSetIfChanged(ref _searchTerm, value);
    }

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set => this.RaiseAndSetIfChanged(ref _isLoading, value);
    }

    /// <summary>
    /// Filtered plugins based on SearchTerm.
    /// </summary>
    public ReadOnlyObservableCollection<PluginInfo> FilteredPlugins => _filteredPlugins;
    
    public ReactiveCommand<PluginInfo, Unit> TogglePluginCommand { get; }
    public ReactiveCommand<PluginInfo, Unit> OpenSettingsCommand { get; }
    public ReactiveCommand<PluginInfo, Unit> ValidateSettingsCommand { get; }
    public ReactiveCommand<PluginInfo, Unit> RollbackSettingsCommand { get; }
    public ReactiveCommand<Unit, Unit> RefreshCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenPluginDirectoryCommand { get; }
    
    public event Action<PluginSettingsViewModel>? PluginSettingsRequested;

    public PluginViewModel(
        IPluginManagementService pluginManagementService,
        IPluginDiscoveryService pluginDiscoveryService)
    {
        _pluginManagementService = pluginManagementService;
        _pluginDiscoveryService = pluginDiscoveryService;

        // Create reactive filtering
        var searchFilter = this.WhenAnyValue(x => x.SearchTerm)
            .Throttle(TimeSpan.FromMilliseconds(300))
            .Select(CreateSearchPredicate);

        _availablePluginsSource
            .Connect()
            .Filter(searchFilter)
            .Sort(SortExpressionComparer<PluginInfo>.Ascending(p => p.DisplayName))
            .ObserveOn(RxApp.MainThreadScheduler)
            .Bind(out _filteredPlugins)
            .Subscribe()
            .DisposeWith(_disposables);

        // Commands
        TogglePluginCommand = ReactiveCommand.CreateFromTask<PluginInfo>(TogglePluginAsync);
        OpenSettingsCommand = ReactiveCommand.CreateFromTask<PluginInfo>(OpenPluginSettingsAsync);
        ValidateSettingsCommand = ReactiveCommand.CreateFromTask<PluginInfo>(ValidatePluginSettingsAsync);
        RollbackSettingsCommand = ReactiveCommand.CreateFromTask<PluginInfo>(RollbackPluginSettingsAsync);
        RefreshCommand = ReactiveCommand.CreateFromTask(RefreshAsync);
        OpenPluginDirectoryCommand = ReactiveCommand.Create(OpenPluginDirectory);

        // Load plugins immediately, then refresh periodically
        _ = LoadAvailablePluginsAsync();
        
        Observable.Timer(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30))
            .SelectMany(_ => Observable.FromAsync(LoadAvailablePluginsAsync))
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe()
            .DisposeWith(_disposables);
    }

    private Func<PluginInfo, bool> CreateSearchPredicate(string searchTerm)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
            return _ => true;

        var filter = searchTerm.Trim();
        return plugin =>
            (plugin.DisplayName?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false) ||
            (plugin.PluginId?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false) ||
            (plugin.Description?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false) ||
            (plugin.Author?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    /// <summary>
    /// Loads available plugins from the service and updates local collections.
    /// </summary>
    private async Task LoadAvailablePluginsAsync()
    {
        try
        {
            IsLoading = true;
            var fetchedPlugins = await _pluginManagementService.GetAvailablePluginsAsync();

            _logger.Debug("Fetched {Count} plugins from management service", fetchedPlugins.Count);

            // Use DynamicData to efficiently update the collection
            _availablePluginsSource.Edit(updater =>
            {
                updater.Clear();
                updater.AddRange(fetchedPlugins);
            });

            _logger.Debug("Updated plugin source with {Count} plugins", _availablePluginsSource.Count);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load available plugins in PluginsViewModel");
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Toggles the enabled state of a plugin
    /// </summary>
    private async Task TogglePluginAsync(PluginInfo plugin)
    {
        try
        {
            var desiredEnabledState = !plugin.IsEnabled;

            _logger.Info("User toggling plugin {PluginId} from {CurrentState} to {DesiredState}", 
                plugin.PluginId, plugin.IsEnabled, desiredEnabledState);

            await _pluginManagementService.SetPluginEnabledAsync(plugin.PluginId, desiredEnabledState);

            // Refresh to get updated status
            await LoadAvailablePluginsAsync();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to toggle plugin {PluginId}", plugin.PluginId);
        }
    }

    /// <summary>
    /// Opens plugin settings dialog
    /// </summary>
    private async Task OpenPluginSettingsAsync(PluginInfo plugin)
    {
        try
        {
            _logger.Info("Opening settings for plugin {PluginId}", plugin.PluginId);

            // Check if plugin has configurable settings
            var hasConfigurableSettings = await _pluginDiscoveryService.HasConfigurableSettingsAsync(plugin.PluginDirectory);
            
            if (!hasConfigurableSettings)
            {
                _logger.Info("Plugin {PluginId} has no configurable settings", plugin.PluginId);
                return;
            }

            // Create and show the settings view model
            var settingsViewModel = new PluginSettingsViewModel(plugin, _pluginDiscoveryService);
            PluginSettingsRequested?.Invoke(settingsViewModel);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to open settings for plugin {PluginId}", plugin.PluginId);
        }
    }

    /// <summary>
    /// Validates plugin settings schema
    /// </summary>
    private async Task ValidatePluginSettingsAsync(PluginInfo plugin)
    {
        try
        {
            _logger.Info("Validating settings for plugin {PluginId}", plugin.PluginId);

            // Check if plugin has configurable settings first
            var hasConfigurableSettings = await _pluginDiscoveryService.HasConfigurableSettingsAsync(plugin.PluginDirectory);
            
            if (!hasConfigurableSettings)
            {
                return;
            }

            var isValid = await _pluginDiscoveryService.ValidateSettingsSchemaAsync(plugin.PluginDirectory);
            
            if (isValid)
            {
                _logger.Info("Settings validation passed for plugin {PluginId}", plugin.PluginId);
            }
            else
            {
                _logger.Warn("Settings validation failed for plugin {PluginId}", plugin.PluginId);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to validate settings for plugin {PluginId}", plugin.PluginId);
        }
    }

    /// <summary>
    /// Rolls back plugin settings to previous version
    /// </summary>
    private async Task RollbackPluginSettingsAsync(PluginInfo plugin)
    {
        try
        {
            _logger.Info("Rolling back settings for plugin {PluginId}", plugin.PluginId);

            // Check if plugin has configurable settings first
            var hasConfigurableSettings = await _pluginDiscoveryService.HasConfigurableSettingsAsync(plugin.PluginDirectory);
            
            if (!hasConfigurableSettings)
            {
                return;
            }

            var success = await _pluginDiscoveryService.RollbackSettingsAsync(plugin.PluginDirectory);
            
            if (success)
            {
                _logger.Info("Settings rollback successful for plugin {PluginId}", plugin.PluginId);
                    
                // Refresh plugin list to reflect any changes
                await LoadAvailablePluginsAsync();
            }
            else
            {
                _logger.Warn("Settings rollback failed for plugin {PluginId} - no previous configuration available", plugin.PluginId);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to rollback settings for plugin {PluginId}", plugin.PluginId);
        }
    }

    /// <summary>
    /// Gets migration status information for a plugin
    /// </summary>
    public async Task<string> GetPluginMigrationStatusAsync(PluginInfo plugin)
    {
        try
        {
            var settings = await _pluginDiscoveryService.GetPluginSettingsAsync(plugin.PluginDirectory);
            
            if (settings.PreviousConfiguration != null)
            {
                var migrationDate = settings.Metadata.TryGetValue("MigratedAt", out var dateStr) 
                    ? dateStr.ToString() 
                    : "Unknown";
                var migrationFrom = settings.Metadata.TryGetValue("MigratedFrom", out var fromStr) 
                    ? fromStr.ToString() 
                    : "Unknown";
                    
                return $"Migrated from version {migrationFrom} on {migrationDate}";
            }
            
            if (settings.Metadata.TryGetValue("RolledBackAt", out var rollbackDate))
            {
                return $"Rolled back on {rollbackDate}";
            }
            
            return $"Current version: {settings.Version}";
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to get migration status for plugin {PluginId}", plugin.PluginId);
            return "Status unavailable";
        }
    }

    /// <summary>
    /// Checks if a plugin has previous configuration available for rollback
    /// </summary>
    public async Task<bool> CanRollbackPluginAsync(PluginInfo plugin)
    {
        try
        {
            var settings = await _pluginDiscoveryService.GetPluginSettingsAsync(plugin.PluginDirectory);
            return settings.PreviousConfiguration != null;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to check rollback availability for plugin {PluginId}", plugin.PluginId);
            return false;
        }
    }

    /// <summary>
    /// Opens the plugin directory in the system file explorer
    /// </summary>
    private void OpenPluginDirectory()
    {
        try
        {
            var pluginDirectory = GetPluginDirectoryPath();
            
            if (!Directory.Exists(pluginDirectory))
            {
                _logger.Warn("Plugin directory does not exist, creating: {Directory}", pluginDirectory);
                Directory.CreateDirectory(pluginDirectory);
            }

            OpenDirectoryInExplorer(pluginDirectory);
            _logger.Info("Opened plugin directory: {Directory}", pluginDirectory);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to open plugin directory");
        }
    }

    private void OpenDirectoryInExplorer(string directory)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{directory}\"",
                UseShellExecute = true
            });
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "xdg-open",
                Arguments = $"\"{directory}\"",
                UseShellExecute = true
            });
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "open",
                Arguments = $"\"{directory}\"",
                UseShellExecute = true
            });
        }
    }

    private string GetPluginDirectoryPath()
    {
        try
        {
            var baseDirectory = AppContext.BaseDirectory;
            var pluginsDirectory = Path.Combine(baseDirectory, "plugins");
            
            if (Directory.Exists(pluginsDirectory))
            {
                return pluginsDirectory;
            }

            // Fallback: try to get from existing plugin info
            var plugins = _availablePluginsSource.Items.ToList();
            if (plugins.Any())
            {
                var firstPlugin = plugins.First();
                var pluginDir = Path.GetDirectoryName(firstPlugin.PluginDirectory);
                if (!string.IsNullOrEmpty(pluginDir) && Directory.Exists(pluginDir))
                {
                    return pluginDir;
                }
            }

            // Create the expected plugins directory
            Directory.CreateDirectory(pluginsDirectory);
            return pluginsDirectory;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to determine plugin directory path");
            var fallbackPath = Path.Combine(AppContext.BaseDirectory, "plugins");
            Directory.CreateDirectory(fallbackPath);
            return fallbackPath;
        }
    }

    /// <summary>
    /// Manually refresh the plugin list
    /// </summary>
    public async Task RefreshAsync()
    {
        await LoadAvailablePluginsAsync();
    }

    public void Dispose()
    {
        _availablePluginsSource.Dispose();
        _disposables.Dispose();
    }
}