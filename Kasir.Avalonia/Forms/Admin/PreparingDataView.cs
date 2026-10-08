using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Kasir.Avalonia.Infrastructure;

namespace Kasir.Avalonia.Forms.Admin;

// Busy screen shown at startup while the database is put in place (a cloud import copies
// ~190 MB and runs migrations). It has nothing to click, so the operator cannot pick a
// first-run choice again while the data is being prepared.
public class PreparingDataView : UserControl
{
    public PreparingDataView()
    {
        Background = ThemeResources.Brush("Bg0Brush");
        var font = new global::Avalonia.Media.FontFamily(ThemeConstants.FontFamily);
        Content = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Width = 520,
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = "Menyiapkan data toko…",
                    FontFamily = font,
                    FontSize = 22,
                    FontWeight = FontWeight.Bold,
                    Foreground = ThemeResources.Brush("FgPrimaryBrush"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                },
                new ProgressBar
                {
                    IsIndeterminate = true,
                    Height = 6,
                },
                new TextBlock
                {
                    Text = "Mohon tunggu, bisa beberapa menit. Jangan matikan komputer.\nLayar login akan muncul sendiri setelah selesai.",
                    FontFamily = font,
                    FontSize = ThemeConstants.FontSize,
                    Foreground = ThemeResources.Brush("FgSecondaryBrush"),
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                },
            },
        };
    }
}
