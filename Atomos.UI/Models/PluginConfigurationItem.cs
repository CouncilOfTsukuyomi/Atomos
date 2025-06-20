using Atomos.UI.Enums;
using Atomos.UI.ViewModels;
using ReactiveUI;
using NLog;

namespace Atomos.UI.Models;

public class PluginConfigurationItem : ViewModelBase
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
    
    private string _value = string.Empty;
    
    public string Key { get; set; } = string.Empty;
    
    public string Value
    {
        get => _value;
        set 
        {
            var oldValue = _value;
            this.RaiseAndSetIfChanged(ref _value, value);
            
            if (oldValue != value)
            {
                _logger.Debug("PluginConfigurationItem value changed: Key='{Key}', Type={Type}, OldValue='{OldValue}' -> NewValue='{NewValue}'", 
                    Key, Type, oldValue, value);
            }
        }
    }
    
    // Separate properties for different control types
    public bool BooleanValue
    {
        get => bool.TryParse(Value, out var result) ? result : false;
        set => Value = value.ToString().ToLowerInvariant();
    }
    
    public double NumberValue
    {
        get => double.TryParse(Value, out var result) ? result : 0.0;
        set => Value = value.ToString();
    }
    
    public ConfigurationType Type { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string[]? Choices { get; set; }
}