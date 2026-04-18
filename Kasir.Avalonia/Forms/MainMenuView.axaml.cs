using System;
using Avalonia.Controls;
using Avalonia.Input;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Utils;
using Kasir.Avalonia.Navigation;
using Kasir.Auth;
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

    // Track which sub-menu panel is currently open (null = bento grid visible)
    private Panel? _openSubMenu;

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

        WireTileButtons();
        WireMenuItems();
        RefreshStatus();
    }

    public void OnNavigatedTo() => RefreshStatus();

    // ── Tile button wiring ────────────────────────────────────────────────

    private void WireTileButtons()
    {
        BtnMaster.Click    += (_, _) => OpenSubMenu(SubMenuMaster);
        BtnTransaksi.Click += (_, _) => OpenSubMenu(SubMenuTransaksi);
        BtnAkuntansi.Click += (_, _) => OpenSubMenu(SubMenuAkuntansi);
        BtnLaporan.Click   += (_, _) => OpenSubMenu(SubMenuLaporan);
        BtnUtility.Click   += (_, _) => OpenSubMenu(SubMenuUtility);
        BtnKeluar.Click    += (_, _) => NavigationService.ReplaceRoot(new LoginView());
    }

    private void OpenSubMenu(Panel subMenu)
    {
        // Hide bento grid, show the requested sub-menu panel
        BentoGrid.IsVisible = false;
        if (_openSubMenu != null)
            _openSubMenu.IsVisible = false;

        _openSubMenu = subMenu;
        subMenu.IsVisible = true;

        // Focus the first menu item in the sub-menu so keyboard nav works immediately
        var menu = subMenu.Children.Count > 0 ? subMenu.Children[0] as Menu : null;
        menu?.Focus();
    }

    private void CloseSubMenu()
    {
        if (_openSubMenu != null)
        {
            _openSubMenu.IsVisible = false;
            _openSubMenu = null!;
        }
        BentoGrid.IsVisible = true;
        // Return focus to first tile
        BtnMaster.Focus();
    }

    // ── Sub-menu item wiring (unchanged handlers) ─────────────────────────

    private void WireMenuItems()
    {
        MniDepartment.Click   += (_, _) => NavigationService.Navigate(new DepartmentView(_userId));
        MniVendor.Click       += (_, _) => NavigationService.Navigate(new VendorView(_userId));
        MniProduct.Click      += (_, _) => NavigationService.Navigate(new ProductView(_userId));
        MniCreditCard.Click   += (_, _) => NavigationService.Navigate(new CreditCardView(_userId));
        MniPriceChange.Click  += (_, _) => NavigationService.Navigate(new PriceChangeView());
        MniOpname.Click       += (_, _) => NavigationService.Navigate(new OpnameView());
        MniPurchaseOrder.Click   += (_, _) => NavigationService.Navigate(new PurchaseOrderView());
        MniGoodsReceipt.Click    += (_, _) => NavigationService.Navigate(new GoodsReceiptView());
        MniPurchaseInvoice.Click += (_, _) => NavigationService.Navigate(new PurchaseInvoiceView());
        MniPayables.Click        += (_, _) => NavigationService.Navigate(new PayablesView());
        MniReturn.Click          += (_, _) => NavigationService.Navigate(new ReturnView());
        MniStockOut.Click        += (_, _) => NavigationService.Navigate(new StockOutView());
        MniSale.Click            += (_, _) => NavigationService.Navigate(new SaleView(new AuthService(DbConnection.GetConnection())));
        MniTransfer.Click        += (_, _) => NavigationService.Navigate(new TransferView());
        MniAccounts.Click    += (_, _) => NavigationService.Navigate(new AccountsView());
        MniJournal.Click     += (_, _) => NavigationService.Navigate(new JournalView(userId: _userId));
        MniCashReceipt.Click += (_, _) => NavigationService.Navigate(new CashReceiptView(userId: _userId));
        MniCashDisburse.Click += (_, _) => NavigationService.Navigate(new CashDisbursementView(userId: _userId));
        MniPosting.Click     += (_, _) => NavigationService.Navigate(new PostingProgressView());
        MniRptProduct.Click  += (_, _) => NavigationService.Navigate(new ProductReportView());
        MniRptSupplier.Click += (_, _) => NavigationService.Navigate(new SupplierReportView());
        MniRptSales.Click    += (_, _) => NavigationService.Navigate(new SalesReportView());
        MniRptInventory.Click += (_, _) => NavigationService.Navigate(new InventoryReportView());
        MniRptFinancial.Click += (_, _) => NavigationService.Navigate(new FinancialReportView());
        MniUsers.Click        += (_, _) => NavigationService.Navigate(new UserView());
        MniPrinterConfig.Click += (_, _) => NavigationService.Navigate(new PrinterConfigView());
        MniBackup.Click       += (_, _) => NavigationService.Navigate(new BackupView());
        MniShift.Click        += (_, _) => NavigationService.Navigate(new ShiftView(_userId));
        MniUpdate.Click       += (_, _) => NavigationService.Navigate(new UpdateView());
        MniAbout.Click        += (_, _) => NavigationService.Navigate(new AboutView());
    }

    // ── Keyboard handling ─────────────────────────────────────────────────

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (KeyboardRouter.IsEscape(e))
        {
            e.Handled = true;
            if (_openSubMenu != null)
                CloseSubMenu();
            else
                NavigationService.ReplaceRoot(new LoginView());
            return;
        }

        if (KeyboardRouter.IsF12(e))
        {
            e.Handled = true;
            SetStatus("Sync tidak tersedia.");
            return;
        }

        // F10 focuses first tile (bento grid entry point)
        if (e.Key == Key.F10)
        {
            e.Handled = true;
            CloseSubMenu();
            BtnMaster.Focus();
            return;
        }

        // Direct single-letter shortcuts when bento is visible and no modifier is held.
        // Alt is intentionally excluded — shortcuts are bare letters now.
        if (_openSubMenu == null && BentoGrid.IsVisible && e.KeyModifiers == KeyModifiers.None)
        {
            switch (e.Key)
            {
                case Key.M: e.Handled = true; OpenSubMenu(SubMenuMaster);    return;
                case Key.T: e.Handled = true; OpenSubMenu(SubMenuTransaksi); return;
                case Key.K: e.Handled = true; OpenSubMenu(SubMenuAkuntansi); return;
                case Key.L: e.Handled = true; OpenSubMenu(SubMenuLaporan);   return;
                case Key.U: e.Handled = true; OpenSubMenu(SubMenuUtility);   return;
                case Key.E: e.Handled = true; NavigationService.ReplaceRoot(new LoginView()); return;
            }
        }

        // Arrow-key navigation between tiles when bento grid is visible
        if (_openSubMenu == null && BentoGrid.IsVisible)
        {
            HandleTileArrowNav(e);
        }
    }

    private void HandleTileArrowNav(KeyEventArgs e)
    {
        // Tile order left-to-right, top-to-bottom: 0=Master 1=Transaksi 2=Akuntansi 3=Laporan 4=Utility 5=Keluar
        var tiles = new Button[] { BtnMaster, BtnTransaksi, BtnAkuntansi, BtnLaporan, BtnUtility, BtnKeluar };
        const int cols = 3;

        int focused = -1;
        for (int i = 0; i < tiles.Length; i++)
        {
            if (tiles[i].IsFocused) { focused = i; break; }
        }
        if (focused < 0) return;

        int next = focused;
        if (e.Key == Key.Right)  next = (focused + 1) % tiles.Length;
        else if (e.Key == Key.Left)  next = (focused - 1 + tiles.Length) % tiles.Length;
        else if (e.Key == Key.Down)  next = Math.Min(focused + cols, tiles.Length - 1);
        else if (e.Key == Key.Up)    next = Math.Max(focused - cols, 0);
        else return;

        e.Handled = true;
        tiles[next].Focus();
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

        SetStatus("F12=Sync  Esc=Keluar  F10=Menu");
    }

    private void SetStatus(string t) => StatusLabel.Text = t;
}
