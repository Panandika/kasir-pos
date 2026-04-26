using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Kasir.Auth;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Hardware;
using Kasir.Models;
using Kasir.Services;
using Kasir.Utils;
using Kasir.Avalonia.Behaviors;
using Kasir.Avalonia.Forms.Shared;
using Kasir.Avalonia.Navigation;
using Kasir.Avalonia.Diagnostics;
using Kasir.Avalonia.Infrastructure;
using Kasir.Avalonia.Utils;

namespace Kasir.Avalonia.Forms.POS;

public partial class SaleView : UserControl, INavigationAware
{
    private record SaleItemRow(int No, string Code, string Name, int Qty, string Price, string Total, string Disc);
    private record SearchRow(string Code, string Name, string Price, Product Tag);

    private readonly ObservableCollection<SaleItemRow> _rows = new();
    private readonly ObservableCollection<SearchRow> _searchRows = new();
    private readonly SalesService _salesService;
    private readonly SaleRepository _saleRepo;
    private readonly ConfigRepository _configRepo;
    private readonly ShiftRepository _shiftRepo;
    private readonly ProductRepository _productRepo;
    private readonly AuthService _auth;
    private readonly User _cashier;
    private readonly IClock _clock;
    private Shift? _currentShift;
    private bool _searchByCode;
    private DispatcherTimer? _debounce;
    private DispatcherTimer? _clockTimer;

    private enum InputMode { Normal, AwaitingMiscPrice }
    private InputMode _inputMode = InputMode.Normal;
    private int _pendingMiscQty = 1;
    private bool _printerHealthChecked;
    private string _printerStatusText = "";
    private bool _printerStatusOk;

    // ── Kembalian / TUNAI banner state ──
    private enum BannerState { Subtotal, Tunai, Kembalian }
    private BannerState _bannerState = BannerState.Subtotal;
    private DispatcherTimer? _bannerTimer;
    private IBrush? _subtotalDefaultBrush;

    public SaleView(AuthService auth)
    {
        InitializeComponent();
        _auth = auth;
        _clock = new ClockImpl();
        var conn = DbConnection.GetConnection();
        _configRepo = new ConfigRepository(conn);
        _shiftRepo = new ShiftRepository(conn);
        _saleRepo = new SaleRepository(conn);
        _productRepo = new ProductRepository(conn);
        _salesService = new SalesService(conn, _clock);
        // Cashier identity comes from the shared session populated by LoginView.
        // If missing (e.g. deep link or navigation from a non-login state), fall
        // back to whatever AuthService knows and default to "UNKNOWN"/0 so the
        // POS can at least render — user can re-login via Esc → Keluar if needed.
        _cashier = CurrentSession.User ?? _auth.CurrentUser
                   ?? new User { Id = 0, Alias = "UNKNOWN" };
        _salesService.SetCashier(_cashier.Alias ?? "UNKNOWN", _cashier.Id);

        DgvItems.ItemsSource = _rows;
        DgvSearch.ItemsSource = _searchRows;

        _subtotalDefaultBrush = LblSubtotal.Foreground;
        TxtBarcode.KeyDown += OnBarcodeKeyDown;
        TxtBarcode.TextChanged += (_, _) => { if (_bannerState != BannerState.Subtotal) ResetBanner(); };
        TxtSearchInput.TextChanged += (_, _) => OnSearchTextChanged();
        TxtSearchInput.KeyDown += OnSearchInputKeyDown;
        DgvSearch.KeyDown += OnSearchGridKeyDown;

        CheckShift();
        UpdateTotals();
        UpdateFooter();

        FooterStatus.RegisterDefault(StatusLabel, "F1=Kode  F2=Nama  F3=Qty  F5=Bayar  F8=Void  F9=Kalkulator  F10=Batal  F11=Laci  +=Pas  Esc=Keluar");

        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => UpdateFooter();
        _clockTimer.Start();

        ViewShortcuts.AutoFocusOnAttach(this, TxtBarcode);

        _ = CheckPrinterHealthAsync();
    }

