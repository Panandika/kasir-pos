using System.Drawing;

namespace Kasir.Forms
{
    // Intentionally static — single-theme by design for single-store system.
    // If multi-theme needed later, replace with ITheme interface + singleton.
    static class ThemeConstants
    {
        // Backgrounds
        public static readonly Color BgPrimary = Color.Black;
        public static readonly Color BgPanel = Color.FromArgb(0, 20, 0);
        public static readonly Color BgHeader = Color.FromArgb(0, 40, 0);
        public static readonly Color BgInput = Color.FromArgb(20, 20, 20);
        public static readonly Color BgGridAlt = Color.FromArgb(8, 12, 8);
        public static readonly Color BgSelection = Color.FromArgb(0, 80, 0);
        public static readonly Color BgMenuDrop = Color.FromArgb(0, 30, 0);
        public static readonly Color BgFooter = Color.FromArgb(0, 30, 0);
        public static readonly Color BgDialog = Color.FromArgb(10, 10, 10);

        // Foregrounds
        public static readonly Color FgPrimary = Color.FromArgb(0, 255, 0);
        public static readonly Color FgDimmed = Color.FromArgb(0, 180, 0);
        public static readonly Color FgMuted = Color.FromArgb(0, 150, 0);
        public static readonly Color FgLabel = Color.Gray;
        public static readonly Color FgWhite = Color.White;
        public static readonly Color FgError = Color.FromArgb(255, 80, 80);
        public static readonly Color FgWarning = Color.Yellow;
        public static readonly Color FgSuccess = Color.FromArgb(0, 255, 128);

        // Borders & Lines
        public static readonly Color GridLine = Color.FromArgb(0, 60, 0);

        // Button backgrounds
        public static readonly Color BtnPrimary = Color.FromArgb(0, 80, 0);
        public static readonly Color BtnSecondary = Color.FromArgb(0, 60, 0);
        public static readonly Color BtnDanger = Color.FromArgb(80, 0, 0);

        // Fonts (static readonly — lives for app lifetime, intentionally not disposed)
        public static readonly Font FontMain = new Font("Consolas", 14f);
        public static readonly Font FontGrid = new Font("Consolas", 11f);
        public static readonly Font FontGridHeader = new Font("Consolas", 11f, FontStyle.Bold);
        public static readonly Font FontSmall = new Font("Consolas", 10f);
        public static readonly Font FontMenu = new Font("Consolas", 12f);
        public static readonly Font FontInput = new Font("Consolas", 16f);
        public static readonly Font FontInputSmall = new Font("Consolas", 12f);
        public static readonly Font FontTitle = new Font("Consolas", 24f, FontStyle.Bold);
        public static readonly Font FontSubtotal = new Font("Consolas", 42f, FontStyle.Bold);
        public static readonly Font FontSubtotalLabel = new Font("Consolas", 28f, FontStyle.Bold);
        public static readonly Font FontHeader = new Font("Consolas", 14f, FontStyle.Bold);

        // Sizing
        public const int RowHeight = 28;
        public const int StatusBarHeight = 25;
        public const int HeaderPanelHeight = 70;
    }
}
