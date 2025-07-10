using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Atomos.UI.Enums;
using Atomos.UI.Models;

namespace Atomos.UI.Helpers;

public static class ConfigurationTypeHelper
{
    public static ConfigurationType DetermineConfigurationType(object? value, JsonDocument? schema = null, string? key = null)
    {
        // Check schema first for enum types
        if (schema?.RootElement.TryGetProperty("properties", out var properties) == true && !string.IsNullOrEmpty(key))
        {
            if (properties.TryGetProperty(key, out var propertySchema))
            {
                // Check for regular enum
                if (propertySchema.TryGetProperty("enum", out var enumElement) && enumElement.ValueKind == JsonValueKind.Array)
                {
                    return ConfigurationType.Enum;
                }
                
                // Check for array types with enum items (like ModTypes)
                if (propertySchema.TryGetProperty("type", out var typeElement) && typeElement.GetString() == "array")
                {
                    if (propertySchema.TryGetProperty("items", out var itemsElement) && 
                        itemsElement.TryGetProperty("enum", out _))
                    {
                        return ConfigurationType.MultiSelectEnum;
                    }
                }
            }
        }
        
        return value switch
        {
            bool => ConfigurationType.Boolean,
            JsonElement jsonElement => DetermineConfigurationTypeFromJsonElement(jsonElement),
            string str when bool.TryParse(str, out _) => ConfigurationType.Boolean,
            int or long or double or float or decimal => ConfigurationType.Number,
            string str when double.TryParse(str, out _) => ConfigurationType.Number,
            string str when str.Contains('\n') || str.Length > 100 => ConfigurationType.TextArea,
            System.Collections.IEnumerable enumerable when enumerable is not string => ConfigurationType.TextArea,
            _ => ConfigurationType.Text
        };
    }

    private static ConfigurationType DetermineConfigurationTypeFromJsonElement(JsonElement jsonElement)
    {
        switch (jsonElement.ValueKind)
        {
            case JsonValueKind.True:
            case JsonValueKind.False:
                return ConfigurationType.Boolean;
            
            case JsonValueKind.Number:
                return ConfigurationType.Number;
            
            case JsonValueKind.Array:
                return ConfigurationType.TextArea;
            
            case JsonValueKind.String:
                var stringValue = jsonElement.GetString();
                if (stringValue != null)
                {
                    if (bool.TryParse(stringValue, out _))
                        return ConfigurationType.Boolean;
                    
                    if (double.TryParse(stringValue, out _))
                        return ConfigurationType.Number;
                    
                    if (stringValue.Contains('\n') || stringValue.Length > 100)
                        return ConfigurationType.TextArea;
                }
                return ConfigurationType.Text;
            
            default:
                return ConfigurationType.Text;
        }
    }

    public static string FormatValueForDisplay(object? value, ConfigurationType type, List<EnumOption>? enumOptions = null)
    {
        if (value == null)
            return string.Empty;

        return type switch
        {
            ConfigurationType.Boolean => value.ToString()?.ToLowerInvariant() ?? "false",
            ConfigurationType.Number => value.ToString() ?? "0",
            ConfigurationType.Enum => FormatEnumValueForDisplay(value, enumOptions),
            ConfigurationType.MultiSelectEnum => FormatMultiSelectEnumValueForDisplay(value, enumOptions),
            ConfigurationType.TextArea => FormatArrayOrTextAreaValue(value),
            _ => value.ToString() ?? string.Empty
        };
    }

    private static string FormatEnumValueForDisplay(object? value, List<EnumOption>? enumOptions)
    {
        if (value == null || enumOptions == null || !enumOptions.Any())
            return value?.ToString() ?? string.Empty;

        // Try to find the enum option by value
        var enumOption = enumOptions.FirstOrDefault(e => 
            e.Value?.ToString() == value.ToString());

        if (enumOption != null)
            return enumOption.Title;

        // If not found, try exact match
        enumOption = enumOptions.FirstOrDefault(e => 
            Equals(e.Value, value));

        if (enumOption != null)
            return enumOption.Title;

        // Fallback to raw value
        return value.ToString() ?? string.Empty;
    }

