using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Atomos.UI.Enums;

namespace Atomos.UI.Converters;

public class EnumToBoolConverter : IValueConverter
{
    public static readonly EnumToBoolConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ConfigurationType enumValue && parameter is ConfigurationType targetValue)
        {
            return enumValue == targetValue;
        }

        // Fallback for string parameter (backward compatibility)
        if (value is ConfigurationType enumVal && parameter is string parameterString)
        {
            if (Enum.TryParse<ConfigurationType>(parameterString, true, out var targetVal))
            {
                return enumVal == targetVal;
            }
        }

        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isTrue && isTrue && parameter is ConfigurationType enumValue)
        {
            return enumValue;
        }

        // Fallback for string parameter
        if (value is bool isTrue2 && isTrue2 && parameter is string parameterString)
        {
            if (Enum.TryParse<ConfigurationType>(parameterString, true, out var enumVal))
            {
                return enumVal;
            }
        }

        return Avalonia.Data.BindingOperations.DoNothing;
    }
}