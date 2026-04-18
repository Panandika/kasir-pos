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
