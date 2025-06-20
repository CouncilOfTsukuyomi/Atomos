using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Atomos.UI.Converters;

public class BoolToColourConverter : IValueConverter
{
    public static readonly BoolToColourConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue && parameter is string colorParameter)
        {
            // Parameter format: "TrueColour|FalseColor" (e.g., "#4CAF50|#FF9800")
            var colours = colorParameter.Split('|');
            if (colours.Length == 2)
            {
                var targetColor = boolValue ? colours[0] : colours[1];
                
                if (Color.TryParse(targetColor, out var color))
                {
                    return color;
                }
            }
        }

        // Default fallback colours
        return value is bool b && b ? Colors.Green : Colors.Orange;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}