    private async Task CheckPrinterHealthAsync()
    {
        if (_printerHealthChecked) return;
        _printerHealthChecked = true;

        string kind = _configRepo.Get("printer_kind") ?? "";
        string name = _configRepo.Get("printer_name") ?? "";

        if (string.IsNullOrEmpty(name))
        {
            _printerStatusText = "OFF";
            _printerStatusOk = false;
            await Dispatcher.UIThread.InvokeAsync(UpdateFooter);
            return;
        }

        var (warning, ok) = await Task.Run(() =>
        {
            // For Windows queues, prefer the WMI status check — it's instant and
            // doesn't open a print job. Fall back to a real Init send for other kinds.
            if (kind == "windows" || (string.IsNullOrEmpty(kind) && !name.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                                                                  && !name.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)
                                                                  && !name.StartsWith("/dev/", StringComparison.OrdinalIgnoreCase)))
            {
                string status = PrinterDiscovery.GetWindowsPrinterStatus(name);
                return status switch
                {
                    null or "ready" or "printing" or "warmup" or "other" or "unknown" => ((string?)null, true),
                    "paused"     => ($"Printer '{name}' di-pause di Windows", false),
                    "offline"    => ($"Printer '{name}' offline", false),
                    "not_found"  => ($"Printer '{name}' tidak ditemukan", false),
                    _            => ($"Printer '{name}' status: {status}", false),
                };
            }

            var printer = new ReceiptPrinter(_configRepo);
            if (printer.IsAvailable()) return ((string?)null, true);
            return (printer.LastError ?? "tidak tersedia", false);
        });

