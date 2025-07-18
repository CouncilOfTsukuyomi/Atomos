using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Atomos.UI.Converters;

public class PropertyNameToSettingNameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string propertyName && !string.IsNullOrEmpty(propertyName))
        {
            return $"{propertyName}Setting";
        }
        return "UnknownSetting";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}