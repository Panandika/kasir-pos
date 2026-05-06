using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Media;
using Avalonia.Styling;
using NUnit.Framework;

namespace KasirAvaloniaTests;

public class ThemeTokenTests
{
    private static readonly string[] BrushKeys =
    {
        "Bg0Brush", "Bg1Brush", "Bg2Brush", "BgHoverBrush", "BgSelectedBrush",
        "AccentBgBrush", "BorderSubtleBrush", "BorderStrongBrush",
        "FgPrimaryBrush", "FgSecondaryBrush", "FgDimBrush", "FgNumericBrush", "FgOnBrandBrush",
        "BrandBrush", "BrandStrongBrush", "BrandSoftBrush",
        "SuccessBrush", "WarningBrush", "DangerBrush", "FocusRingBrush"
    };

    private static object? Resolve(string key, ThemeVariant variant)
    {
        // Variant-aware ResourceNodeExtensions.FindResource overload —
        // resolves against ThemeDictionaries for the requested variant
        // independent of ActualThemeVariant.
        return Application.Current!.FindResource(variant, key);
    }

    [AvaloniaTest]
    public void All_20_tokens_resolve_in_dark_variant()
    {
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        foreach (var key in BrushKeys)
        {
            var brush = Resolve(key, ThemeVariant.Dark);
            Assert.That(brush, Is.Not.Null, $"Dark variant: {key} did not resolve");
            Assert.That(brush, Is.InstanceOf<IBrush>(), $"Dark variant: {key} is not IBrush");
        }
    }

    [AvaloniaTest]
    public void All_20_tokens_resolve_in_light_variant()
    {
        Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
        foreach (var key in BrushKeys)
        {
            var brush = Resolve(key, ThemeVariant.Light);
            Assert.That(brush, Is.Not.Null, $"Light variant: {key} did not resolve");
            Assert.That(brush, Is.InstanceOf<IBrush>(), $"Light variant: {key} is not IBrush");
        }
    }

    [AvaloniaTest]
    public void Theme_toggle_changes_actual_theme_variant()
    {
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        Assert.That(Application.Current.ActualThemeVariant, Is.EqualTo(ThemeVariant.Dark));
        Application.Current.RequestedThemeVariant = ThemeVariant.Light;
        Assert.That(Application.Current.ActualThemeVariant, Is.EqualTo(ThemeVariant.Light));
    }

    [AvaloniaTest]
    public void Brand_color_differs_between_variants()
    {
        var darkBrand = Resolve("BrandBrush", ThemeVariant.Dark) as ISolidColorBrush;
        var lightBrand = Resolve("BrandBrush", ThemeVariant.Light) as ISolidColorBrush;
        Assert.That(darkBrand, Is.Not.Null);
        Assert.That(lightBrand, Is.Not.Null);
        Assert.That(darkBrand!.Color, Is.Not.EqualTo(lightBrand!.Color),
            "Dark brand color should differ from light brand color");
    }
}
