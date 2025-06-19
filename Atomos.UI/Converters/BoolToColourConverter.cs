using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Atomos.UI.Converters;

public class BoolToColorConverter : IValueConverter
{
    public static readonly BoolToColorConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue && parameter is string colorParams)
        {
            var colors = colorParams.Split('|');
            if (colors.Length == 2)
            {
                var colorString = boolValue ? colors[0] : colors[1];
                if (Color.TryParse(colorString, out var color))
                {
                    return color;
                }
            }
        }
        
        return Colors.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}