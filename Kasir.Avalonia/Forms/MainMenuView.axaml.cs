using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Utils;
using Kasir.Avalonia.Navigation;
using Kasir.Avalonia.Utils;
using Kasir.Auth;
using Kasir.Services;
using Kasir.Avalonia.Forms.Master;
using Kasir.Avalonia.Forms.Admin;
using Kasir.Avalonia.Forms.POS;
using Kasir.Avalonia.Forms.Purchasing;
using Kasir.Avalonia.Forms.Inventory;
using Kasir.Avalonia.Forms.Accounting;
using Kasir.Avalonia.Forms.Bank;
using Kasir.Avalonia.Forms.Reports;

namespace Kasir.Avalonia.Forms;

public partial class MainMenuView : UserControl, INavigationAware
{
    private readonly int _userId;
    private readonly ConfigRepository _configRepo;
    private readonly SaleRepository _saleRepo;
    private readonly ShiftRepository _shiftRepo;

    private enum Level { Main, SubMenu }
    private Level _level = Level.Main;
    private string? _openCategory;
    private TopLevel? _registeredTopLevel;
    private string? _updateBadgeVersion;

    public MainMenuView(int userId = 1)
    {
        InitializeComponent();
        _userId = userId;
        var conn = DbConnection.GetConnection();
        _configRepo = new ConfigRepository(conn);
        _saleRepo = new SaleRepository(conn);
        _shiftRepo = new ShiftRepository(conn);

        LblStoreName.Text = _configRepo.Get("store_name") ?? "KASIR POS";
        LblGreeting.Text = $"Selamat datang — {DateTime.Now:dddd, dd MMMM yyyy}";

        FooterStatus.RegisterDefault(StatusLabel, "F12=Sync  Esc=Kembali  F10=Menu");
        ShowMainTiles();
        RefreshStatus();
        _ = CheckForUpdateAsync();
    }

    public void OnNavigatedTo()
    {
        RefreshStatus();
        // Re-focus a tile after GoBack so OnKeyDown/OnPreviewKeyDown receive keys
        // without the user having to click. Deferred to Background so the visual
        // tree is settled after NavigationService swaps content.
        Dispatcher.UIThread.Post(() =>
        {
            if (BentoGrid.Children.Count > 0 && BentoGrid.Children[0] is Button b) b.Focus();
        }, DispatcherPriority.Background);
    }

    // ── Tile models ───────────────────────────────────────────────────────

    private sealed class TileSpec
    {
        public string Label { get; init; } = "";
        public int UnderlineIndex { get; init; }
        public Key Hotkey { get; init; }
        public Action Activate { get; init; } = () => { };
        public bool IsDanger { get; init; }
    }

    private IReadOnlyList<TileSpec> MainTiles() => new[]
    {
        new TileSpec { Label = "Master",    UnderlineIndex = 0, Hotkey = Key.M, Activate = () => DrillInto("Master") },
        new TileSpec { Label = "Transaksi", UnderlineIndex = 0, Hotkey = Key.T, Activate = () => DrillInto("Transaksi") },
        new TileSpec { Label = "Akuntansi", UnderlineIndex = 1, Hotkey = Key.K, Activate = () => DrillInto("Akuntansi") },
        new TileSpec { Label = "Laporan",   UnderlineIndex = 0, Hotkey = Key.L, Activate = () => DrillInto("Laporan") },
        new TileSpec { Label = "Bank",      UnderlineIndex = 0, Hotkey = Key.B, Activate = () => DrillInto("Bank") },
        new TileSpec { Label = "Utility",   UnderlineIndex = 0, Hotkey = Key.U, Activate = () => DrillInto("Utility") },
        new TileSpec { Label = "Keluar",    UnderlineIndex = 1, Hotkey = Key.E, IsDanger = true,
                       Activate = () => NavigationService.ReplaceRoot(new LoginView()) },
    };

