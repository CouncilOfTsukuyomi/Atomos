using System;
using System.Collections.Generic;
using PluginManager.Core.Models;
using ReactiveUI;

namespace Atomos.UI.Models;

public class PluginDisplayItem : ReactiveObject
{
    private string _pluginId = string.Empty;
    public string PluginId
    {
        get => _pluginId;
        set => this.RaiseAndSetIfChanged(ref _pluginId, value);
    }

    private string _pluginName = string.Empty;
    public string PluginName
    {
        get => _pluginName;
        set => this.RaiseAndSetIfChanged(ref _pluginName, value);
    }

    private List<PluginMod> _mods = new();
    public List<PluginMod> Mods
    {
        get => _mods;
        set => this.RaiseAndSetIfChanged(ref _mods, value);
    }

    private DateTime _lastUpdated;
    public DateTime LastUpdated
    {
        get => _lastUpdated;
        set => this.RaiseAndSetIfChanged(ref _lastUpdated, value);
    }

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set => this.RaiseAndSetIfChanged(ref _isLoading, value);
    }

    private string? _errorMessage;
    public string? ErrorMessage
    {
        get => _errorMessage;
        set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
    }
    
    private bool _isExpanded = true;
    public bool IsExpanded
    {
        get => _isExpanded;
        set => this.RaiseAndSetIfChanged(ref _isExpanded, value);
    }
}