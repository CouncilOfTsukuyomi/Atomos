using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using Atomos.UI.Interfaces;
using NLog;
using PluginManager.Core.Interfaces;
using PluginManager.Core.Models;

namespace Atomos.UI.Services;

public class PluginDataService : IPluginDataService
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
    
    private readonly IPluginManagementService _pluginManagementService;
    private readonly IPluginService _pluginService;
    private readonly CompositeDisposable _disposables = new();
    
    private readonly BehaviorSubject<List<PluginInfo>> _pluginInfoSubject = new(new List<PluginInfo>());
    private readonly BehaviorSubject<Dictionary<string, List<PluginMod>>> _pluginModsSubject = new(new Dictionary<string, List<PluginMod>>());
    
    private readonly Dictionary<string, List<PluginMod>> _pluginModsCache = new();
    private readonly Dictionary<string, DateTime> _lastModsFetch = new();
    private readonly TimeSpan _modsCacheTimeout = TimeSpan.FromMinutes(5);
    
    public IObservable<List<PluginInfo>> PluginInfos => _pluginInfoSubject.AsObservable();
    public IObservable<Dictionary<string, List<PluginMod>>> PluginMods => _pluginModsSubject.AsObservable();
    
    public PluginDataService(
        IPluginManagementService pluginManagementService,
        IPluginService pluginService)
    {
        _pluginManagementService = pluginManagementService;
        _pluginService = pluginService;
        
        Observable.Timer(TimeSpan.Zero, TimeSpan.FromMinutes(2))
            .SelectMany(_ => Observable.FromAsync(RefreshPluginInfoAsync))
            .Subscribe()
            .DisposeWith(_disposables);
        
        Observable.Timer(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5))
            .SelectMany(_ => Observable.FromAsync(RefreshPluginModsAsync))
            .Subscribe()
            .DisposeWith(_disposables);
    }
    
    public async Task RefreshPluginInfoAsync()
    {
        try
        {
            _logger.Debug("Refreshing plugin info from management service");
            var pluginInfos = await _pluginManagementService.GetAvailablePluginsAsync();
            _pluginInfoSubject.OnNext(pluginInfos);
            _logger.Debug("Updated plugin info: {Count} plugins", pluginInfos.Count);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to refresh plugin info");
        }
    }
    
    public async Task RefreshPluginModsAsync()
    {
        try
        {
            var enabledPlugins = _pluginService.GetEnabledPlugins();
            var updatedMods = new Dictionary<string, List<PluginMod>>(_pluginModsCache);
            
            foreach (var plugin in enabledPlugins)
            {
                try
                {
                    var needsRefresh = !_lastModsFetch.ContainsKey(plugin.PluginId) ||
                                     DateTime.UtcNow - _lastModsFetch[plugin.PluginId] > _modsCacheTimeout;
                    
                    if (needsRefresh)
                    {
                        _logger.Debug("Refreshing mods for plugin {PluginId}", plugin.PluginId);
                        var mods = await _pluginService.GetRecentModsFromPluginAsync(plugin.PluginId);
                        updatedMods[plugin.PluginId] = mods;
                        _lastModsFetch[plugin.PluginId] = DateTime.UtcNow;
                        _logger.Debug("Updated {Count} mods for plugin {PluginId}", mods.Count, plugin.PluginId);
                    }
                    else
                    {
                        _logger.Debug("Using cached mods for plugin {PluginId} (last fetch: {LastFetch})", 
                            plugin.PluginId, _lastModsFetch[plugin.PluginId]);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to refresh mods for plugin {PluginId}", plugin.PluginId);
                }
            }
            
            var enabledPluginIds = enabledPlugins.Select(p => p.PluginId).ToHashSet();
            var keysToRemove = updatedMods.Keys.Where(k => !enabledPluginIds.Contains(k)).ToList();
            foreach (var key in keysToRemove)
            {
                updatedMods.Remove(key);
                _pluginModsCache.Remove(key);
                _lastModsFetch.Remove(key);
                _logger.Debug("Removed cached data for disabled plugin {PluginId}", key);
            }
            
            _pluginModsCache.Clear();
            foreach (var kvp in updatedMods)
            {
                _pluginModsCache[kvp.Key] = kvp.Value;
            }
            
            _pluginModsSubject.OnNext(new Dictionary<string, List<PluginMod>>(updatedMods));
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to refresh plugin mods");
        }
    }
    
    public async Task RefreshPluginModsForPlugin(string pluginId)
    {
        try
        {
            _logger.Debug("Force refreshing mods for plugin {PluginId}", pluginId);
            var mods = await _pluginService.GetRecentModsFromPluginAsync(pluginId);
            
            var updatedMods = new Dictionary<string, List<PluginMod>>(_pluginModsCache)
            {
                [pluginId] = mods
            };
            
            _pluginModsCache[pluginId] = mods;
            _lastModsFetch[pluginId] = DateTime.UtcNow;
            _pluginModsSubject.OnNext(updatedMods);
            _logger.Debug("Force refreshed {Count} mods for plugin {PluginId}", mods.Count, pluginId);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to refresh mods for plugin {PluginId}", pluginId);
        }
    }
    
    public List<PluginMod> GetCachedModsForPlugin(string pluginId)
    {
        return _pluginModsCache.GetValueOrDefault(pluginId, new List<PluginMod>());
    }
    
    public void Dispose()
    {
        _pluginInfoSubject?.Dispose();
        _pluginModsSubject?.Dispose();
        _disposables?.Dispose();
    }
}