    private IReadOnlyList<TileSpec> SubTiles(string category) => category switch
    {
        "Master" => new[]
        {
            new TileSpec { Label = "Departemen",       UnderlineIndex = 0, Hotkey = Key.D, Activate = () => NavigationService.Navigate(new DepartmentView(_userId)) },
            new TileSpec { Label = "Supplier",         UnderlineIndex = 0, Hotkey = Key.S, Activate = () => NavigationService.Navigate(new VendorView(_userId)) },
            new TileSpec { Label = "Barang",           UnderlineIndex = 0, Hotkey = Key.B, Activate = () => NavigationService.Navigate(new ProductView(_userId)) },
            new TileSpec { Label = "Credit Card",      UnderlineIndex = 0, Hotkey = Key.C, Activate = () => NavigationService.Navigate(new CreditCardView(_userId)) },
            new TileSpec { Label = "Ganti Harga Jual", UnderlineIndex = 0, Hotkey = Key.G, Activate = () => NavigationService.Navigate(new PriceChangeView()) },
            new TileSpec { Label = "Stok Opname",      UnderlineIndex = 5, Hotkey = Key.O, Activate = () => NavigationService.Navigate(new OpnameView()) },
        },
        "Transaksi" => new[]
        {
            new TileSpec { Label = "Pemesanan/Order",        UnderlineIndex = 10, Hotkey = Key.O, Activate = () => NavigationService.Navigate(new PurchaseOrderView()) },
            new TileSpec { Label = "Penerimaan Barang",      UnderlineIndex = 1,  Hotkey = Key.E, Activate = () => NavigationService.Navigate(new GoodsReceiptView()) },
            new TileSpec { Label = "Nota Pembelian",         UnderlineIndex = 0,  Hotkey = Key.N, Activate = () => NavigationService.Navigate(new PurchaseInvoiceView()) },
            new TileSpec { Label = "Hutang",                 UnderlineIndex = 0,  Hotkey = Key.H, Activate = () => NavigationService.Navigate(new PayablesView()) },
            new TileSpec { Label = "Retur Pembelian",        UnderlineIndex = 0,  Hotkey = Key.R, Activate = () => NavigationService.Navigate(new ReturnView()) },
            new TileSpec { Label = "Pemakaian/Rusak/Hilang", UnderlineIndex = 3,  Hotkey = Key.A, Activate = () => NavigationService.Navigate(new StockOutView()) },
            new TileSpec { Label = "Penjualan",              UnderlineIndex = 0,  Hotkey = Key.P, Activate = () => NavigationService.Navigate(new SaleView(new AuthService(DbConnection.GetConnection()))) },
            new TileSpec { Label = "Transfer",               UnderlineIndex = 0,  Hotkey = Key.T, Activate = () => NavigationService.Navigate(new TransferView()) },
        },
        "Akuntansi" => new[]
        {
            new TileSpec { Label = "Daftar Perkiraan", UnderlineIndex = 0, Hotkey = Key.D, Activate = () => NavigationService.Navigate(new AccountsView()) },
            new TileSpec { Label = "Jurnal Memorial",  UnderlineIndex = 0, Hotkey = Key.J, Activate = () => NavigationService.Navigate(new JournalView(userId: _userId)) },
            new TileSpec { Label = "Penerimaan Kas",   UnderlineIndex = 11, Hotkey = Key.K, Activate = () => NavigationService.Navigate(new CashReceiptView(userId: _userId)) },
            new TileSpec { Label = "Pengeluaran Kas",  UnderlineIndex = 0,  Hotkey = Key.P, Activate = () => NavigationService.Navigate(new CashDisbursementView(userId: _userId)) },
            new TileSpec { Label = "Proses Posting",   UnderlineIndex = 2,  Hotkey = Key.O, Activate = () => NavigationService.Navigate(new PostingProgressView()) },
        },
        "Laporan" => new[]
        {
            new TileSpec { Label = "Cetak Master Barang",   UnderlineIndex = 13, Hotkey = Key.B, Activate = () => NavigationService.Navigate(new ProductReportView()) },
            new TileSpec { Label = "Cetak Master Supplier", UnderlineIndex = 13, Hotkey = Key.S, Activate = () => NavigationService.Navigate(new SupplierReportView()) },
            new TileSpec { Label = "Penjualan",             UnderlineIndex = 3,  Hotkey = Key.J, Activate = () => NavigationService.Navigate(new SalesReportView()) },
            new TileSpec { Label = "Stok Barang",           UnderlineIndex = 2,  Hotkey = Key.O, Activate = () => NavigationService.Navigate(new InventoryReportView()) },
            new TileSpec { Label = "Laporan Keuangan",      UnderlineIndex = 0,  Hotkey = Key.L, Activate = () => NavigationService.Navigate(new FinancialReportView()) },
        },
        "Bank" => new[]
        {
            new TileSpec { Label = "Input Tabel Bank",        UnderlineIndex = 12, Hotkey = Key.B, Activate = () => NavigationService.Navigate(new BankView()) },
            new TileSpec { Label = "Input Giro Tolakan/Cair", UnderlineIndex = 6,  Hotkey = Key.G, Activate = () => NavigationService.Navigate(new BankGiroView()) },
        },
        "Utility" => new[]
        {
            new TileSpec { Label = "User Management",  UnderlineIndex = 5, Hotkey = Key.M, Activate = () => NavigationService.Navigate(new UserView()) },
            new TileSpec { Label = "Printer Config",   UnderlineIndex = 0, Hotkey = Key.P, Activate = () => NavigationService.Navigate(new PrinterConfigView()) },
            new TileSpec { Label = "Backup",           UnderlineIndex = 0, Hotkey = Key.B, Activate = () => NavigationService.Navigate(new BackupView()) },
            new TileSpec { Label = "Shift Management", UnderlineIndex = 0, Hotkey = Key.S, Activate = () => NavigationService.Navigate(new ShiftView(_userId)) },
            new TileSpec { Label = "Periksa Update" + (_updateBadgeVersion != null ? $"  ● v{_updateBadgeVersion}" : ""), UnderlineIndex = 8, Hotkey = Key.U, Activate = () => NavigationService.Navigate(new UpdateView()) },
            new TileSpec { Label = "Tentang",          UnderlineIndex = 0, Hotkey = Key.T, Activate = () => NavigationService.Navigate(new AboutView()) },
        },
        _ => Array.Empty<TileSpec>(),
    };

