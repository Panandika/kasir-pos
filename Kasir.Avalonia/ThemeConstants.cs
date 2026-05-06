namespace Kasir.Avalonia;

// DS-V2 migration (P5a): all Color/IBrush static fields were removed.
// Themed colors now resolve via {DynamicResource X} in AXAML or
// Application.Current.FindResource("X") in code-behind. See BaseTheme.axaml.
// Only non-color literals (font family, sizes) remain here.
public static class ThemeConstants
{
    public const string FontFamily = "Inter,Segoe UI,system-ui,sans-serif";
    public const double FontSize = 14;
    public const double HeaderFontSize = 15;
    public const double StatusFontSize = 12;
}
