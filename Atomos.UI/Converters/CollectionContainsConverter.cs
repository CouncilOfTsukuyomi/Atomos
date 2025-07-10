using System;
using System.Collections;
using System.Globalization;
using Avalonia.Data.Converters;
using Atomos.UI.Models;

namespace Atomos.UI.Converters;

public class CollectionContainsConverter : IValueConverter
{
    public static readonly CollectionContainsConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not IEnumerable collection || parameter is not EnumOption enumOption)
            return false;
        
        foreach (var item in collection)
        {
            if (item is EnumOption option && AreEqual(option, enumOption))
                return true;
        }

        return false;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException("CollectionContainsConverter is one-way only");
    }

    private static bool AreEqual(EnumOption option1, EnumOption option2)
    {
        return option1.Value?.ToString() == option2.Value?.ToString() && 
               option1.Title == option2.Title;
    }
}