using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Atomos.UI.Converters;

public class StringToNumberConverter : IValueConverter
{
    public static readonly StringToNumberConverter Instance = new();
    
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string stringValue && double.TryParse(stringValue, out var result))
        {
            return result;
        }
        
        return 0.0;
    }
    
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // Just return the string representation - don't modify other types
        return value?.ToString() ?? "0";
    }
}