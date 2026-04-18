using Avalonia.Controls;
using Avalonia.Input;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Hardware;
using Kasir.Avalonia.Forms.Shared;
using Kasir.Avalonia.Navigation;

namespace Kasir.Avalonia.Forms.Admin;

public partial class PrinterConfigView : UserControl
{
    private readonly ConfigRepository _configRepo;

    public PrinterConfigView()
    {
        InitializeComponent();
        _configRepo = new ConfigRepository(DbConnection.GetConnection());
        TxtPrinterName.Text = _configRepo.Get("printer_name") ?? "";
        BtnTestPrint.Click += async (_, _) => await OnTestPrint();
        BtnTestDrawer.Click += async (_, _) => await OnTestDrawer();
        BtnSave.Click += async (_, _) => await OnSave();
        SetStatus("Printer Config — Test Print, Test Drawer, Save — Esc=Keluar");
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (KeyboardRouter.IsEscape(e)) { e.Handled = true; NavigationService.GoBack(); }
    }

    private async System.Threading.Tasks.Task OnTestPrint()
    {
        string name = TxtPrinterName.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(name))
        {
            await MsgBox.Show(NavigationService.Owner, "Nama printer tidak boleh kosong.");
            return;
        }
        var printer = new ReceiptPrinter(name);
        bool ok = printer.PrintTestReceipt(_configRepo.Get("store_name") ?? "TEST STORE");
        await MsgBox.Show(NavigationService.Owner, ok ? "Test print dikirim!" : "Print gagal. Cek nama printer.");
    }

    private async System.Threading.Tasks.Task OnTestDrawer()
    {
        string name = TxtPrinterName.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(name))
        {
            await MsgBox.Show(NavigationService.Owner, "Nama printer tidak boleh kosong.");
            return;
        }
        var drawer = new CashDrawer(name);
        bool ok = drawer.Open();
        await MsgBox.Show(NavigationService.Owner, ok ? "Perintah laci dikirim!" : "Laci gagal.");
    }

    private async System.Threading.Tasks.Task OnSave()
    {
        _configRepo.Set("printer_name", TxtPrinterName.Text?.Trim() ?? "");
        await MsgBox.Show(NavigationService.Owner, "Nama printer tersimpan.");
    }

    private void SetStatus(string t) => StatusLabel.Text = t;
}
