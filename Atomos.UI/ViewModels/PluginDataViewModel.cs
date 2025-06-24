using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Atomos.UI.Interfaces;
using Atomos.UI.Models;
using CommonLib.Models;
using NLog;
using PluginManager.Core.Interfaces;
using PluginManager.Core.Models;
using ReactiveUI;

namespace Atomos.UI.ViewModels
{
    public class PluginDataViewModel : ViewModelBase, IDisposable
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        private readonly IPluginManagementService _pluginManagementService;
        private readonly IPluginService _pluginService;
        private readonly IDownloadManagerService _downloadManagerService;
        private readonly CompositeDisposable _disposables = new();

        private ObservableCollection<PluginDisplayItem> _pluginItems;
        public ObservableCollection<PluginDisplayItem> PluginItems
        {
            get => _pluginItems;
            set => this.RaiseAndSetIfChanged(ref _pluginItems, value);
        }

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set => this.RaiseAndSetIfChanged(ref _isLoading, value);
        }

        private bool _hasError;
        public bool HasError
        {
            get => _hasError;
            set => this.RaiseAndSetIfChanged(ref _hasError, value);
        }

        private string _errorMessage = string.Empty;
        public string ErrorMessage
        {
            get => _errorMessage;
            set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
        }

        public ReactiveCommand<Unit, Unit> RefreshCommand { get; }
        public ReactiveCommand<string, Unit> RefreshPluginCommand { get; }
        public ReactiveCommand<PluginDisplayItem, Unit> TogglePluginExpandCommand { get; }
        public ReactiveCommand<Unit, Unit> ExpandAllCommand { get; }
        public ReactiveCommand<Unit, Unit> CollapseAllCommand { get; }

        public PluginDataViewModel(
            IPluginManagementService pluginManagementService, 
            IPluginService pluginService,
            IDownloadManagerService downloadManagerService)
        {
            _pluginManagementService = pluginManagementService;
            _pluginService = pluginService;
            _downloadManagerService = downloadManagerService;
            PluginItems = new ObservableCollection<PluginDisplayItem>();

            // Commands
            RefreshCommand = ReactiveCommand.CreateFromTask(LoadPluginDataAsync);
            RefreshPluginCommand = ReactiveCommand.CreateFromTask<string>(RefreshPluginDataAsync);
            
            // Collapse/Expand Commands
            TogglePluginExpandCommand = ReactiveCommand.Create<PluginDisplayItem>(TogglePluginExpand);
            ExpandAllCommand = ReactiveCommand.Create(ExpandAll);
            CollapseAllCommand = ReactiveCommand.Create(CollapseAll);

            // Periodically refresh plugin data
            Observable.Timer(TimeSpan.Zero, TimeSpan.FromMinutes(5))
                .SelectMany(_ => Observable.FromAsync(LoadPluginDataAsync))
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe()
                .DisposeWith(_disposables);
        }
        
        public async Task DownloadModAsync(PluginMod pluginMod, CancellationToken ct = default)
        {
            try
            {
                _logger.Info("Starting download for mod: {ModName} from plugin source: {PluginSource}", 
                    pluginMod.Name, pluginMod.PluginSource);
        
                // Create a progress reporter that provides rich updates like your updater
                var progress = new Progress<DownloadProgress>(OnDownloadProgressChanged);
        
                await _downloadManagerService.DownloadModAsync(pluginMod, ct, progress);
        
                _logger.Info("Successfully completed download for mod: {ModName}", pluginMod.Name);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to download mod: {ModName} from {PluginSource}", 
                    pluginMod.Name, pluginMod.PluginSource);
                throw;
            }
        }
        
        private void OnDownloadProgressChanged(DownloadProgress progress)
        {
            _logger.Info("=== PLUGIN DOWNLOAD PROGRESS ===");
            _logger.Info("Status: {Status}", progress.Status);
            _logger.Info("Progress: {Percent}% - {FormattedSize} at {FormattedSpeed}", 
                progress.PercentComplete, progress.FormattedSize, progress.FormattedSpeed);
            _logger.Info("Elapsed: {Elapsed}", progress.ElapsedTime);
            _logger.Info("=== END PLUGIN DOWNLOAD PROGRESS ===");
        }


        /// <summary>
        /// Toggle the expanded state of a specific plugin
        /// </summary>
        private void TogglePluginExpand(PluginDisplayItem plugin)
        {
            if (plugin != null)
            {
                plugin.IsExpanded = !plugin.IsExpanded;
                _logger.Debug("Toggled plugin {PluginId} expand state to {IsExpanded}", plugin.PluginId, plugin.IsExpanded);
            }
        }

        /// <summary>
        /// Expand all plugins
        /// </summary>
        private void ExpandAll()
        {
            foreach (var plugin in PluginItems)
            {
                plugin.IsExpanded = true;
            }
            _logger.Debug("Expanded all {Count} plugins", PluginItems.Count);
        }

        /// <summary>
        /// Collapse all plugins
        /// </summary>
        private void CollapseAll()
        {
            foreach (var plugin in PluginItems)
            {
                plugin.IsExpanded = false;
            }
            _logger.Debug("Collapsed all {Count} plugins", PluginItems.Count);
        }

        /// <summary>
        /// Loads plugin data from all enabled and loaded plugins
        /// </summary>
        private async Task LoadPluginDataAsync()
        {
            try
            {
                IsLoading = true;
                HasError = false;
                ErrorMessage = "";

                _logger.Debug("Loading plugin data...");

                // Check all plugins first
                var allPlugins = _pluginService.GetAllPlugins();
                _logger.Debug("Total plugins registered: {Count}", allPlugins.Count);

                // Get all enabled plugins from the plugin service
                var enabledPlugins = _pluginService.GetEnabledPlugins();
                _logger.Debug("Found {Count} enabled plugins", enabledPlugins.Count);

                // If no plugins are registered at all, show helpful message
                if (allPlugins.Count == 0)
                {
                    _logger.Warn("No plugins are registered in the PluginService");
                    HasError = true;
                    ErrorMessage = "No plugins are registered. Please check that plugins are being loaded and registered properly.";
                    return;
                }

                // If plugins exist but none are enabled
                if (enabledPlugins.Count == 0)
                {
                    _logger.Warn("No plugins are enabled. Total plugins: {Total}", allPlugins.Count);
                    HasError = true;
                    ErrorMessage = $"No plugins are enabled. Found {allPlugins.Count} registered plugin(s), but none are enabled.";
                    return;
                }

                // Update existing items or create new ones
                var updatedItems = new List<PluginDisplayItem>();

                foreach (var plugin in enabledPlugins)
                {
                    try
                    {
                        _logger.Debug("Processing plugin: {PluginId} - {DisplayName} (Enabled: {IsEnabled})", 
                            plugin.PluginId, plugin.DisplayName, plugin.IsEnabled);

                        // Find existing item or create new one
                        var existingItem = PluginItems.FirstOrDefault(x => x.PluginId == plugin.PluginId);
                        var displayItem = existingItem ?? new PluginDisplayItem
                        {
                            PluginId = plugin.PluginId,
                            PluginName = plugin.DisplayName,
                            IsExpanded = false // Default to collapsed for new items
                        };

                        // Load recent mods from this plugin
                        displayItem.IsLoading = true;
                        displayItem.ErrorMessage = null;
                        
                        _logger.Debug("Calling GetRecentModsFromPluginAsync for plugin {PluginId}", plugin.PluginId);
                        var recentMods = await _pluginService.GetRecentModsFromPluginAsync(plugin.PluginId);
                        displayItem.Mods = recentMods ?? new List<PluginMod>();
                        displayItem.LastUpdated = DateTime.Now;
                        displayItem.IsLoading = false;

                        _logger.Debug("Loaded {ModCount} mods for plugin {PluginId}", displayItem.Mods.Count, plugin.PluginId);
                        updatedItems.Add(displayItem);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Failed to load data for plugin {PluginId}", plugin.PluginId);
                        
                        var existingItem = PluginItems.FirstOrDefault(x => x.PluginId == plugin.PluginId);
                        var errorItem = existingItem ?? new PluginDisplayItem
                        {
                            PluginId = plugin.PluginId,
                            PluginName = plugin.DisplayName,
                            IsExpanded = true
                        };
                        
                        errorItem.ErrorMessage = ex.Message;
                        errorItem.IsLoading = false;
                        errorItem.LastUpdated = DateTime.Now;
                        updatedItems.Add(errorItem);
                    }
                }

                // Update the collection
                PluginItems.Clear();
                foreach (var item in updatedItems)
                {
                    PluginItems.Add(item);
                }

                _logger.Info("Successfully loaded data for {Count} plugins", updatedItems.Count);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to load plugin data");
                HasError = true;
                ErrorMessage = ex.Message;
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// Refreshes data for a specific plugin
        /// </summary>
        private async Task RefreshPluginDataAsync(string pluginId)
        {
            if (string.IsNullOrEmpty(pluginId)) return;

            var displayItem = PluginItems.FirstOrDefault(x => x.PluginId == pluginId);
            if (displayItem == null) return;

            try
            {
                displayItem.IsLoading = true;
                displayItem.ErrorMessage = null;

                _logger.Debug("Refreshing data for plugin {PluginId}", pluginId);

                // Get the plugin to ensure it's still available
                var plugin = _pluginService.GetPlugin(pluginId);
                
                if (plugin != null)
                {
                    // Load recent mods from this specific plugin
                    var recentMods = await _pluginService.GetRecentModsFromPluginAsync(pluginId);
                    
                    // Update the display item
                    displayItem.Mods = recentMods ?? new List<PluginMod>();
                    displayItem.LastUpdated = DateTime.Now;

                    _logger.Debug("Refreshed {ModCount} mods for plugin {PluginId}", displayItem.Mods.Count, pluginId);
                }
                else
                {
                    displayItem.ErrorMessage = "Plugin not found or not enabled";
                    _logger.Warn("Plugin {PluginId} not found or not enabled", pluginId);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to refresh data for plugin {PluginId}", pluginId);
                displayItem.ErrorMessage = ex.Message;
            }
            finally
            {
                displayItem.IsLoading = false;
            }
        }

        public void Dispose()
        {
            _disposables.Dispose();
        }
    }
}