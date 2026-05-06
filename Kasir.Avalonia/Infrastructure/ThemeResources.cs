using Avalonia;
using Avalonia.Media;

namespace Kasir.Avalonia.Infrastructure;

// Variant-aware resource resolution. Application.Current.FindResource(key) returns
// UnsetValue for keys living inside ResourceDictionary.ThemeDictionaries; the
// variant-aware overload via TryGetResource resolves both shared and themed scopes.
public static class ThemeResources
{
    public static IBrush? Brush(string key)
    {
        var app = Application.Current;
        if (app == null) return null;
        if (app.TryGetResource(key, app.ActualThemeVariant, out var val) && val is IBrush b) return b;
        return null;
    }

    public static T? Resource<T>(string key) where T : class
    {
        var app = Application.Current;
        if (app == null) return null;
        if (app.TryGetResource(key, app.ActualThemeVariant, out var val) && val is T t) return t;
        return null;
    }

    public static double Number(string key, double fallback)
    {
        var app = Application.Current;
        if (app == null) return fallback;
        if (app.TryGetResource(key, app.ActualThemeVariant, out var val) && val is double d) return d;
        return fallback;
    }
}
