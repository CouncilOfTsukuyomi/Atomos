using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Atomos.UI.Converters;

public class MenuItemNameConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string label)
        {
            return $"{label.Replace(" ", "")}MenuItem";
        }
        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}