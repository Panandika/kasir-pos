using Avalonia.Media;

namespace Kasir.Avalonia;

public static class ThemeConstants
{
    public static readonly Color BackgroundColor = Color.FromRgb(0, 0, 0);
    public static readonly Color ForegroundColor = Color.FromRgb(0, 220, 0);
    public static readonly Color HighlightColor = Color.FromRgb(0, 180, 0);
    public static readonly Color HeaderColor = Color.FromRgb(0, 255, 0);
    public static readonly Color DisabledColor = Color.FromRgb(0, 100, 0);
    public static readonly Color ErrorColor = Color.FromRgb(255, 80, 80);
    public static readonly Color WarningColor = Color.FromRgb(255, 200, 0);
    public static readonly Color StatusBarColor = Color.FromRgb(0, 40, 0);
    public static readonly Color BorderColor = Color.FromRgb(0, 160, 0);
    public static readonly Color SelectionBackColor = Color.FromRgb(0, 100, 0);
    public static readonly Color SelectionForeColor = Color.FromRgb(0, 255, 0);
    public static readonly Color InputBackColor = Color.FromRgb(0, 20, 0);
    public static readonly Color FocusedInputBackColor = Color.FromRgb(0, 40, 0);

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