    private static string FormatMultiSelectEnumValueForDisplay(object? value, List<EnumOption>? enumOptions)
    {
        if (value == null || enumOptions == null || !enumOptions.Any())
            return string.Empty;

        var selectedValues = new List<object>();

        // Handle different array formats
        if (value is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in jsonElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Number)
                    selectedValues.Add(item.GetInt32());
                else if (item.ValueKind == JsonValueKind.String)
                    selectedValues.Add(item.GetString()!);
            }
        }
        else if (value is System.Collections.IEnumerable enumerable && value is not string)
        {
            foreach (var item in enumerable)
            {
                if (item != null)
                    selectedValues.Add(item);
            }
        }
        else if (value.ToString() == "[]")
        {
            return string.Empty; // Empty array
        }

        // Convert values to titles
        var selectedTitles = selectedValues
            .Select(val => enumOptions.FirstOrDefault(e => 
                e.Value?.ToString() == val.ToString() || Equals(e.Value, val))?.Title ?? val.ToString())
            .ToList();

        return string.Join(", ", selectedTitles);
    }

    private static string FormatArrayOrTextAreaValue(object? value)
    {
        if (value == null)
            return string.Empty;

        // Handle JsonElement arrays
        if (value is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Array)
        {
            var items = new List<string>();
            foreach (var item in jsonElement.EnumerateArray())
            {
                items.Add(item.GetString() ?? item.ToString() ?? "");
            }
            return string.Join("\n", items);
        }

        // Handle .NET collections
        if (value is System.Collections.IEnumerable enumerable && value is not string)
        {
            var items = new List<string>();
            foreach (var item in enumerable)
            {
                items.Add(item?.ToString() ?? "");
            }
            return string.Join("\n", items);
        }

        // Handle regular strings
        return value.ToString() ?? string.Empty;
    }

    public static object ConvertValueForSaving(string value, ConfigurationType type, List<EnumOption>? enumOptions = null)
    {
        return type switch
        {
            ConfigurationType.Number => ConvertNumberValue(value),
            ConfigurationType.Boolean => bool.TryParse(value, out var boolVal) ? boolVal : false,
            ConfigurationType.Enum => ConvertEnumValueForSaving(value, enumOptions),
            ConfigurationType.MultiSelectEnum => ConvertMultiSelectEnumValueForSaving(value, enumOptions),
            ConfigurationType.TextArea => ConvertTextAreaValue(value),
            _ => value ?? string.Empty
        };
    }

    private static object ConvertNumberValue(string value)
    {
        if (string.IsNullOrEmpty(value))
            return 0;

        // Try int first, then double
        if (int.TryParse(value, out var intVal))
            return intVal;
        
        if (double.TryParse(value, out var doubleVal))
            return doubleVal;
        
        return 0;
    }

    private static object ConvertTextAreaValue(string value)
    {
        if (string.IsNullOrEmpty(value))
            return new List<string>();

        // If it contains newlines, treat as array
        if (value.Contains('\n'))
        {
            return value.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
        }

        // Otherwise, return as single string
        return value;
    }

    private static object ConvertEnumValueForSaving(string displayValue, List<EnumOption>? enumOptions)
    {
        if (enumOptions == null || !enumOptions.Any())
            return displayValue;
        
        // The displayValue is the user-friendly title, we need to find the corresponding enum value
        var enumOption = enumOptions.FirstOrDefault(e => e.Title == displayValue);
        if (enumOption != null)
        {
            return enumOption.Value; // Return the actual enum value
        }
        
        // Try to find by value (fallback)
        enumOption = enumOptions.FirstOrDefault(e => e.Value?.ToString() == displayValue);
        if (enumOption != null)
        {
            return enumOption.Value;
        }
        
        // If we can't find it, return the displayValue as-is
        return displayValue;
    }

    private static object ConvertMultiSelectEnumValueForSaving(string displayValue, List<EnumOption>? enumOptions)
    {
        if (enumOptions == null || !enumOptions.Any())
            return new List<object>();

        if (string.IsNullOrEmpty(displayValue))
            return new List<object>();

        // Parse the comma-separated display titles
        var displayTitles = displayValue.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .ToList();

        var selectedValues = new List<object>();
        
        foreach (var title in displayTitles)
        {
            var enumOption = enumOptions.FirstOrDefault(e => e.Title == title);
            if (enumOption != null)
            {
                selectedValues.Add(enumOption.Value);
            }
        }

        return selectedValues;
    }
}