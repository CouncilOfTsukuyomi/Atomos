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
using PluginManager.Core.Models;
using ReactiveUI;

namespace Atomos.UI.ViewModels;

public class PluginDataViewModel : ViewModelBase, IDisposable
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

    private readonly IPluginDataService _pluginDataService;
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
        IPluginDataService pluginDataService,
        IDownloadManagerService downloadManagerService)
    {
        _pluginDataService = pluginDataService;
        _downloadManagerService = downloadManagerService;
        PluginItems = new ObservableCollection<PluginDisplayItem>();

        RefreshCommand = ReactiveCommand.CreateFromTask(RefreshAllData);
        RefreshPluginCommand = ReactiveCommand.CreateFromTask<string>(RefreshPluginDataAsync);
        TogglePluginExpandCommand = ReactiveCommand.Create<PluginDisplayItem>(TogglePluginExpand);
        ExpandAllCommand = ReactiveCommand.Create(ExpandAll);
        CollapseAllCommand = ReactiveCommand.Create(CollapseAll);
        
        _pluginDataService.PluginMods
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(UpdatePluginItems)
            .DisposeWith(_disposables);

        _logger.Debug("PluginDataViewModel initialized with centralized data service");
    }
        
    public async Task DownloadModAsync(PluginMod pluginMod, CancellationToken ct = default)
    {
        try
        {
            _logger.Info("Starting download for mod: {ModName} from plugin source: {PluginSource}", 
                pluginMod.Name, pluginMod.PluginSource);
        
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

    private void UpdatePluginItems(Dictionary<string, List<PluginMod>> pluginMods)
    {
        try
        {
            var existingItems = PluginItems.ToDictionary(x => x.PluginId, x => x);
            
            PluginItems.Clear();
            
            foreach (var kvp in pluginMods)
            {
                var pluginId = kvp.Key;
                var mods = kvp.Value;
                
                var displayItem = existingItems.GetValueOrDefault(pluginId) ?? new PluginDisplayItem
                {
                    PluginId = pluginId,
                    PluginName = pluginId,
                    IsExpanded = false
                };
                
                // Only attach heavy mods list if expanded; otherwise drop references to save memory
                displayItem.Mods = displayItem.IsExpanded ? mods : new List<PluginMod>();
                displayItem.IsLoading = false;
                displayItem.ErrorMessage = null;
                
                PluginItems.Add(displayItem);
            }
            
            HasError = false;
            ErrorMessage = "";
            
            _logger.Debug("Updated UI with {Count} plugin items from centralized service", PluginItems.Count);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to update plugin items");
            HasError = true;
            ErrorMessage = ex.Message;
        }
    }

    private void TogglePluginExpand(PluginDisplayItem plugin)
    {
        if (plugin == null) return;

        plugin.IsExpanded = !plugin.IsExpanded;
        _logger.Debug("Toggled plugin {PluginId} expand state to {IsExpanded}", plugin.PluginId, plugin.IsExpanded);

        if (plugin.IsExpanded)
        {
            // Populate from cache if available to immediately show content
            var cached = _pluginDataService.GetCachedModsForPlugin(plugin.PluginId) ?? new List<PluginMod>();
            plugin.Mods = cached;

            // If nothing in cache, trigger a background fetch
            if (cached.Count == 0)
            {
                plugin.IsLoading = true;
                _ = _pluginDataService.RefreshPluginModsForPlugin(plugin.PluginId)
                    .ContinueWith(t =>
                    {
                        if (t.IsFaulted && t.Exception != null)
                        {
                            _logger.Error(t.Exception, "Failed background fetch when expanding {PluginId}", plugin.PluginId);
                        }
                        RxApp.MainThreadScheduler.Schedule(Unit.Default, (sched, state) =>
                        {
                            plugin.IsLoading = false;
                            return Disposable.Empty;
                        });
                    });
            }
        }
        else
        {
            // Collapse: drop reference to heavy mods list to free memory and clear service cache
            plugin.Mods = new List<PluginMod>();
            _pluginDataService.ClearPluginData(plugin.PluginId);
        }
    }

    private void ExpandAll()
    {
        foreach (var plugin in PluginItems)
        {
            plugin.IsExpanded = true;
        }
        _logger.Debug("Expanded all {Count} plugins", PluginItems.Count);
    }

    private void CollapseAll()
    {
        foreach (var plugin in PluginItems)
        {
            plugin.IsExpanded = false;
            plugin.Mods = new List<PluginMod>();
            _pluginDataService.ClearPluginData(plugin.PluginId);
        }
        _logger.Debug("Collapsed all {Count} plugins and cleared caches", PluginItems.Count);
    }

    private async Task RefreshAllData()
    {
        try
        {
            IsLoading = true;
            _logger.Debug("Manually refreshing all plugin data (clearing caches first)");
            // Clear in-memory caches and UI-bound collections to release memory before reloading
            _pluginDataService.ClearAllData();
            // Proactively ask GC to reclaim memory after clearing, without blocking UI
            _ = Task.Run(() => { try { GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true); } catch { } });
            await _pluginDataService.RefreshPluginInfoAsync();
            await _pluginDataService.RefreshPluginModsAsync();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to refresh all data");
            HasError = true;
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task RefreshPluginDataAsync(string pluginId)
    {
        if (string.IsNullOrEmpty(pluginId)) return;

        try
        {
            var displayItem = PluginItems.FirstOrDefault(x => x.PluginId == pluginId);
            if (displayItem != null)
            {
                displayItem.IsLoading = true;
                displayItem.ErrorMessage = null;
                // Release references to the current mods list to free memory sooner
                displayItem.Mods = new List<PluginMod>();
            }

            _logger.Debug("Manually refreshing plugin data for {PluginId} (clearing caches first)", pluginId);
            _pluginDataService.ClearPluginData(pluginId);
            // Proactively ask GC to reclaim memory after clearing, without blocking UI
            _ = Task.Run(() => { try { GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true); } catch { } });

            await _pluginDataService.RefreshPluginModsForPlugin(pluginId);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to refresh plugin data for {PluginId}", pluginId);
            
            var displayItem = PluginItems.FirstOrDefault(x => x.PluginId == pluginId);
            if (displayItem != null)
            {
                displayItem.ErrorMessage = ex.Message;
                displayItem.IsLoading = false;
            }
        }
    }

    public void Dispose()
    {
        _disposables.Dispose();
    }
}