        _printerStatusOk = ok;
        _printerStatusText = ok ? "ON" : "OFF";
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            UpdateFooter();
            if (warning != null)
            {
                var hint = FooterStatus.GetDefault(StatusLabel) ?? "";
                FooterStatus.Show(StatusLabel, "⚠ " + warning + (string.IsNullOrEmpty(hint) ? "" : "  " + hint));
            }
        });
    }

    public void OnNavigatedTo()
    {
        CheckShift();
        Dispatcher.UIThread.Post(() => TxtBarcode.Focus(), DispatcherPriority.Background);
    }

    private void CheckShift()
    {
        string regId = _configRepo.Get("register_id") ?? "01";
        _currentShift = _shiftRepo.GetOpenShift(regId);
        if (_currentShift != null) _salesService.SetShift(_currentShift.ShiftNumber);
    }

    private void OnBarcodeKeyDown(object? sender, KeyEventArgs e)
    {
        // Banner dismiss on any keypress while kembalian/tunai is showing.
        // Pass-through (no e.Handled) so the keystroke still types into the box.
        if (_bannerState != BannerState.Subtotal) ResetBanner();

        // Esc during misc-price prompt cancels the prompt without exiting the sale.
        if (_inputMode == InputMode.AwaitingMiscPrice && e.Key == Key.Escape)
        {
            e.Handled = true;
            TxtBarcode.Text = "";
            ExitPricePromptMode();
            FooterStatus.Show(StatusLabel, "Input Barang Tanpa Kode dibatalkan.");
            return;
        }

        // `+` quick-cash: digits-only contents in TxtBarcode treated as cash
        // amount. If contents are not digits-only (e.g. user typed product
        // code), or no sale exists, fall through to global Key.Add handler
        // which does DoExactPayment().
        if (e.Key == Key.Add || e.Key == Key.OemPlus)
        {
            string cashRaw = (TxtBarcode.Text ?? "").Trim();
            if (cashRaw.Length > 0 && IndonesianMoneyFormatter.IsDigitsOnly(cashRaw))
            {
                e.Handled = true;
                HandleQuickCash(cashRaw);
                return;
            }
            // Otherwise let SaleView.OnKeyDown's Key.Add path run — DoExactPayment.
            return;
        }

        if (!KeyboardRouter.IsEnter(e)) return;
        e.Handled = true;
        string raw = (TxtBarcode.Text ?? "").Trim();
        TxtBarcode.Text = "";
        if (raw.Length == 0) return;

        if (_inputMode == InputMode.AwaitingMiscPrice)
        {
            HandleMiscPriceInput(raw);
            return;
        }

        // Normal mode: "<code>" or "<code>*<qty>"
        string code; int qty = 1;
        int star = raw.IndexOf('*');
        if (star >= 0)
        {
            code = raw.Substring(0, star).Trim();
            string qtyStr = raw.Substring(star + 1).Trim();
            if (!int.TryParse(qtyStr, out qty) || qty < 1)
            {
                FooterStatus.Show(StatusLabel, "Qty tidak valid.");
                return;
            }
        }
        else
        {
            code = raw;
        }

        if (code == SalesService.MiscProductCode)
        {
            EnterPricePromptMode(qty);
            return;
        }

        using var _ = PerfMetrics.Measure(PerfMetrics.BarcodeScanLine);
        AddItemByCode(code, qty);
    }

    private async void AddItemByCode(string code, int qty = 1)
    {
        if (_currentShift == null)
        {
            bool openNow = await MsgBox.Confirm(NavigationService.Owner,
                "Tidak ada shift terbuka. Buka shift sekarang?");
            if (!openNow) { FooterStatus.Show(StatusLabel, "Shift belum dibuka."); return; }
            NavigationService.Navigate(new ShiftView(_cashier.Id));
            return;
        }
        var item = _salesService.AddItem(code, qty);
        if (item == null)
        {
            if (long.TryParse(code, out long numVal))
                LblSubtotal.Text = numVal.ToString("N0");
            FooterStatus.Show(StatusLabel, "Barang tidak ditemukan: " + code);
            return;
        }
        RefreshGrid();
        UpdateTotals();
        FooterStatus.Show(StatusLabel, $"Ditambahkan: {item.ProductCode} — {item.ProductName}" + (qty > 1 ? $" x{qty}" : ""));
    }

    private void EnterPricePromptMode(int qty)
    {
        _pendingMiscQty = qty;
        _inputMode = InputMode.AwaitingMiscPrice;
        FooterStatus.Show(StatusLabel, $"Barang Tanpa Kode (qty={qty}) — ketik harga (Rp), Enter utk simpan, Esc utk batal.");
    }

    private void ExitPricePromptMode()
    {
        _inputMode = InputMode.Normal;
        _pendingMiscQty = 1;
    }

    private void HandleMiscPriceInput(string text)
    {
        if (!long.TryParse(text, out long rupiah) || rupiah <= 0)
        {
            FooterStatus.Show(StatusLabel, "Harga tidak valid. Ketik angka > 0 atau Esc utk batal.");
            return;
        }
        try
        {
            long unitPriceCents = rupiah * 100;
            var item = _salesService.AddMiscItem(_pendingMiscQty, unitPriceCents);
            RefreshGrid();
            UpdateTotals();
            FooterStatus.Show(StatusLabel, $"Ditambahkan: {SalesService.MiscProductName} — {_pendingMiscQty} x {Formatting.FormatCurrency(unitPriceCents)}");
        }
        catch (Exception ex)
        {
            FooterStatus.Show(StatusLabel, "Gagal: " + ex.Message);
        }
        ExitPricePromptMode();
    }

    private void RefreshGrid()
    {
        _rows.Clear();
        int no = 1;
        foreach (var item in _salesService.CurrentItems)
            _rows.Add(new SaleItemRow(no++, item.ProductCode, item.ProductName ?? "", item.Quantity,
                Formatting.FormatCurrencyShort(item.UnitPrice),
                Formatting.FormatCurrencyShort(item.Value),
                item.DiscValue > 0 ? Formatting.FormatCurrencyShort(item.DiscValue) : ""));
    }

    private void UpdateTotals()
    {
        var t = _salesService.GetTotals();
        // Don't overwrite the banner while it's showing TUNAI/KEMBALIAN.
        if (_bannerState == BannerState.Subtotal)
            LblSubtotal.Text = (t.NetAmount / 100).ToString("N0");
        LblTotalRow.Text = $"TOTAL\u2192  {Formatting.FormatCurrency(t.NetAmount)}";
        LblItemCount.Text = _salesService.CurrentItems.Count.ToString();
    }

    private void UpdateFooter()
    {
        string regId = _configRepo.Get("register_id") ?? "01";
        string today = _clock.Now.ToString("yyyy-MM-dd");
        int cnt = _saleRepo.GetDailyCount(today);
        LblFooter.Text = $"JAM\u2192 {_clock.Now:HH:mm:ss}  MESIN#{regId}  ID#{_cashier.Id}  JRNL#{cnt:D5}";
        if (!string.IsNullOrEmpty(_printerStatusText))
        {
            LblPrinterStatus.Text = $"🖨 {_printerStatusText}";
            LblPrinterStatus.Foreground = _printerStatusOk
                ? new SolidColorBrush(Colors.LimeGreen)
                : new SolidColorBrush(Colors.OrangeRed);
        }
    }

    private async void OpenPayment()
    {
        if (_salesService.CurrentItems.Count == 0) { FooterStatus.Show(StatusLabel, "Tidak ada item."); return; }
        var totals = _salesService.GetTotals();
        var result = await PaymentWindow.Show(NavigationService.Owner, totals.NetAmount);
        if (result == null) { TxtBarcode.Focus(); return; }
        try
        {
            Sale sale;
            using (var _ = PerfMetrics.Measure(PerfMetrics.SaleCommit))
                sale = _salesService.CompleteSale(result.CashAmount, result.CardAmount, result.VoucherAmount, result.CardCode, result.CardType, "");
            FooterStatus.Show(StatusLabel, $"LUNAS: {sale.JournalNo} — Kembali: {Formatting.FormatCurrency(sale.ChangeAmount)}");
            _ = PrintReceiptAsync(sale);
            if (result.CashAmount > 0) OpenCashDrawer();
            _salesService.ClearCurrentSale();
            RefreshGrid();
            if (sale.ChangeAmount > 0) ShowKembalianBanner(sale.ChangeAmount);
            else UpdateTotals();
            UpdateFooter();
            TxtBarcode.Focus();
        }
        catch (Exception ex) { await MsgBox.Show(NavigationService.Owner, "Gagal bayar: " + ex.Message); }
    }

    // ── Banner state machine (Q2) ────────────────────────────────────
    private void ShowTunaiBanner(long cashCents)
    {
        _bannerState = BannerState.Tunai;
        LblSubtotal.Text = $"TUNAI: {Formatting.FormatCurrency(cashCents)}";
        if (_subtotalDefaultBrush != null) LblSubtotal.Foreground = _subtotalDefaultBrush;
    }

    private void ShowKembalianBanner(long changeCents)
    {
        _bannerState = BannerState.Kembalian;
        LblSubtotal.Text = $"KEMBALIAN: {Formatting.FormatCurrency(changeCents)}";
        LblSubtotal.Foreground = new SolidColorBrush(Colors.LimeGreen);
        StartBannerTimer();
    }

    private void StartBannerTimer()
    {
        _bannerTimer?.Stop();
        _bannerTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _bannerTimer.Tick += (_, _) => { _bannerTimer?.Stop(); ResetBanner(); };
        _bannerTimer.Start();
    }

    private void ResetBanner()
    {
        _bannerTimer?.Stop();
        _bannerTimer = null;
        _bannerState = BannerState.Subtotal;
        if (_subtotalDefaultBrush != null) LblSubtotal.Foreground = _subtotalDefaultBrush;
        UpdateTotals();
    }

    private async void HandleQuickCash(string digits)
    {
        if (_salesService.CurrentItems.Count == 0)
        {
            FooterStatus.Show(StatusLabel, "Tidak ada item.");
            return;
        }
        if (!long.TryParse(digits, out long rupiah) || rupiah <= 0)
        {
            FooterStatus.Show(StatusLabel, "Jumlah tunai tidak valid.");
            return;
        }
        long cashCents = rupiah * 100;
        var totals = _salesService.GetTotals();
        if (cashCents < totals.NetAmount)
        {
            FooterStatus.Show(StatusLabel, "Tunai kurang", 3);
            return;
        }
        try
        {
            ShowTunaiBanner(cashCents);
            Sale sale;
            using (var _ = PerfMetrics.Measure(PerfMetrics.SaleCommit))
                sale = _salesService.CompleteSale(cashCents, 0, 0, "", "", "");
            FooterStatus.Show(StatusLabel, $"LUNAS: {sale.JournalNo} — Kembali: {Formatting.FormatCurrency(sale.ChangeAmount)}");
            _ = PrintReceiptAsync(sale);
            OpenCashDrawer();
            _salesService.ClearCurrentSale();
            TxtBarcode.Text = "";
            RefreshGrid(); UpdateFooter();
            // Show kembalian (overrides the brief Tunai banner). UpdateTotals
            // is intentionally NOT called here — ShowKembalianBanner sets the
            // banner text directly, and ResetBanner() will refresh totals.
            if (sale.ChangeAmount > 0)
                ShowKembalianBanner(sale.ChangeAmount);
            else
                ResetBanner();
            TxtBarcode.Focus();
        }
        catch (Exception ex)
        {
            ResetBanner();
            await MsgBox.Show(NavigationService.Owner, "Gagal bayar: " + ex.Message);
        }
    }

    private void DoExactPayment()
    {
        if (_salesService.CurrentItems.Count == 0) return;
        try
        {
            var t = _salesService.GetTotals();
            Sale sale;
            using (var _ = PerfMetrics.Measure(PerfMetrics.SaleCommit))
                sale = _salesService.CompleteSale(t.NetAmount, 0, 0, "", "", "");
            FooterStatus.Show(StatusLabel, $"LUNAS (PAS): {sale.JournalNo}");
            _ = PrintReceiptAsync(sale);
            OpenCashDrawer();
            _salesService.ClearCurrentSale();
            RefreshGrid(); UpdateTotals(); UpdateFooter();
            TxtBarcode.Focus();
        }
        catch (Exception ex) { _ = MsgBox.Show(NavigationService.Owner, "Gagal: " + ex.Message); }
    }

    private async Task PrintReceiptAsync(Sale sale)
    {
        try
        {
            string? printerName = _configRepo.Get("printer_name");
            if (string.IsNullOrEmpty(printerName)) return;
            var items = _saleRepo.GetItemsByJournalNo(sale.JournalNo);
            byte[] data = BuildReceiptBytes(sale, items);
            var printer = new ReceiptPrinter(_configRepo);
            bool ok = await Task.Run(() => printer.Print(data));
            if (!ok) await MsgBox.Show(NavigationService.Owner, "Struk tidak tercetak.\n" + (printer.LastError ?? "(tidak ada detail error)"));
        }
        catch (Exception ex) { await MsgBox.Show(NavigationService.Owner, "Print error: " + ex.Message); }
    }

    private byte[] BuildReceiptBytes(Sale sale, List<SaleItem> items)
    {
        string storeName = _configRepo.Get("store_name") ?? "TOKO";
        string? addr = _configRepo.Get("store_address");
        string? tagline = _configRepo.Get("store_tagline");
        var r = new List<byte[]>
        {
            EscPosCommands.Init, EscPosCommands.CenterAlign, EscPosCommands.BoldOn,
            EscPosCommands.Text(storeName + "\n"), EscPosCommands.BoldOff
        };
        if (!string.IsNullOrEmpty(addr)) r.Add(EscPosCommands.Text(addr + "\n"));
        if (!string.IsNullOrEmpty(tagline)) r.Add(EscPosCommands.Text(tagline + "\n"));
        r.Add(EscPosCommands.LeftAlign);
        r.Add(EscPosCommands.Text($"Date: {sale.DocDate}\n"));
        r.Add(EscPosCommands.Text($"No: {sale.JournalNo}  Kasir: {sale.Cashier}\n"));
        r.Add(EscPosCommands.Text("================================\n"));
        foreach (var item in items)
        {
            string name = item.ProductName ?? item.ProductCode;
            if (name.Length > 24) name = name.Substring(0, 24);
            r.Add(EscPosCommands.Text($"{name,-24}{Formatting.FormatCurrencyShort(item.Value),8}\n"));
        }
        r.Add(EscPosCommands.Text("================================\n"));
        r.Add(EscPosCommands.BoldOn);
        r.Add(EscPosCommands.Text($"TOTAL: {Formatting.FormatCurrency(sale.TotalValue),26}\n"));
        r.Add(EscPosCommands.BoldOff);
        if (sale.CashAmount > 0) r.Add(EscPosCommands.Text($"TUNAI: {Formatting.FormatCurrency(sale.CashAmount),26}\n"));
        if (sale.NonCash > 0) r.Add(EscPosCommands.Text($"KARTU: {Formatting.FormatCurrency(sale.NonCash),26}\n"));
        if (sale.ChangeAmount > 0) r.Add(EscPosCommands.Text($"KEMBALI: {Formatting.FormatCurrency(sale.ChangeAmount),24}\n"));
        r.Add(EscPosCommands.Text("================================\n"));
        r.Add(EscPosCommands.CenterAlign);
        r.Add(EscPosCommands.Text("Terima kasih!\n\n\n"));
        r.Add(EscPosCommands.PartialCut);
        return r.SelectMany(b => b).ToArray();
    }

    private void OpenCashDrawer()
    {
        try
        {
            string? p = _configRepo.Get("printer_name");
            if (string.IsNullOrEmpty(p)) return;
            var drawer = new CashDrawer(_configRepo);
            if (!drawer.Open() && !string.IsNullOrEmpty(drawer.LastError))
                FooterStatus.Show(StatusLabel, "⚠ Laci: " + drawer.LastError);
        }
        catch (Exception ex) { FooterStatus.Show(StatusLabel, "⚠ Laci error: " + ex.Message); }
    }

    // ── Inline product search ──────────────────────────────────────

    private void ShowSearch(bool byCode)
    {
        _searchByCode = byCode;
        LblSearchMode.Text = byCode ? "Cari Kode (F1):" : "Cari Nama (F2):";
        TxtSearchInput.Text = "";
        SearchPanel.IsVisible = true;
        TxtSearchInput.Focus();
        LoadSearchResults("");
    }

    private void HideSearch()
    {
        _debounce?.Stop();
        SearchPanel.IsVisible = false;
        _searchRows.Clear();
        TxtBarcode.Focus();
    }

    private void OnSearchTextChanged()
    {
        _debounce?.Stop();
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _debounce.Tick += (_, _) =>
        {
            _debounce?.Stop();
            LoadSearchResults(TxtSearchInput.Text?.Trim() ?? "");
        };
        _debounce.Start();
    }

    private void LoadSearchResults(string query)
    {
        _searchRows.Clear();
        if (string.IsNullOrEmpty(query)) return; // lazy: don't preload 24k products on F1/F2
        List<Product> results;
        if (_searchByCode) results = _productRepo.SearchByCodePrefix(query, 50);
        else
        {
            using var _ = PerfMetrics.Measure(PerfMetrics.ProductSearch);
            results = _productRepo.SearchByName(query, 50);
        }
        foreach (var p in results)
            _searchRows.Add(new SearchRow(p.ProductCode, p.Name, Formatting.FormatCurrencyShort(p.Price), p));
        if (_searchRows.Count > 0) DgvSearch.SelectedIndex = 0;
    }

    private void OnSearchInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (KeyboardRouter.IsEnter(e)) { e.Handled = true; SelectSearchResult(); }
        else if (KeyboardRouter.IsEscape(e)) { e.Handled = true; HideSearch(); }
        else if (e.Key == Key.Down && _searchRows.Count > 0) { e.Handled = true; DgvSearch.Focus(); }
    }

    private void OnSearchGridKeyDown(object? sender, KeyEventArgs e)
    {
        if (KeyboardRouter.IsEnter(e)) { e.Handled = true; SelectSearchResult(); }
        else if (KeyboardRouter.IsEscape(e)) { e.Handled = true; HideSearch(); }
    }

    private void SelectSearchResult()
    {
        if (DgvSearch.SelectedItem is SearchRow row)
        {
            HideSearch();
            AddItemByCode(row.Tag.ProductCode);
        }
    }

    // ── Keyboard handler ──────────────────────────────────────────

    protected override async void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        PerfMetrics.Record(PerfMetrics.KeypressEcho, 0); // key routed to handler — latency is sub-ms
        if (SearchPanel.IsVisible) return;
        if (KeyboardRouter.IsF1(e)) { e.Handled = true; ShowSearch(true); }
        else if (KeyboardRouter.IsF2(e)) { e.Handled = true; ShowSearch(false); }
        else if (KeyboardRouter.IsF3(e)) { e.Handled = true; await ChangeQty(); }
        else if (KeyboardRouter.IsF5(e))
        {
            e.Handled = true;
            using (var _ = PerfMetrics.Measure(PerfMetrics.F8PaymentVisible))
                OpenPayment();
        }
        else if (KeyboardRouter.IsF8(e)) { e.Handled = true; await VoidItem(); }
        else if (KeyboardRouter.IsF9(e))
        {
            e.Handled = true;
            await new CalculatorDialogWindow().ShowDialog(NavigationService.Owner);
            TxtBarcode.Focus();
        }
        else if (KeyboardRouter.IsF10(e))
        {
            e.Handled = true;
            if (_salesService.CurrentItems.Count > 0)
            {
                bool ok = await MsgBox.Confirm(NavigationService.Owner, "Batalkan seluruh transaksi?");
                if (ok) { _salesService.ClearCurrentSale(); RefreshGrid(); UpdateTotals(); FooterStatus.Show(StatusLabel, "Transaksi dibatalkan."); }
            }
        }
        else if (KeyboardRouter.IsF11(e)) { e.Handled = true; OpenCashDrawer(); FooterStatus.Show(StatusLabel, "Laci dibuka."); }
        else if (e.Key == Key.Add || e.Key == Key.OemPlus) { e.Handled = true; DoExactPayment(); }
        else if (KeyboardRouter.IsEscape(e))
        {
            e.Handled = true;
            string prompt = _salesService.CurrentItems.Count > 0
                ? "Tinggalkan transaksi yang sedang berjalan dan kembali ke menu utama?"
                : "Kembali ke menu utama?";
            bool ok = await MsgBox.Confirm(NavigationService.Owner, prompt);
            if (!ok) return;
            _clockTimer?.Stop();
            _debounce?.Stop();
            _bannerTimer?.Stop();
            NavigationService.GoHome();
        }
    }

    private async Task ChangeQty()
    {
        if (DgvItems.SelectedIndex < 0) return;
        int idx = DgvItems.SelectedIndex;
        var item = _salesService.CurrentItems[idx];
        var (ok, vals) = await InputDialogWindow.Show(NavigationService.Owner, "Ubah Qty",
            new[] { $"Qty baru untuk {item.ProductCode}" }, new[] { item.Quantity.ToString() });
        if (!ok) return;
        if (!int.TryParse(vals[0], out int qty) || qty <= 0) { await MsgBox.Show(NavigationService.Owner, "Qty tidak valid."); return; }
        _salesService.UpdateItemQty(idx, qty);
        RefreshGrid(); UpdateTotals();
    }

    private async Task VoidItem()
    {
        if (DgvItems.SelectedIndex < 0) return;
        int idx = DgvItems.SelectedIndex;
        bool ok = await MsgBox.Confirm(NavigationService.Owner, "Void item ini?");
        if (!ok) return;
        _salesService.RemoveItem(idx);
        RefreshGrid(); UpdateTotals();
    }
}
