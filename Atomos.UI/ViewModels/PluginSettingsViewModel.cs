using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Atomos.UI.Enums;
using Atomos.UI.Helpers;
using Atomos.UI.Models;
using Atomos.UI.Services;
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

    public PluginSettingsViewModel(
        PluginInfo plugin, 
        IPluginDiscoveryService pluginDiscoveryService)
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
        _originalValues[item.Key] = item.Type switch
        {
            ConfigurationType.MultiSelectEnum => JsonSerializer.Serialize(item.GetActualValue()),
            _ => item.Value
        };
        
        item.WhenAnyValue(x => x.Value)
            .Skip(1)
            .Subscribe(newValue =>
            {
                CheckForUnsavedChanges();
                _logger.Debug("Configuration item '{Key}' changed to '{Value}'", item.Key, newValue);
            })
            .DisposeWith(_disposables);
        
        if (item.Type == ConfigurationType.MultiSelectEnum)
        {
            item.CheckableEnumOptions.CollectionChanged += (sender, e) =>
            {
                CheckForUnsavedChanges();
            };
        
            foreach (var option in item.CheckableEnumOptions)
            {
                option.WhenAnyValue(x => x.IsChecked)
                    .Skip(1)
                    .Subscribe(_ => CheckForUnsavedChanges())
                    .DisposeWith(_disposables);
            }
        }
    }

    private void CheckForUnsavedChanges()
    {
        var hasChanges = ConfigurationItems.Any(item => 
        {
            if (!_originalValues.TryGetValue(item.Key, out var originalValue))
                return false;
            
            return item.Type switch
            {
                ConfigurationType.MultiSelectEnum => 
                    JsonSerializer.Serialize(item.GetActualValue()) != originalValue,
                _ => originalValue != item.Value
            };
        });
    
        HasUnsavedChanges = hasChanges;
    }

    public void Show() => IsVisible = true;
    public void Hide() => IsVisible = false;
    public event Action? Closed;

    private async Task LoadConfigurationAsync()
    {
        try
        {
            IsLoading = true;
            ConfigurationItems.Clear();
            _originalValues.Clear();

            var schema = await PluginSchemaService.GetPluginSchemaAsync(Plugin.PluginDirectory);
            var settings = await _pluginDiscoveryService.GetPluginSettingsAsync(Plugin.PluginDirectory);
            
            var configurationSource = settings?.Configuration?.Any() == true 
                ? settings.Configuration 
                : Plugin.Configuration;

            if (configurationSource?.Any() == true)
            {
                foreach (var kvp in configurationSource)
                {
                    var item = CreateConfigurationItem(kvp.Key, kvp.Value, schema);
                    ConfigurationItems.Add(item);
                }
            }

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

    private PluginConfigurationItem CreateConfigurationItem(string key, object value, JsonDocument? schema)
    {
        var configType = ConfigurationTypeHelper.DetermineConfigurationType(value, schema, key);
        var (displayName, description, enumOptions) = PluginSchemaService.GetSchemaMetadata(key, schema);

        var item = new PluginConfigurationItem
        {
            Key = key,
            Value = value?.ToString() ?? string.Empty,
            Type = configType,
            DisplayName = displayName,
            Description = description,
            EnumOptions = enumOptions
        };
        
        item.SetOriginalValue(value);
        item.InitializeEnumSelection();

        return item;
    }

    private async Task SaveSettingsAsync()
    {
        try
        {
            IsLoading = true;
    
            var configuration = new Dictionary<string, object>();
    
            foreach (var item in ConfigurationItems)
            {
                var value = item.Type switch
                {
                    ConfigurationType.Boolean => item.BooleanValue,
                    ConfigurationType.Number => item.NumberValue,
                    ConfigurationType.MultiSelectEnum => item.GetActualValue(),
                    ConfigurationType.Enum => item.GetActualValue(),
                    _ => item.Value
                };
        
                configuration[item.Key] = value ?? GetFallbackDefaultValue(item.Type);
        
                _logger.Debug("Saving setting: {Key} = {Value} (Type: {Type})", 
                    item.Key, value, item.Type);
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
                _originalValues[item.Key] = item.Type switch
                {
                    ConfigurationType.MultiSelectEnum => JsonSerializer.Serialize(item.GetActualValue()),
                    _ => item.Value
                };
            }
    
            HasUnsavedChanges = false;
            Hide();
            Closed?.Invoke();
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

    private async Task ResetToDefaultsAsync()
    {
        try
        {
            var schema = await PluginSchemaService.GetPluginSchemaAsync(Plugin.PluginDirectory);
        
            foreach (var item in ConfigurationItems)
            {
                var defaultValue = GetDefaultValueFromSchema(item.Key, schema);
                item.Value = defaultValue != null 
                    ? ConfigurationTypeHelper.FormatValueForDisplay(defaultValue, item.Type)
                    : GetFallbackDefaultValueAsString(item.Type);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to reset configuration for plugin {PluginId}", Plugin.PluginId);
        }
    }
    
    private string GetFallbackDefaultValueAsString(ConfigurationType type)
    {
        return type switch
        {
            ConfigurationType.Boolean => "false",
            ConfigurationType.Number => "0",
            ConfigurationType.MultiSelectEnum => "[]",
            ConfigurationType.Enum => string.Empty,
            _ => string.Empty
        };
    }



    private object? GetDefaultValueFromSchema(string key, JsonDocument? schema)
    {
        if (schema?.RootElement.TryGetProperty("properties", out var properties) == true)
        {
            if (properties.TryGetProperty(key, out var propertySchema))
            {
                if (propertySchema.TryGetProperty("default", out var defaultElement))
                {
                    return defaultElement.ValueKind switch
                    {
                        JsonValueKind.String => defaultElement.GetString(),
                        JsonValueKind.Number => defaultElement.GetInt32(),
                        JsonValueKind.True => true,
                        JsonValueKind.False => false,
                        _ => defaultElement.GetRawText()
                    };
                }
            }
        }
        return null;
    }

    private object GetFallbackDefaultValue(ConfigurationType type)
    {
        return type switch
        {
            ConfigurationType.Boolean => false,
            ConfigurationType.Number => 0,
            ConfigurationType.MultiSelectEnum => new int[0],
            ConfigurationType.Enum => null,
            _ => string.Empty
        };
    }


    public void Cancel()
    {
        foreach (var item in ConfigurationItems)
        {
            if (_originalValues.TryGetValue(item.Key, out var originalValue))
            {
                item.Value = originalValue;
            }
        }
        
        HasUnsavedChanges = false;
        Hide();
        Closed?.Invoke();
    }

    public void Dispose()
    {
        _disposables.Dispose();
    }
}