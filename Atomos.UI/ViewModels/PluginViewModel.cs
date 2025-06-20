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
using Atomos.UI.Views;
using DynamicData;
using DynamicData.Binding;
using NLog;
using PluginManager.Core.Interfaces;
using PluginManager.Core.Models;
using ReactiveUI;

namespace Atomos.UI.ViewModels;

public class PluginsViewModel : ViewModelBase, IDisposable
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
    public ReactiveCommand<Unit, Unit> RefreshCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenPluginDirectoryCommand { get; }

    public event Action<PluginSettingsViewModel>? PluginSettingsRequested;

    public PluginsViewModel(
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
        RefreshCommand = ReactiveCommand.CreateFromTask(RefreshAsync);
        OpenPluginDirectoryCommand = ReactiveCommand.Create(OpenPluginDirectory);

        // Periodically refresh available plugins
        Observable.Timer(TimeSpan.Zero, TimeSpan.FromSeconds(30))
            .SelectMany(_ => Observable.FromAsync(LoadAvailablePluginsAsync))
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe()
            .DisposeWith(_disposables);
        
        DebugPluginPaths();
    }
    
    private void DebugPluginPaths()
    {
        var baseDir = AppContext.BaseDirectory;
        var pluginsDir = Path.Combine(baseDir, "plugins");
        _logger.Debug("Base directory: {BaseDir}", baseDir);
        _logger.Debug("Looking for plugins in: {PluginsDir}", pluginsDir);

        if (Directory.Exists(pluginsDir))
        {
            var subdirs = Directory.GetDirectories(pluginsDir);
            _logger.Debug("Found {Count} subdirectories: {Dirs}", subdirs.Length, string.Join(", ", subdirs.Select(Path.GetFileName)));
        
            foreach (var dir in subdirs)
            {
                var files = Directory.GetFiles(dir);
                _logger.Debug("Directory {Dir} contains: {Files}", Path.GetFileName(dir), string.Join(", ", files.Select(Path.GetFileName)));
            }
        }
        else
        {
            _logger.Debug("Plugins directory does not exist: {PluginsDir}", pluginsDir);
        }
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

            foreach (var plugin in fetchedPlugins)
            {
                _logger.Debug("Fetched plugin {PluginId} - {DisplayName} with IsEnabled={IsEnabled}", 
                    plugin.PluginId, plugin.DisplayName, plugin.IsEnabled);
            }

            // Use DynamicData to efficiently update the collection
            _availablePluginsSource.Edit(updater =>
            {
                updater.Clear();
                
                // Add each plugin individually and verify the state
                foreach (var plugin in fetchedPlugins)
                {
                    _logger.Debug("Adding plugin {PluginId} to source with IsEnabled={IsEnabled}", 
                        plugin.PluginId, plugin.IsEnabled);
                    updater.Add(plugin);
                }
            });

            // Verify what's actually in the source after adding
            foreach (var plugin in _availablePluginsSource.Items)
            {
                _logger.Debug("Plugin in source: {PluginId} - IsEnabled={IsEnabled}", 
                    plugin.PluginId, plugin.IsEnabled);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load available plugins in PluginsViewModel.");
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
            // Debug the current state
            _logger.Debug("Current plugin state - PluginId: {PluginId}, IsLoaded: {IsLoaded}, IsEnabled: {IsEnabled}", 
                plugin.PluginId, plugin.IsLoaded, plugin.IsEnabled);

            // Toggle based on IsEnabled (registry state), not IsLoaded (runtime state)
            var desiredEnabledState = !plugin.IsEnabled;

            _logger.Info("User clicked to {Action} plugin {PluginId} (IsEnabled: {IsEnabled} -> {DesiredEnabled})", 
                desiredEnabledState ? "ENABLE" : "DISABLE", 
                plugin.PluginId,
                plugin.IsEnabled,
                desiredEnabledState);

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

            // Check if plugin has configurable settings using the schema
            var hasConfigurableSettings = await _pluginDiscoveryService.HasConfigurableSettingsAsync(plugin.PluginDirectory);
            
            if (!hasConfigurableSettings)
            {
                _logger.Info("Plugin {PluginId} has no configurable settings schema", plugin.PluginId);
                return;
            }

            // Create the settings view model and request it to be shown
            var settingsViewModel = new PluginSettingsViewModel(plugin, _pluginDiscoveryService);
            PluginSettingsRequested?.Invoke(settingsViewModel);
            settingsViewModel.Show();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to open settings for plugin {PluginId}", plugin.PluginId);
        }
    }

    /// <summary>
    /// Opens the plugin directory in the system file explorer
    /// </summary>
    private void OpenPluginDirectory()
    {
        try
        {
            // Get the actual plugin directory path
            var pluginDirectory = GetPluginDirectoryPath();
            
            if (!Directory.Exists(pluginDirectory))
            {
                _logger.Warn("Plugin directory does not exist: {Directory}", pluginDirectory);
                Directory.CreateDirectory(pluginDirectory);
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{pluginDirectory}\"",
                    UseShellExecute = true
                });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "xdg-open",
                    Arguments = $"\"{pluginDirectory}\"",
                    UseShellExecute = true
                });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "open",
                    Arguments = $"\"{pluginDirectory}\"",
                    UseShellExecute = true
                });
            }

            _logger.Info("Opened plugin directory: {Directory}", pluginDirectory);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to open plugin directory");
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

            // Fallback: try to get from a plugin info if available
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

            // Final fallback: create the expected plugins directory
            Directory.CreateDirectory(pluginsDirectory);
            return pluginsDirectory;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to determine plugin directory path");
            // Ultimate fallback - changed from "addons" to "plugins"
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