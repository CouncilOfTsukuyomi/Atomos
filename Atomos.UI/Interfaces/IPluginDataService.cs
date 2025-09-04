using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PluginManager.Core.Models;

namespace Atomos.UI.Interfaces;

public interface IPluginDataService : IDisposable
{
    IObservable<List<PluginInfo>> PluginInfos { get; }
    IObservable<Dictionary<string, List<PluginMod>>> PluginMods { get; }
    
    Task RefreshPluginInfoAsync();
    Task RefreshPluginModsAsync();
    Task RefreshPluginModsForPlugin(string pluginId);
    List<PluginMod> GetCachedModsForPlugin(string pluginId);
    
    // Clears cached data and UI signals for a specific plugin
    void ClearPluginData(string pluginId);
    
    // Clears all in-memory cached data and notifies observers with empty collections
    void ClearAllData();
}