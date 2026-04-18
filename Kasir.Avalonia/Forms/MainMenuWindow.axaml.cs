using System;
using Avalonia.Controls;
using Avalonia.Input;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Utils;
using Kasir.Avalonia.Forms.Shared;
using Kasir.Avalonia.Forms.Master;
using Kasir.Avalonia.Forms.Admin;
using Kasir.Avalonia.Forms.POS;

namespace Kasir.Avalonia.Forms;

public partial class MainMenuWindow : Window
{
    private readonly int _userId;
    private readonly ConfigRepository _configRepo;
    private readonly SaleRepository _saleRepo;
    private readonly ShiftRepository _shiftRepo;

    public MainMenuWindow(int userId = 1)
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

    private void WireMenuItems()
    {
        MniDepartment.Click += (_, _) => new DepartmentWindow(_userId).ShowDialog(this);
        MniVendor.Click += (_, _) => new VendorWindow(_userId).ShowDialog(this);
        MniUsers.Click += (_, _) => new UserWindow().ShowDialog(this);
        MniPrinterConfig.Click += (_, _) => new PrinterConfigWindow().ShowDialog(this);
        MniBackup.Click += (_, _) => new BackupWindow().ShowDialog(this);
        MniShift.Click += (_, _) => new ShiftWindow(_userId).ShowDialog(this);
        MniExit.Click += (_, _) => Close();
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
        if (KeyboardRouter.IsEscape(e)) { e.Handled = true; Close(); }
        else if (KeyboardRouter.IsF12(e)) { e.Handled = true; SetStatus("Sync tidak tersedia."); }
    }

    private void SetStatus(string t) => StatusLabel.Text = t;
}
