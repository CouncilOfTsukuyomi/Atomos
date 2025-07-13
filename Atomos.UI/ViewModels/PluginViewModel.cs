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
using Avalonia.Threading;
using DynamicData;
using DynamicData.Binding;
using NLog;
using PluginManager.Core.Events;
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

    private DateTime _lastRefresh = DateTime.MinValue;
    private readonly TimeSpan _minRefreshInterval = TimeSpan.FromSeconds(10);

    public ReadOnlyObservableCollection<PluginInfo> FilteredPlugins => _filteredPlugins;
    
    public ReactiveCommand<PluginInfo, Unit> TogglePluginCommand { get; }
    public ReactiveCommand<PluginInfo, Unit> OpenSettingsCommand { get; }
    public ReactiveCommand<PluginInfo, Unit> ValidateSettingsCommand { get; }
    public ReactiveCommand<PluginInfo, Unit> RollbackSettingsCommand { get; }
    public ReactiveCommand<Unit, Unit> RefreshCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenPluginDirectoryCommand { get; }
    public ReactiveCommand<Unit, Unit> LoadAllEnabledPluginsCommand { get; }
    
    public ReactiveCommand<Unit, Unit> OpenDiscordCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenGitHubIssuesCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenPluginDocsCommand { get; }
    
    public event Action<PluginSettingsViewModel>? PluginSettingsRequested;

    public PluginViewModel(
        IPluginManagementService pluginManagementService,
        IPluginDiscoveryService pluginDiscoveryService)
    {
        _pluginManagementService = pluginManagementService;
        _pluginDiscoveryService = pluginDiscoveryService;

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

        TogglePluginCommand = ReactiveCommand.CreateFromTask<PluginInfo>(TogglePluginAsync);
        OpenSettingsCommand = ReactiveCommand.CreateFromTask<PluginInfo>(OpenPluginSettingsAsync);
        ValidateSettingsCommand = ReactiveCommand.CreateFromTask<PluginInfo>(ValidatePluginSettingsAsync);
        RollbackSettingsCommand = ReactiveCommand.CreateFromTask<PluginInfo>(RollbackPluginSettingsAsync);
        RefreshCommand = ReactiveCommand.CreateFromTask(RefreshAsync);
        OpenPluginDirectoryCommand = ReactiveCommand.Create(OpenPluginDirectory);
        LoadAllEnabledPluginsCommand = ReactiveCommand.CreateFromTask(LoadAllEnabledPluginsAsync);

        OpenDiscordCommand = ReactiveCommand.Create(OpenDiscord);
        OpenGitHubIssuesCommand = ReactiveCommand.Create(OpenGitHubIssues);
        OpenPluginDocsCommand = ReactiveCommand.Create(OpenPluginDocs);

        _pluginDiscoveryService.AllPluginsLoaded += OnAllPluginsLoaded;
        _pluginDiscoveryService.PluginDiscovered += OnPluginDiscovered;

        _ = LoadAvailablePluginsAsync();
        
        Observable.Timer(TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(2))
            .Where(_ => DateTime.UtcNow - _lastRefresh >= _minRefreshInterval)
            .SelectMany(_ => Observable.FromAsync(() => LoadAvailablePluginsAsync(false)))
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe()
            .DisposeWith(_disposables);
    }

    private void OnAllPluginsLoaded(object? sender, AllPluginsLoadedEventArgs e)
    {
        _logger.Info("All plugins loaded notification received. {SuccessCount} loaded, {FailedCount} failed in {Duration}ms",
            e.LoadedPlugins.Count, e.FailedPlugins.Count, e.TotalLoadTime.TotalMilliseconds);

        foreach (var failedPlugin in e.FailedPlugins)
        {
            _logger.Warn("Failed to load plugin: {PluginId} - {LoadError}", 
                failedPlugin.PluginId, failedPlugin.LoadError);
        }

        Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await LoadAvailablePluginsAsync(forceRefresh: true);
        });
    }
    
    private void OnPluginDiscovered(object? sender, PluginDiscoveredEventArgs e)
    {
        _logger.Info("Plugin discovered: {PluginId} v{Version} by {Author} (IsNew: {IsNew})", 
            e.DiscoveredPlugin.PluginId, 
            e.DiscoveredPlugin.Version, 
            e.DiscoveredPlugin.Author,
            e.IsNewPlugin);

        if (e.IsNewPlugin)
        {
            _logger.Info("New plugin detected: {PluginId}, refreshing UI immediately", e.DiscoveredPlugin.PluginId);
        }
        
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            var existingPlugin = _availablePluginsSource.Items.FirstOrDefault(p => p.PluginId == e.DiscoveredPlugin.PluginId);
        
            _availablePluginsSource.Edit(updater =>
            {
                if (existingPlugin != null)
                {
                    var index = updater.IndexOf(existingPlugin);
                    if (index >= 0)
                    {
                        updater[index] = e.DiscoveredPlugin;
                    }
                }
                else
                {
                    updater.Add(e.DiscoveredPlugin);
                }
            });

            _logger.Debug("Updated plugin source with discovered plugin: {PluginId}", e.DiscoveredPlugin.PluginId);
        });
    }

    private async Task LoadAllEnabledPluginsAsync()
    {
        try
        {
            IsLoading = true;
            _logger.Info("Loading all enabled plugins...");
            
            await _pluginDiscoveryService.LoadAllEnabledPluginsAsync();
            
            _logger.Info("All enabled plugins load initiated");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load all enabled plugins");
        }
        finally
        {
            IsLoading = false;
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

    private async Task LoadAvailablePluginsAsync(bool forceRefresh = true)
    {
        try
        {
            if (!forceRefresh && DateTime.UtcNow - _lastRefresh < _minRefreshInterval)
            {
                _logger.Debug("Skipping plugin refresh - too soon since last refresh");
                return;
            }

            IsLoading = true;
            _logger.Debug("Loading available plugins (forceRefresh: {ForceRefresh})", forceRefresh);
            
            var fetchedPlugins = await _pluginManagementService.GetAvailablePluginsAsync();

            _logger.Debug("Fetched {Count} plugins from management service", fetchedPlugins.Count);

            _availablePluginsSource.Edit(updater =>
            {
                updater.Clear();
                updater.AddRange(fetchedPlugins);
            });

            _lastRefresh = DateTime.UtcNow;
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

    private async Task TogglePluginAsync(PluginInfo plugin)
    {
        try
        {
            var desiredEnabledState = !plugin.IsEnabled;

            _logger.Info("User toggling plugin {PluginId} from {CurrentState} to {DesiredState}", 
                plugin.PluginId, plugin.IsEnabled, desiredEnabledState);

            await _pluginManagementService.SetPluginEnabledAsync(plugin.PluginId, desiredEnabledState);

            await Task.Delay(500);

            await LoadAvailablePluginsAsync(true);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to toggle plugin {PluginId}", plugin.PluginId);
        }
    }

    private async Task OpenPluginSettingsAsync(PluginInfo plugin)
    {
        try
        {
            _logger.Info("Opening settings for plugin {PluginId}", plugin.PluginId);

            var hasConfigurableSettings = await _pluginDiscoveryService.HasConfigurableSettingsAsync(plugin.PluginDirectory);
            
            if (!hasConfigurableSettings)
            {
                _logger.Info("Plugin {PluginId} has no configurable settings", plugin.PluginId);
                return;
            }

            var settingsViewModel = new PluginSettingsViewModel(plugin, _pluginDiscoveryService);
            PluginSettingsRequested?.Invoke(settingsViewModel);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to open settings for plugin {PluginId}", plugin.PluginId);
        }
    }

    private async Task ValidatePluginSettingsAsync(PluginInfo plugin)
    {
        try
        {
            _logger.Info("Validating settings for plugin {PluginId}", plugin.PluginId);

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

    private async Task RollbackPluginSettingsAsync(PluginInfo plugin)
    {
        try
        {
            _logger.Info("Rolling back settings for plugin {PluginId}", plugin.PluginId);

            var hasConfigurableSettings = await _pluginDiscoveryService.HasConfigurableSettingsAsync(plugin.PluginDirectory);
            
            if (!hasConfigurableSettings)
            {
                return;
            }

            var success = await _pluginDiscoveryService.RollbackSettingsAsync(plugin.PluginDirectory);
            
            if (success)
            {
                _logger.Info("Settings rollback successful for plugin {PluginId}", plugin.PluginId);
                await LoadAvailablePluginsAsync(true);
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

    private void OpenDiscord()
    {
        try
        {
            var discordUrl = "https://discord.gg/rtGXwMn7pX";
            OpenUrl(discordUrl);
            _logger.Info("Opened Discord community link");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to open Discord link");
        }
    }

    private void OpenGitHubIssues()
    {
        try
        {
            var githubUrl = "https://github.com/CouncilOfTsukuyomi/Atomos/issues/new?template=plugin-request.md";
            OpenUrl(githubUrl);
            _logger.Info("Opened GitHub Issues page");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to open GitHub Issues link");
        }
    }

    private void OpenPluginDocs()
    {
        try
        {
            var docsUrl = "https://github.com/CouncilOfTsukuyomi/PluginTemplate";
            OpenUrl(docsUrl);
            _logger.Info("Opened plugin development documentation");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to open plugin documentation link");
        }
    }

    private void OpenUrl(string url)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "xdg-open",
                    Arguments = url,
                    UseShellExecute = true
                });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "open",
                    Arguments = url,
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to open URL: {Url}", url);
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

    public async Task RefreshAsync()
    {
        await LoadAvailablePluginsAsync();
    }

    public void Dispose()
    {
        _pluginDiscoveryService.AllPluginsLoaded -= OnAllPluginsLoaded;
        _pluginDiscoveryService.PluginDiscovered -= OnPluginDiscovered;
        _availablePluginsSource.Dispose();
        _disposables.Dispose();
    }
}