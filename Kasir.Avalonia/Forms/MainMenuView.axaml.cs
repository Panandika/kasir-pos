using System;
using Avalonia.Controls;
using Avalonia.Input;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Utils;
using Kasir.Avalonia.Navigation;
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

        WireMenuItems();
        RefreshStatus();
    }

    public void OnNavigatedTo() => RefreshStatus();

    private void WireMenuItems()
    {
        var owner = NavigationService.Owner;

        MniDepartment.Click   += (_, _) => new DepartmentWindow(_userId).ShowDialog(owner);
        MniVendor.Click       += (_, _) => new VendorWindow(_userId).ShowDialog(owner);
        MniProduct.Click      += (_, _) => new ProductWindow(_userId).ShowDialog(owner);
        MniCreditCard.Click   += (_, _) => new CreditCardWindow(_userId).ShowDialog(owner);
        MniPriceChange.Click  += (_, _) => new PriceChangeWindow().ShowDialog(owner);
        MniOpname.Click       += (_, _) => new OpnameWindow().ShowDialog(owner);
        MniPurchaseOrder.Click   += (_, _) => new PurchaseOrderWindow().ShowDialog(owner);
        MniGoodsReceipt.Click    += (_, _) => new GoodsReceiptWindow().ShowDialog(owner);
        MniPurchaseInvoice.Click += (_, _) => new PurchaseInvoiceWindow().ShowDialog(owner);
        MniPayables.Click        += (_, _) => new PayablesWindow().ShowDialog(owner);
        MniReturn.Click          += (_, _) => new ReturnWindow().ShowDialog(owner);
        MniStockOut.Click        += (_, _) => new StockOutWindow().ShowDialog(owner);
        MniSale.Click            += (_, _) => new SaleWindow(new Kasir.Auth.AuthService(DbConnection.GetConnection())).ShowDialog(owner);
        MniTransfer.Click        += (_, _) => new TransferWindow().ShowDialog(owner);
        MniAccounts.Click    += (_, _) => new AccountsWindow().ShowDialog(owner);
        MniJournal.Click     += (_, _) => new JournalWindow(userId: _userId).ShowDialog(owner);
        MniCashReceipt.Click += (_, _) => new CashReceiptWindow(userId: _userId).ShowDialog(owner);
        MniCashDisburse.Click += (_, _) => new CashDisbursementWindow(userId: _userId).ShowDialog(owner);
        MniPosting.Click     += (_, _) => new PostingProgressWindow().ShowDialog(owner);
        MniRptProduct.Click  += (_, _) => new ProductReportWindow().ShowDialog(owner);
        MniRptSupplier.Click += (_, _) => new SupplierReportWindow().ShowDialog(owner);
        MniRptSales.Click    += (_, _) => new SalesReportWindow().ShowDialog(owner);
        MniRptInventory.Click += (_, _) => new InventoryReportWindow().ShowDialog(owner);
        MniRptFinancial.Click += (_, _) => new FinancialReportWindow().ShowDialog(owner);
        MniUsers.Click        += (_, _) => new UserWindow().ShowDialog(owner);
        MniPrinterConfig.Click += (_, _) => new PrinterConfigWindow().ShowDialog(owner);
        MniBackup.Click       += (_, _) => new BackupWindow().ShowDialog(owner);
        MniShift.Click        += (_, _) => new ShiftWindow(_userId).ShowDialog(owner);
        MniUpdate.Click       += (_, _) => new UpdateWindow().ShowDialog(owner);
        MniAbout.Click        += (_, _) => new AboutWindow().ShowDialog(owner);
        MniExit.Click         += (_, _) => NavigationService.ReplaceRoot(new LoginView());
    }

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

        SetStatus("F12=Sync  Esc=Keluar");
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (KeyboardRouter.IsEscape(e)) { e.Handled = true; NavigationService.ReplaceRoot(new LoginView()); }
        else if (KeyboardRouter.IsF12(e)) { e.Handled = true; SetStatus("Sync tidak tersedia."); }
    }

    private void SetStatus(string t) => StatusLabel.Text = t;
}
