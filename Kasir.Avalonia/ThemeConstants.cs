using Avalonia.Media;

namespace Kasir.Avalonia;

// Token values mirror BaseTheme.axaml semantic tokens (DESIGN-SYSTEM.md §5.1).
// Keep in sync with Themes/BaseTheme.axaml whenever colors change.
public static class ThemeConstants
{
    // Background surfaces
    public static readonly Color BackgroundColor = Color.FromRgb(0, 0, 0);         // Bg0
    public static readonly Color InputBackColor = Color.FromRgb(0, 20, 0);         // Bg1
    public static readonly Color StatusBarColor = Color.FromRgb(0, 40, 0);         // Bg2
    public static readonly Color FocusedInputBackColor = Color.FromRgb(0, 56, 0);  // BgHover
    public static readonly Color SelectionBackColor = Color.FromRgb(0, 68, 0);     // BgSelected
    public static readonly Color ErrorBackColor = Color.FromRgb(68, 0, 0);         // AccentBg (danger bg)

    // Border tokens
    public static readonly Color BorderColor = Color.FromRgb(0, 160, 0);           // BorderSubtle
    public static readonly Color DisabledColor = Color.FromRgb(0, 100, 0);         // BorderStrong / FgDim

    // Foreground tokens
    public static readonly Color ForegroundColor = Color.FromRgb(0, 220, 0);       // FgPrimary
    public static readonly Color HighlightColor = Color.FromRgb(255, 200, 0);      // FgNumeric / Accent
    public static readonly Color HeaderColor = Color.FromRgb(0, 136, 0);           // FgSecondary
    public static readonly Color SelectionForeColor = Color.FromRgb(0, 100, 0);    // FgDim

    // Semantic state tokens
    public static readonly Color AccentColor = Color.FromRgb(255, 200, 0);         // Accent
    public static readonly Color SuccessColor = Color.FromRgb(0, 220, 0);          // Success
    public static readonly Color WarningColor = Color.FromRgb(255, 153, 0);        // Warning
    public static readonly Color ErrorColor = Color.FromRgb(255, 80, 80);          // Danger
    public static readonly Color FocusRingColor = Color.FromRgb(0, 220, 0);        // FocusRing

    // Brushes (same field names as before — code-behind uses these)
    public static readonly IBrush BackgroundBrush = new SolidColorBrush(BackgroundColor);
    public static readonly IBrush ForegroundBrush = new SolidColorBrush(ForegroundColor);
    public static readonly IBrush HighlightBrush = new SolidColorBrush(HighlightColor);
    public static readonly IBrush HeaderBrush = new SolidColorBrush(HeaderColor);
    public static readonly IBrush DisabledBrush = new SolidColorBrush(DisabledColor);
    public static readonly IBrush ErrorBrush = new SolidColorBrush(ErrorColor);
    public static readonly IBrush WarningBrush = new SolidColorBrush(WarningColor);
    public static readonly IBrush StatusBarBrush = new SolidColorBrush(StatusBarColor);
    public static readonly IBrush BorderBrush = new SolidColorBrush(BorderColor);
    public static readonly IBrush SelectionBackBrush = new SolidColorBrush(SelectionBackColor);
    public static readonly IBrush SelectionForeBrush = new SolidColorBrush(SelectionForeColor);
    public static readonly IBrush InputBackBrush = new SolidColorBrush(InputBackColor);
    public static readonly IBrush FocusedInputBackBrush = new SolidColorBrush(FocusedInputBackColor);

    public const string FontFamily = "Consolas,Cascadia Mono,Liberation Mono,DejaVu Sans Mono,monospace";
    public const double FontSize = 13;
    public const double HeaderFontSize = 14;
    public const double StatusFontSize = 12;
}
