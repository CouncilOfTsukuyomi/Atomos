
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Atomos.UI.Enums;
using Atomos.UI.Models;
using NLog;
using PluginManager.Core.Interfaces;
using PluginManager.Core.Models;
using ReactiveUI;

namespace Atomos.UI.ViewModels;

public class PluginSettingsViewModel : ViewModelBase, IDisposable
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
    
    private readonly IPluginDiscoveryService _pluginDiscoveryService;
    private readonly CompositeDisposable _disposables = new();
    private readonly Dictionary<string, string> _originalValues = new();
    
    private PluginInfo _plugin;
    private bool _hasUnsavedChanges;
    private bool _isLoading;
    private bool _isVisible;

    public PluginInfo Plugin
    {
        get => _plugin;
        private set => this.RaiseAndSetIfChanged(ref _plugin, value);
    }

    public bool HasUnsavedChanges
    {
        get => _hasUnsavedChanges;
        set => this.RaiseAndSetIfChanged(ref _hasUnsavedChanges, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        set => this.RaiseAndSetIfChanged(ref _isLoading, value);
    }

    public bool IsVisible
    {
        get => _isVisible;
        set => this.RaiseAndSetIfChanged(ref _isVisible, value);
    }

    public ObservableCollection<PluginConfigurationItem> ConfigurationItems { get; } = new();
    
    public ReactiveCommand<Unit, Unit> SaveCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }
    public ReactiveCommand<Unit, Unit> ResetToDefaultsCommand { get; }

    public PluginSettingsViewModel(PluginInfo plugin, IPluginDiscoveryService pluginDiscoveryService)
    {
        _plugin = plugin;
        _pluginDiscoveryService = pluginDiscoveryService;

        SaveCommand = ReactiveCommand.CreateFromTask(SaveSettingsAsync);
        CancelCommand = ReactiveCommand.Create(Cancel);
        ResetToDefaultsCommand = ReactiveCommand.CreateFromTask(ResetToDefaultsAsync);
        
        ConfigurationItems.CollectionChanged += (sender, e) =>
        {
            if (e.NewItems != null)
            {
                foreach (PluginConfigurationItem item in e.NewItems)
                {
                    SubscribeToItemChanges(item);
                }
            }
        };
        
        _ = LoadConfigurationAsync();
    }

    private void SubscribeToItemChanges(PluginConfigurationItem item)
    {
        _originalValues[item.Key] = item.Value;
        
        item.WhenAnyValue(x => x.Value)
            .Skip(1)
            .Subscribe(newValue =>
            {
                CheckForUnsavedChanges();
                _logger.Debug("Configuration item '{Key}' changed to '{Value}'", item.Key, newValue);
            })
            .DisposeWith(_disposables);
    }

    private void CheckForUnsavedChanges()
    {
        var hasChanges = ConfigurationItems.Any(item => 
            _originalValues.TryGetValue(item.Key, out var originalValue) && 
            originalValue != item.Value);
        
        if (HasUnsavedChanges != hasChanges)
        {
            HasUnsavedChanges = hasChanges;
            _logger.Debug("Unsaved changes status changed to: {HasChanges}", hasChanges);
        }
    }

    public void Show()
    {
        _logger.Info("Show() called for plugin {PluginId}", Plugin.PluginId);
        IsVisible = true;
        _logger.Info("IsVisible set to true for plugin {PluginId}", Plugin.PluginId);
    }

    public event Action? Closed;

    public void Hide()
    {
        _logger.Info("Hide() called for plugin {PluginId}", Plugin.PluginId);
        IsVisible = false;
        _logger.Info("IsVisible set to false for plugin {PluginId}", Plugin.PluginId);
        Closed?.Invoke();
    }

    private async Task LoadConfigurationAsync()
    {
        try
        {
            IsLoading = true;
            ConfigurationItems.Clear();
            _originalValues.Clear();
            
            var settings = await _pluginDiscoveryService.GetPluginSettingsAsync(Plugin.PluginDirectory);
        
            if (settings?.Configuration?.Any() == true)
            {
                _logger.Debug("Loading configuration from saved settings for plugin {PluginId}", Plugin.PluginId);
            
                foreach (var kvp in settings.Configuration)
                {
                    var configType = DetermineConfigurationType(kvp.Value);
                    var formattedValue = FormatValueForDisplay(kvp.Value, configType);
                
                    var item = new PluginConfigurationItem
                    {
                        Key = kvp.Key,
                        Value = formattedValue,
                        Type = configType,
                        DisplayName = FormatDisplayName(kvp.Key),
                        Description = $"Configuration setting for {kvp.Key}"
                    };
                
                    ConfigurationItems.Add(item);
                    _logger.Debug("Created configuration item for {Key} with type {Type} and value '{Value}'", 
                        kvp.Key, configType, formattedValue);
                }
            }
            else if (Plugin.Configuration?.Any() == true)
            {
                _logger.Debug("Loading configuration from plugin schema for plugin {PluginId}", Plugin.PluginId);
            
                foreach (var kvp in Plugin.Configuration)
                {
                    var configType = DetermineConfigurationType(kvp.Value);
                    var formattedValue = FormatValueForDisplay(kvp.Value, configType);
                
                    var item = new PluginConfigurationItem
                    {
                        Key = kvp.Key,
                        Value = formattedValue,
                        Type = configType,
                        DisplayName = FormatDisplayName(kvp.Key),
                        Description = $"Configuration setting for {kvp.Key}"
                    };
                
                    ConfigurationItems.Add(item);
                    _logger.Debug("Created configuration item for {Key} with type {Type} and value '{Value}'", 
                        kvp.Key, configType, formattedValue);
                }
            }
            else
            {
                _logger.Debug("No configuration schema found in plugin {PluginId}", Plugin.PluginId);
            }

            await Task.Delay(100);
            
            RxApp.MainThreadScheduler.Schedule(() =>
            {
                foreach (var item in ConfigurationItems)
                {
                    _logger.Debug("Added configuration item {Key} with value '{Value}' to UI", item.Key, item.Value);
                }
            });
        
            _logger.Info("Loaded {Count} configuration items for plugin {PluginId}", 
                ConfigurationItems.Count, Plugin.PluginId);
            
            HasUnsavedChanges = false;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load configuration for plugin {PluginId}", Plugin.PluginId);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private ConfigurationType DetermineConfigurationType(object? value)
    {
        return value switch
        {
            bool => ConfigurationType.Boolean,
            int or long or double or float or decimal => ConfigurationType.Number,
            string str when str.Contains('\n') || str.Length > 100 => ConfigurationType.TextArea,
            _ => ConfigurationType.Text
        };
    }

    private string FormatValueForDisplay(object? value, ConfigurationType type)
    {
        if (value == null)
            return string.Empty;
        
        var result = type switch
        {
            ConfigurationType.Boolean => value.ToString()?.ToLowerInvariant() ?? "false",
            ConfigurationType.Number => value.ToString() ?? "0",
            _ => value.ToString() ?? string.Empty
        };
        
        _logger.Debug("Formatted value '{InputValue}' of type {Type} to display value '{DisplayValue}'", 
            value, type, result);
        
        return result;
    }

    private string FormatDisplayName(string key)
    {
        return System.Text.RegularExpressions.Regex.Replace(key, "(\\B[A-Z])", " $1");
    }

    private async Task SaveSettingsAsync()
    {
        try
        {
            IsLoading = true;
            
            var configuration = new Dictionary<string, object>();
            
            foreach (var item in ConfigurationItems)
            {
                var key = item.Key;
                var value = ConvertValueForSaving(item.Value, item.Type);
                configuration[key] = value;
            }
            
            var settings = new PluginSettings
            {
                IsEnabled = Plugin.IsEnabled,
                Configuration = configuration,
                Version = Plugin.Version
            };
            
            await _pluginDiscoveryService.SavePluginSettingsAsync(Plugin.PluginDirectory, settings);
            
            await _pluginDiscoveryService.UpdatePluginConfigurationAsync(Plugin.PluginId, configuration);
            
            foreach (var item in ConfigurationItems)
            {
                _originalValues[item.Key] = item.Value;
            }
            
            HasUnsavedChanges = false;
            Hide();
            
            _logger.Info("Saved {Count} configuration settings for plugin {PluginId}", 
                configuration.Count, Plugin.PluginId);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to save configuration for plugin {PluginId}", Plugin.PluginId);
            throw;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private object ConvertValueForSaving(string value, ConfigurationType type)
    {
        return type switch
        {
            ConfigurationType.Number => int.TryParse(value, out var intVal) ? intVal : 
                                       double.TryParse(value, out var doubleVal) ? doubleVal : 0,
            ConfigurationType.Boolean => bool.TryParse(value, out var boolVal) ? boolVal : false,
            _ => value ?? string.Empty
        };
    }

    private async Task ResetToDefaultsAsync()
    {
        try
        {
            // Reset all items to their default values (empty for now)
            foreach (var item in ConfigurationItems)
            {
                item.Value = string.Empty;
            }
            
            _logger.Info("Reset configuration to defaults for plugin {PluginId}", Plugin.PluginId);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to reset configuration for plugin {PluginId}", Plugin.PluginId);
        }
    }

    public void Cancel()
    {
        // Restore original values
        foreach (var item in ConfigurationItems)
        {
            if (_originalValues.TryGetValue(item.Key, out var originalValue))
            {
                item.Value = originalValue;
            }
        }
        
        HasUnsavedChanges = false;
        Hide();
    }

    public void Dispose()
    {
        _disposables.Dispose();
    }
}