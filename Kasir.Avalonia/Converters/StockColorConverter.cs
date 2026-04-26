using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Kasir.Avalonia.Converters;

public class StockColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string s && int.TryParse(s, out int stock))
        {
            if (stock < 0)
                return ThemeConstants.ErrorBrush;
            if (stock == 0)
                return ThemeConstants.DisabledBrush;
            return ThemeConstants.ForegroundBrush;
        }
        return ThemeConstants.ForegroundBrush;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
