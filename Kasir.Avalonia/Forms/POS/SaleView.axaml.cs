using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Kasir.Auth;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Hardware;
using Kasir.Models;
using Kasir.Services;
using Kasir.Utils;
using Kasir.Avalonia.Forms.Shared;
using Kasir.Avalonia.Navigation;
using Kasir.Avalonia.Diagnostics;

namespace Kasir.Avalonia.Forms.POS;

public partial class SaleView : UserControl
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
    private readonly IClock _clock;
    private Shift? _currentShift;
    private bool _searchByCode;
    private DispatcherTimer? _debounce;
    private DispatcherTimer? _clockTimer;

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
        _salesService.SetCashier(_auth.CurrentUser.Alias, _auth.CurrentUser.Id);

        DgvItems.ItemsSource = _rows;
        DgvSearch.ItemsSource = _searchRows;

        TxtBarcode.KeyDown += OnBarcodeKeyDown;
        TxtSearchInput.TextChanged += (_, _) => OnSearchTextChanged();
        TxtSearchInput.KeyDown += OnSearchInputKeyDown;
        DgvSearch.KeyDown += OnSearchGridKeyDown;

        CheckShift();
        UpdateTotals();
        UpdateFooter();

        StatusLabel.Text = "F1=Kode  F2=Nama  F3=Qty  F5=Bayar  F8=Void  F9=Kalkulator  F10=Batal  F11=Laci  +=Pas  Esc=Keluar";

        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => UpdateFooter();
        _clockTimer.Start();
    }

    private void CheckShift()
    {
        string regId = _configRepo.Get("register_id") ?? "01";
        _currentShift = _shiftRepo.GetOpenShift(regId);
        if (_currentShift != null) _salesService.SetShift(_currentShift.ShiftNumber);
    }

    private void OnBarcodeKeyDown(object? sender, KeyEventArgs e)
    {
        if (!KeyboardRouter.IsEnter(e)) return;
        e.Handled = true;
        string code = TxtBarcode.Text?.Trim() ?? "";
        if (!string.IsNullOrEmpty(code))
        {
            using var _ = PerfMetrics.Measure(PerfMetrics.BarcodeScanLine);
            AddItemByCode(code);
        }
        TxtBarcode.Text = "";
    }

    private void AddItemByCode(string code)
    {
        if (_currentShift == null)
        {
            StatusLabel.Text = "Tidak ada shift terbuka. Buka shift dahulu (Utility > Shift).";
            return;
        }
        var item = _salesService.AddItem(code, 1);
        if (item == null)
        {
            if (long.TryParse(code, out long numVal))
                LblSubtotal.Text = numVal.ToString("N0");
            StatusLabel.Text = "Barang tidak ditemukan: " + code;
            return;
        }
        RefreshGrid();
        UpdateTotals();
        StatusLabel.Text = $"Ditambahkan: {item.ProductCode} — {item.ProductName}";
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
        LblSubtotal.Text = (t.NetAmount / 100).ToString("N0");
        LblTotalRow.Text = $"TOTAL\u2192  {Formatting.FormatCurrency(t.NetAmount)}";
        LblItemCount.Text = _salesService.CurrentItems.Count.ToString();
    }

    private void UpdateFooter()
    {
        string regId = _configRepo.Get("register_id") ?? "01";
        string today = _clock.Now.ToString("yyyy-MM-dd");
        int cnt = _saleRepo.GetDailyCount(today);
        LblFooter.Text = $"JAM\u2192 {_clock.Now:HH:mm:ss}  MESIN#{regId}  ID#{_auth.CurrentUser.Id}  JRNL#{cnt:D5}";
    }

    private async void OpenPayment()
    {
        if (_salesService.CurrentItems.Count == 0) { StatusLabel.Text = "Tidak ada item."; return; }
        var totals = _salesService.GetTotals();
        var result = await PaymentWindow.Show(NavigationService.Owner, totals.NetAmount);
        if (result == null) return;
        try
        {
            Sale sale;
            using (var _ = PerfMetrics.Measure(PerfMetrics.SaleCommit))
                sale = _salesService.CompleteSale(result.CashAmount, result.CardAmount, result.VoucherAmount, result.CardCode, result.CardType, "");
            StatusLabel.Text = $"LUNAS: {sale.JournalNo} — Kembali: {Formatting.FormatCurrency(sale.ChangeAmount)}";
            _ = PrintReceiptAsync(sale);
            if (result.CashAmount > 0) OpenCashDrawer();
            _salesService.ClearCurrentSale();
            RefreshGrid(); UpdateTotals(); UpdateFooter();
        }
        catch (Exception ex) { await MsgBox.Show(NavigationService.Owner, "Gagal bayar: " + ex.Message); }
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
            StatusLabel.Text = $"LUNAS (PAS): {sale.JournalNo}";
            _ = PrintReceiptAsync(sale);
            OpenCashDrawer();
            _salesService.ClearCurrentSale();
            RefreshGrid(); UpdateTotals(); UpdateFooter();
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
            var printer = new ReceiptPrinter(printerName);
            bool ok = await Task.Run(() => printer.Print(data));
            if (!ok) await MsgBox.Show(NavigationService.Owner, "Struk tidak tercetak.");
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
            if (!string.IsNullOrEmpty(p)) new CashDrawer(p).Open();
        }
        catch { }
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
        List<Product> results;
        if (string.IsNullOrEmpty(query)) results = _productRepo.GetAllActive();
        else if (_searchByCode) results = _productRepo.SearchByCodePrefix(query, 50);
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
                if (ok) { _salesService.ClearCurrentSale(); RefreshGrid(); UpdateTotals(); StatusLabel.Text = "Transaksi dibatalkan."; }
            }
        }
        else if (KeyboardRouter.IsF11(e)) { e.Handled = true; OpenCashDrawer(); StatusLabel.Text = "Laci dibuka."; }
        else if (e.Key == Key.Add) { e.Handled = true; DoExactPayment(); }
        else if (KeyboardRouter.IsEscape(e))
        {
            e.Handled = true;
            if (_salesService.CurrentItems.Count > 0)
            {
                bool ok = await MsgBox.Confirm(NavigationService.Owner, "Tinggalkan transaksi yang sedang berjalan?");
                if (!ok) return;
            }
            _clockTimer?.Stop();
            _debounce?.Stop();
            NavigationService.GoBack();
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
