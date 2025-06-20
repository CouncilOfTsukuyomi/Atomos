using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Atomos.UI.Converters;

public class StringToBoolConverter : IValueConverter
{
    public static readonly StringToBoolConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string str)
        {
            if (bool.TryParse(str, out var boolValue))
                return boolValue;
                
            return !string.IsNullOrEmpty(str);
        }
        
        return false;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // Only convert if we're actually dealing with a boolean
        if (value is bool boolValue)
        {
            return boolValue.ToString().ToLowerInvariant();
        }
        
        // For anything else, return as-is
        return value?.ToString() ?? "false";
    }
}