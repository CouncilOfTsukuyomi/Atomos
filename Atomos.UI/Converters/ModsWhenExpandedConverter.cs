using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Data.Converters;

namespace Atomos.UI.Converters;

// Returns the mods list only when the card is expanded; otherwise returns null so the ItemsControl clears its children
public class ModsWhenExpandedConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values == null || values.Count < 2)
            return null;

        var modsObj = values[0];
        var isExpanded = values[1] as bool?;

        if (isExpanded == true)
        {
            // Optional limit via ConverterParameter (e.g., "50")
            int limit = int.MaxValue;
            if (parameter is string s && int.TryParse(s, out var parsed))
                limit = parsed;

            if (modsObj is IEnumerable enumerable)
            {
                var list = enumerable.Cast<object>().Take(limit).ToList();
                return list;
            }
            return modsObj;
        }

        // When collapsed: returning null releases generated item containers and heavy image controls
        return null;
    }
}