using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Kasir.Avalonia.Infrastructure;

namespace Kasir.Avalonia.Converters;

public class StockColorConverter : IValueConverter
{
    private IBrush? _danger;
    private IBrush? _dim;
    private IBrush? _primary;
    private bool _subscribed;

    private void EnsureBrushes()
    {
        if (_subscribed) return;
        ResolveBrushes();
        if (Application.Current is { } app)
        {
            app.ActualThemeVariantChanged += (_, _) => ResolveBrushes();
        }
        _subscribed = true;
    }

    private void ResolveBrushes()
    {
        if (Application.Current is not { } app) return;
        _danger = ThemeResources.Brush("DangerBrush");
        _dim = ThemeResources.Brush("FgDimBrush");
        _primary = ThemeResources.Brush("FgPrimaryBrush");
    }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        EnsureBrushes();
        if (value is string s && int.TryParse(s, out int stock))
        {
            if (stock < 0)
                return _danger;
            if (stock == 0)
                return _dim;
            return _primary;
        }
        return _primary;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
