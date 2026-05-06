using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Hardware;
using Kasir.Avalonia.Forms.Shared;
using Kasir.Avalonia.Navigation;
using Kasir.Avalonia.Utils;

namespace Kasir.Avalonia.Forms.Admin;

public partial class PrinterConfigView : UserControl
{
    private readonly ConfigRepository _configRepo;

    private record KindOption(string Label, string Value);

    private static readonly KindOption[] Kinds =
    {
        new("Windows Printer (driver Windows)", "windows"),
        new("Serial (COM port)", "serial"),
        new("Device File (LPT, /dev/usb/lp0)", "device_file"),
    };

    public PrinterConfigView()
    {
        InitializeComponent();
        _configRepo = new ConfigRepository(DbConnection.GetConnection());

        CmbKind.ItemsSource = Kinds.Select(k => k.Label).ToArray();
        CmbBaud.ItemsSource = PrinterDiscovery.CommonBaudRates.Select(b => b.ToString()).ToArray();

        LoadSavedConfig();

        CmbKind.SelectionChanged += (_, _) => OnKindChanged();
        BtnRefresh.Click += (_, _) => RepopulateNames();
        BtnTestPrint.Click += async (_, _) => await OnTestPrint();
        BtnTestDrawer.Click += async (_, _) => await OnTestDrawer();
        BtnSave.Click += async (_, _) => await OnSave();

        FooterStatus.RegisterDefault(StatusLabel, "Printer Config — Test Print, Test Drawer, Simpan — Esc=Keluar");
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (KeyboardRouter.IsEscape(e)) { e.Handled = true; NavigationService.GoBack(); }
    }

    private void LoadSavedConfig()
    {
        string savedKind = _configRepo.Get("printer_kind") ?? "";
        string savedName = _configRepo.Get("printer_name") ?? "";
        string savedBaud = _configRepo.Get("printer_baud") ?? RawPrinterFactory.DefaultBaud.ToString();

        // Map saved kind → dropdown index. Empty/unknown → infer from name (legacy installs).
        int idx = System.Array.FindIndex(Kinds, k => k.Value == savedKind);
        if (idx < 0) idx = InferKindIndex(savedName);
        CmbKind.SelectedIndex = idx;

        RepopulateNames();
        if (!string.IsNullOrEmpty(savedName)) CmbName.SelectedItem = savedName;
        CmbName.Text = savedName;

        int baudIdx = System.Array.FindIndex(PrinterDiscovery.CommonBaudRates, b => b.ToString() == savedBaud);
        CmbBaud.SelectedIndex = baudIdx >= 0 ? baudIdx : System.Array.IndexOf(PrinterDiscovery.CommonBaudRates, RawPrinterFactory.DefaultBaud);
    }

    private static int InferKindIndex(string name)
    {
        if (string.IsNullOrEmpty(name)) return 0; // default Windows
        if (name.StartsWith("LPT", System.StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("/dev/usb", System.StringComparison.OrdinalIgnoreCase)) return 2;
        if (name.StartsWith("COM", System.StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("/dev/tty", System.StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("/dev/cu", System.StringComparison.OrdinalIgnoreCase)) return 1;
        return 0; // anything else → assume Windows queue name
    }

    private void OnKindChanged()
    {
        BaudRow.IsVisible = SelectedKind() == "serial";
        RepopulateNames();
    }

    private string SelectedKind()
    {
        int i = CmbKind.SelectedIndex;
        return (i >= 0 && i < Kinds.Length) ? Kinds[i].Value : "windows";
    }

    private void RepopulateNames()
    {
        string kind = SelectedKind();
        string keep = CmbName.Text ?? "";

        switch (kind)
        {
            case "windows":
                CmbName.ItemsSource = PrinterDiscovery.EnumerateWindowsPrinters().ToArray();
                break;
            case "serial":
                CmbName.ItemsSource = PrinterDiscovery.EnumerateSerialPorts().ToArray();
                break;
            default:
                CmbName.ItemsSource = System.Array.Empty<string>();
                break;
        }
        if (!string.IsNullOrEmpty(keep)) CmbName.Text = keep;
    }

    private int SelectedBaud()
    {
        int i = CmbBaud.SelectedIndex;
        return (i >= 0 && i < PrinterDiscovery.CommonBaudRates.Length)
            ? PrinterDiscovery.CommonBaudRates[i]
            : RawPrinterFactory.DefaultBaud;
    }

    private string CurrentName() => (CmbName.Text ?? "").Trim();

    private async Task OnTestPrint()
    {
        string name = CurrentName();
        if (string.IsNullOrEmpty(name))
        {
            await MsgBox.Show(NavigationService.Owner, "Pilih printer / port dulu.");
            return;
        }
        var raw = RawPrinterFactory.Create(SelectedKind(), name, SelectedBaud());
        var printer = new ReceiptPrinter(raw);
        bool ok = printer.PrintTestReceipt(_configRepo.Get("store_name") ?? "TEST STORE");
        await MsgBox.Show(NavigationService.Owner, ok ? "Test print dikirim!" : "Print gagal.\n" + (printer.LastError ?? "(tidak ada detail error)"));
    }

    private async Task OnTestDrawer()
    {
        string name = CurrentName();
        if (string.IsNullOrEmpty(name))
        {
            await MsgBox.Show(NavigationService.Owner, "Pilih printer / port dulu.");
            return;
        }
        var raw = RawPrinterFactory.Create(SelectedKind(), name, SelectedBaud());
        var drawer = new CashDrawer(raw);
        bool ok = drawer.Open();
        await MsgBox.Show(NavigationService.Owner, ok ? "Perintah laci dikirim!" : "Laci gagal.\n" + (raw.LastError ?? "(tidak ada detail error)"));
    }

    private async Task OnSave()
    {
        _configRepo.Set("printer_kind", SelectedKind());
        _configRepo.Set("printer_name", CurrentName());
        _configRepo.Set("printer_baud", SelectedBaud().ToString());
        await MsgBox.Show(NavigationService.Owner, "Konfigurasi printer tersimpan.");
    }

    private void SetStatus(string t) => FooterStatus.Show(StatusLabel, t);
}
