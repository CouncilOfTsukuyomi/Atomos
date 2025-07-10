using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Atomos.UI.Enums;
using Atomos.UI.ViewModels;
using ReactiveUI;
using NLog;
using System.Text.Json;

namespace Atomos.UI.Models;

public class PluginConfigurationItem : ViewModelBase
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
    
    private string _value = string.Empty;
    private EnumOption? _selectedEnumOption;
    private ObservableCollection<CheckableEnumOption> _checkableEnumOptions = new();
    private object? _originalValue;
    
    public string Key { get; set; } = string.Empty;
    
    public string Value
    {
        get => _value;
        set => this.RaiseAndSetIfChanged(ref _value, value);
    }
    
    public EnumOption? SelectedEnumOption
    {
        get => _selectedEnumOption;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedEnumOption, value);
            if (value != null)
            {
                Value = value.Value?.ToString() ?? "";
            }
        }
    }
    
    public ObservableCollection<CheckableEnumOption> CheckableEnumOptions
    {
        get => _checkableEnumOptions;
        set => this.RaiseAndSetIfChanged(ref _checkableEnumOptions, value);
    }
    
    public string SelectedOptionsText
    {
        get
        {
            if (Type != ConfigurationType.MultiSelectEnum || !_checkableEnumOptions.Any())
                return "Select options...";
            
            var selectedOptions = _checkableEnumOptions
                .Where(o => o.IsChecked)
                .Select(o => o.EnumOption.Title)
                .ToList();
            
            return selectedOptions.Count switch
            {
                0 => "None selected",
                1 => selectedOptions.First(),
                <= 3 => string.Join(", ", selectedOptions),
                _ => $"{selectedOptions.Count} options selected"
            };
        }
    }
    
    public void SetOriginalValue(object? originalValue)
    {
        _originalValue = originalValue;
        _logger.Debug("Setting original value for {Key}: {Value} (Type: {Type})", Key, originalValue, originalValue?.GetType().Name);
    }
    
    public object? GetActualValue()
    {
        return Type switch
        {
            ConfigurationType.Boolean => BooleanValue,
            ConfigurationType.Number => NumberValue,
            ConfigurationType.MultiSelectEnum => GetSelectedEnumValuesAsArray(),
            ConfigurationType.Enum => GetSelectedEnumValue(),
            _ => Value
        };
    }
    
    private object? GetSelectedEnumValue()
    {
        return _selectedEnumOption?.Value;
    }
    
    private int[] GetSelectedEnumValuesAsArray()
    {
        return _checkableEnumOptions
            .Where(o => o.IsChecked)
            .Select(o => o.EnumOption.Value)
            .Cast<int>()
            .ToArray();
    }
    
    private void UpdateSelectedEnumOption()
    {
        if (EnumOptions == null || !EnumOptions.Any())
            return;
        
        var enumOption = EnumOptions.FirstOrDefault(e => 
            e.Value?.ToString() == _value ||
            Equals(e.Value, _value) ||
            e.Title == _value);

        if (enumOption == null && int.TryParse(_value, out var intValue))
        {
            enumOption = EnumOptions.FirstOrDefault(e => 
                e.Value is int intVal && intVal == intValue);
        }

        if (enumOption != _selectedEnumOption)
        {
            _selectedEnumOption = enumOption;
            this.RaisePropertyChanged(nameof(SelectedEnumOption));
        }
    }
    
    private void UpdateCheckableEnumOptions()
    {
        if (EnumOptions == null || !EnumOptions.Any())
            return;
        
        _logger.Debug("Updating checkable enum options for {Key}. Original value: {Value} (Type: {Type})", 
            Key, _originalValue, _originalValue?.GetType().Name);
        
        foreach (var option in _checkableEnumOptions)
        {
            option.PropertyChanged -= OnCheckableOptionChanged;
        }
        _checkableEnumOptions.Clear();
        
        var selectedValues = new List<object>();
        
        if (_originalValue is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in jsonElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Number)
                {
                    selectedValues.Add(item.GetInt32());
                }
                else if (item.ValueKind == JsonValueKind.String)
                {
                    selectedValues.Add(item.GetString() ?? "");
                }
            }
            _logger.Debug("Parsed JSON array: {Values}", string.Join(",", selectedValues));
        }
        
        foreach (var enumOption in EnumOptions)
        {
            var isChecked = selectedValues.Contains(enumOption.Value) || 
                           selectedValues.Contains(enumOption.Value?.ToString());
            
            var checkableOption = new CheckableEnumOption(enumOption)
            {
                IsChecked = isChecked
            };
            checkableOption.PropertyChanged += OnCheckableOptionChanged;
            _checkableEnumOptions.Add(checkableOption);
            
            _logger.Debug("Created checkable option: {Title} (Value: {Value}, Checked: {IsChecked})", 
                enumOption.Title, enumOption.Value, isChecked);
        }
        
        this.RaisePropertyChanged(nameof(CheckableEnumOptions));
        this.RaisePropertyChanged(nameof(SelectedOptionsText));
    }
    
    private void OnCheckableOptionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CheckableEnumOption.IsChecked))
        {
            _logger.Debug("Checkable option changed for {Key}", Key);
            UpdateValueFromCheckableOptions();
            this.RaisePropertyChanged(nameof(SelectedOptionsText));
        }
    }
    
    private void UpdateValueFromCheckableOptions()
    {
        var selectedValues = _checkableEnumOptions
            .Where(o => o.IsChecked)
            .Select(o => o.EnumOption.Value)
            .ToList();

        var displayTitles = _checkableEnumOptions
            .Where(o => o.IsChecked)
            .Select(o => o.EnumOption.Title)
            .ToList();
        
        Value = displayTitles.Count == 0 
            ? "None selected" 
            : string.Join(", ", displayTitles);
            
        _logger.Debug("Updated value for {Key}: {Value} (Selected values: {SelectedValues})", 
            Key, Value, string.Join(",", selectedValues));
    }
    
    public void InitializeEnumSelection()
    {
        if (IsEnum && EnumOptions != null)
        {
            UpdateSelectedEnumOption();
        }
        else if (IsMultiSelectEnum && EnumOptions != null)
        {
            UpdateCheckableEnumOptions();
        }
    }
    
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
    
    public List<EnumOption>? EnumOptions { get; set; }
    public bool IsEnum => Type == ConfigurationType.Enum;
    public bool IsMultiSelectEnum => Type == ConfigurationType.MultiSelectEnum;
}

public class EnumOption
{
    public object Value { get; set; }
    public string Title { get; set; }
    
    public EnumOption(object value, string title)
    {
        Value = value;
        Title = title;
    }
    
    public override string ToString() => Title;
}

public class CheckableEnumOption : ViewModelBase
{
    private bool _isChecked;
    
    public EnumOption EnumOption { get; }
    
    public bool IsChecked
    {
        get => _isChecked;
        set => this.RaiseAndSetIfChanged(ref _isChecked, value);
    }
    
    public CheckableEnumOption(EnumOption enumOption)
    {
        EnumOption = enumOption;
    }
}