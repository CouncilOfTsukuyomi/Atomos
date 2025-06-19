using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Atomos.UI.Converters;

public class BoolToStringConverter : IValueConverter
{
    public static readonly BoolToStringConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue && parameter is string stringParams)
        {
            var strings = stringParams.Split('|');
            if (strings.Length == 2)
            {
                return boolValue ? strings[0] : strings[1];
            }
        }
        
        return value?.ToString() ?? string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string stringValue && parameter is string stringParams)
        {
            var strings = stringParams.Split('|');
            if (strings.Length == 2)
            {
                // Return true if the string matches the "true" value (first string)
                return stringValue == strings[0];
            }
        }
        
        // Try to parse as boolean if no parameter
        if (value is string str)
        {
            if (bool.TryParse(str, out bool result))
            {
                return result;
            }
        }
        
        return false; // Default fallback
    }
}