    // ── Tile rendering ────────────────────────────────────────────────────

    private void ShowMainTiles()
    {
        _level = Level.Main;
        _openCategory = null;
        BreadcrumbLabel.IsVisible = false;
        HintBar.Text = "F12=Sync  Esc=Login  Arrow keys navigate tiles  Enter activates";
        RebuildTiles(MainTiles());
    }

    private void DrillInto(string category)
    {
        _level = Level.SubMenu;
        _openCategory = category;
        BreadcrumbLabel.Text = $"← {category}   (Esc=Kembali)";
        BreadcrumbLabel.IsVisible = true;
        HintBar.Text = "Esc=Kembali  Arrow keys navigate  Enter activates";
        RebuildTiles(SubTiles(category));
    }

    private int _lastRowCount = 1;

    private void RebuildTiles(IReadOnlyList<TileSpec> tiles)
    {
        BentoGrid.Children.Clear();
        BentoGrid.RowDefinitions.Clear();
        BentoGrid.ColumnDefinitions.Clear();

        if (tiles.Count == 0) return;

        int cols = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(tiles.Count)));
        int rows = (int)Math.Ceiling((double)tiles.Count / cols);
        _lastRowCount = rows;

        for (int r = 0; r < rows; r++)
            BentoGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Star, MaxHeight = 220 });
        for (int c = 0; c < cols; c++)
            BentoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });

        for (int i = 0; i < tiles.Count; i++)
        {
            var btn = BuildTileButton(tiles[i]);
            Grid.SetRow(btn, i / cols);
            Grid.SetColumn(btn, i % cols);
            BentoGrid.Children.Add(btn);
        }

        // Responsive tile height: fit (rows*Height + gaps) within BentoGrid bounds.
        // Avalonia Button doesn't stretch vertically (AvaloniaUI#7331) so we set
        // Height explicitly, recomputing on size change to keep tiles from
        // overflowing the status bar on small windows.
        BentoGrid.SizeChanged -= BentoGrid_SizeChanged;
        BentoGrid.SizeChanged += BentoGrid_SizeChanged;
        ApplyTileHeight();

        // Focus first tile so arrow-key nav and Enter work immediately
        if (BentoGrid.Children.Count > 0 && BentoGrid.Children[0] is Button first)
        {
            first.Focus();
        }
    }

    private void BentoGrid_SizeChanged(object? sender, SizeChangedEventArgs e) => ApplyTileHeight();

    private void ApplyTileHeight()
    {
        double avail = BentoGrid.Bounds.Height;
        if (avail <= 0 || _lastRowCount <= 0) return;
        double gap = BentoGrid.RowSpacing;
        double h = (avail - gap * (_lastRowCount - 1)) / _lastRowCount;
        h = Math.Clamp(h, 60, 180);
        foreach (var child in BentoGrid.Children)
            if (child is Button b) b.Height = h;
    }

    private Button BuildTileButton(TileSpec spec)
    {
        var label = new TextBlock
        {
            FontSize = 24,
            FontFamily = (FontFamily)Application.Current!.FindResource("PlexSansFont")!,
            Foreground = (IBrush)Application.Current!.FindResource("FgPrimaryBrush")!,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        // Build inline runs: before-letter + underlined-letter + after-letter
        int idx = Math.Clamp(spec.UnderlineIndex, 0, Math.Max(0, spec.Label.Length - 1));
        if (idx > 0)
            label.Inlines!.Add(new Run(spec.Label.Substring(0, idx)));
        if (idx < spec.Label.Length)
        {
            var hotRun = new Run(spec.Label.Substring(idx, 1)) { TextDecorations = TextDecorations.Underline };
            label.Inlines!.Add(hotRun);
        }
        if (idx + 1 < spec.Label.Length)
            label.Inlines!.Add(new Run(spec.Label.Substring(idx + 1)));

        var keyLabel = spec.Hotkey.ToString().ToUpperInvariant();
        var hint = new TextBlock
        {
            Text = $"[{keyLabel}]",
            FontSize = (double)Application.Current!.FindResource("FontSizeLabel")!,
            FontFamily = (FontFamily)Application.Current!.FindResource("JetBrainsMonoFont")!,
            Foreground = (IBrush)Application.Current!.FindResource("FgSecondaryBrush")!,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        var stack = new StackPanel { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(label);
        stack.Children.Add(hint);

        var btn = new Button
        {
            Content = stack,
            Padding = new Thickness(16, 20, 16, 20),
            // Height set dynamically by ApplyTileHeight() — see RebuildTiles.
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            BorderThickness = new Thickness(1),
            FontFamily = (FontFamily)Application.Current!.FindResource("PlexSansFont")!,
            Background = spec.IsDanger
                ? (IBrush)Application.Current!.FindResource("AccentBgBrush")!
                : (IBrush)Application.Current!.FindResource("Bg1Brush")!,
            Foreground = (IBrush)Application.Current!.FindResource("FgPrimaryBrush")!,
            BorderBrush = (IBrush)Application.Current!.FindResource("BorderStrongBrush")!,
        };
        btn.Click += (_, _) => spec.Activate();
        return btn;
    }

    // ── Keyboard handling ─────────────────────────────────────────────────

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _registeredTopLevel = TopLevel.GetTopLevel(this);
        _registeredTopLevel?.AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        // GetTopLevel(this) returns null once detached, so use the cached
        // reference from OnAttachedToVisualTree — otherwise the tunneled Esc
        // handler leaks onto the window and fires when other views are active,
        // hijacking their Esc and sending the user back to login.
        _registeredTopLevel?.RemoveHandler(KeyDownEvent, OnPreviewKeyDown);
        _registeredTopLevel = null;
        base.OnDetachedFromVisualTree(e);
    }

    // Tunneled handler runs BEFORE the focused Button's KeyNav, so letter shortcuts fire.
    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled) return;
        if (e.KeyModifiers != KeyModifiers.None) return;
        // Don't hijack keys when a TextBox is focused (defensive — none exist on this view today).
        if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox) return;

        // Escape on tunneled path so back-nav works even when no tile has focus
        // (e.g. right after NavigationService.GoBack restores this view).
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            if (_level == Level.SubMenu) ShowMainTiles();
            else NavigationService.ReplaceRoot(new LoginView());
            return;
        }

        var tiles = _level == Level.Main ? MainTiles() : SubTiles(_openCategory ?? "");
        foreach (var t in tiles)
        {
            if (e.Key == t.Hotkey)
            {
                e.Handled = true;
                t.Activate();
                return;
            }
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        // Escape is handled by the tunneled OnPreviewKeyDown above — it fires
        // regardless of which descendant owns focus, which we need because after
        // GoBack the restored view may not have focus on a tile yet.

        if (KeyboardRouter.IsF12(e))
        {
            e.Handled = true;
            FooterStatus.Show(StatusLabel, "Sync tidak tersedia.");
            return;
        }

        if (e.Key == Key.F10)
        {
            e.Handled = true;
            ShowMainTiles();
            return;
        }

        // Arrow-key navigation between tiles
        HandleTileArrowNav(e);
    }

    private void HandleTileArrowNav(KeyEventArgs e)
    {
        if (BentoGrid.Children.Count == 0) return;
        int cols = BentoGrid.ColumnDefinitions.Count;
        if (cols == 0) return;

        var buttons = new List<Button>();
        foreach (var child in BentoGrid.Children)
            if (child is Button b) buttons.Add(b);
        if (buttons.Count == 0) return;

        int focused = -1;
        for (int i = 0; i < buttons.Count; i++)
            if (buttons[i].IsFocused) { focused = i; break; }
        if (focused < 0) return;

        int next = focused;
        if (e.Key == Key.Right) next = (focused + 1) % buttons.Count;
        else if (e.Key == Key.Left) next = (focused - 1 + buttons.Count) % buttons.Count;
        else if (e.Key == Key.Down) next = Math.Min(focused + cols, buttons.Count - 1);
        else if (e.Key == Key.Up) next = Math.Max(focused - cols, 0);
        else return;

        e.Handled = true;
        buttons[next].Focus();
    }

    // ── Update check ──────────────────────────────────────────────────────

    private async Task CheckForUpdateAsync()
    {
        try
        {
            var svc = new UpdateService(DbConnection.GetConnection());
            var result = await svc.CheckForUpdateAsync();
            if (!result.Available || string.IsNullOrEmpty(result.NewVersion)) return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                // (b) Footer toast — 10 seconds
                FooterStatus.Show(StatusLabel, $"● Update v{result.NewVersion} tersedia — Utility → U", 10);

                // (a) Badge — store version so tile builder picks it up on next Utility navigation
                _updateBadgeVersion = result.NewVersion;
            });
        }
        catch
        {
            // Silent — update check is best-effort, no UI surprise on offline / 404
        }
    }

    // ── Status helpers ────────────────────────────────────────────────────

    private void RefreshStatus()
    {
        string regId = _configRepo.Get("register_id") ?? "01";
        var shift = _shiftRepo.GetOpenShift(regId);
        LblShiftStatus.Text = shift != null
            ? $"Shift {shift.ShiftNumber} BUKA"
            : "Shift belum dibuka";

        string today = DateTime.Now.ToString("yyyy-MM-dd");
        int count = _saleRepo.GetDailyCount(today);
        LblDailyCount.Text = $"Transaksi hari ini: {count}";

        FooterStatus.Reset(StatusLabel);
    }

}
