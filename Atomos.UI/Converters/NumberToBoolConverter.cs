using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Atomos.UI.Converters;

public class NumberToBoolConverter : IValueConverter
{
    public static readonly NumberToBoolConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int intValue && parameter is string paramStr && int.TryParse(paramStr, out var compareValue))
        {
            return intValue == compareValue;
        }

        if (value is double doubleValue && parameter is string paramStr2 && double.TryParse(paramStr2, out var compareValue2))
        {
            return Math.Abs(doubleValue - compareValue2) < 0.001;
        }

        return false;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}