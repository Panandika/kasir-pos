using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Models;
using Kasir.Services;
using Kasir.Utils;
using Kasir.Avalonia.Forms.Shared;
using Kasir.Avalonia.Navigation;
using Kasir.Avalonia.Utils;

namespace Kasir.Avalonia.Forms.Purchasing;

public partial class GoodsReceiptView : UserControl
{
    private record GoodsReceiptRow(string No, string Ref, string Code, string Name, string Qty, string Price, string Total, PurchaseItem Tag);

    private readonly ObservableCollection<GoodsReceiptRow> _rows = new();
    private readonly List<PurchaseItem> _items = new();
    private readonly PurchasingService _service;
    private readonly SubsidiaryRepository _vendorRepo;
    private readonly ProductRepository _productRepo;
    private string _vendorCode = "";
    private string _orderNo = "";

    private readonly int _userId;

    public GoodsReceiptView(int userId)
    {
        _userId = userId;
        InitializeComponent();
        var conn = DbConnection.GetConnection();
        _service = new PurchasingService(conn, new ClockImpl());
        _vendorRepo = new SubsidiaryRepository(conn);
        _productRepo = new ProductRepository(conn);
        DgvItems.ItemsSource = _rows;
        TxtDate.Text = DateTime.Now.ToString("yyyy-MM-dd");
        FooterStatus.RegisterDefault(StatusLabel, "Goods Receipt — F2: Supplier, F3: Ambil dari PO, Ins: Tambah, F4: Ubah, Del: Hapus, F10: Simpan, Esc: Keluar");
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (KeyboardRouter.IsF2(e))          { e.Handled = true; SelectVendor(); }
        else if (KeyboardRouter.IsF3(e))     { e.Handled = true; LoadFromOrder(); }
        else if (KeyboardRouter.IsF4(e))     { e.Handled = true; EditItem(); }
        else if (KeyboardRouter.IsInsert(e)) { e.Handled = true; AddItem(); }
        else if (KeyboardRouter.IsDelete(e)) { e.Handled = true; DeleteItem(); }
        else if (KeyboardRouter.IsF10(e))    { e.Handled = true; Save(); }
        else if (KeyboardRouter.IsEscape(e)) { e.Handled = true; NavigationService.GoBack(); }
    }

    private async void SelectVendor()
    {
        var (ok, vals) = await InputDialogWindow.Show(NavigationService.Owner, "Supplier", new[] { "Kode Supplier" }, new[] { "" });
        if (!ok || string.IsNullOrWhiteSpace(vals[0])) return;
        var vendor = _vendorRepo.GetByCode(vals[0].Trim().ToUpper());
        if (vendor == null) { await MsgBox.Show(NavigationService.Owner, "Supplier tidak ditemukan."); return; }
        if (vendor.SubCode != _vendorCode)
        {
            // A PO belongs to one vendor: drop its link and lines when the vendor changes.
            _orderNo = "";
            if (_items.RemoveAll(i => !string.IsNullOrEmpty(i.OrderRef)) > 0) RefreshGrid();
        }
        _vendorCode = vendor.SubCode;
        TxtVendor.Text = $"{vendor.SubCode} — {vendor.Name}";
        SetStatus($"Supplier: {vendor.Name}");
    }

    private async void AddItem()
    {
        var (ok1, codeVals) = await InputDialogWindow.Show(NavigationService.Owner, "Tambah Item", new[] { "Kode Barang" }, new[] { "" });
        if (!ok1 || string.IsNullOrWhiteSpace(codeVals[0])) return;

        var product = _productRepo.GetByCode(codeVals[0].Trim().ToUpper());
        if (product == null) { await MsgBox.Show(NavigationService.Owner, "Barang tidak ditemukan."); return; }

        string defaultPrice = (product.BuyingPrice / 100.0).ToString("F0");
        var (ok2, vals) = await InputDialogWindow.Show(NavigationService.Owner, "Detail Item",
            new[] { "Qty", "Harga Beli" },
            new[] { "1", defaultPrice });
        if (!ok2) return;

        if (!int.TryParse(vals[0], out int qty) || qty <= 0)
        { await MsgBox.Show(NavigationService.Owner, "Qty tidak valid."); return; }
        if (!Formatting.TryParseRupiah(vals[1], out long priceLong) || priceLong < 0)
        { await MsgBox.Show(NavigationService.Owner, "Harga tidak valid."); return; }
        decimal price = priceLong;

        var item = new PurchaseItem
        {
            ProductCode = product.ProductCode,
            ProductName = product.Name,
            Quantity = qty,
            UnitPrice = (long)(price * 100m),
            Value = (long)(price * 100m) * qty
        };
        // A product that is on the loaded PO counts toward it; anything else is an extra, unlinked line.
        if (!string.IsNullOrEmpty(_orderNo)
            && _service.GetOrderReceiptStatus(_orderNo).Any(l => l.ProductCode == product.ProductCode && l.Remaining > 0))
        {
            item.OrderRef = _orderNo;
        }
        _items.Add(item);
        RefreshGrid();
    }

    private async void LoadFromOrder()
    {
        if (string.IsNullOrEmpty(_vendorCode)) { await MsgBox.Show(NavigationService.Owner, "Pilih supplier dulu (F2)."); return; }

        var open = _service.GetOpenPurchaseOrders(_vendorCode);
        if (open.Count == 0) { await MsgBox.Show(NavigationService.Owner, "Tidak ada PO terbuka untuk supplier ini."); return; }
        SetStatus("PO terbuka: " + string.Join(", ", open.Select(o => $"{o.JournalNo} ({o.DocDate})")));

        var (ok, vals) = await InputDialogWindow.Show(NavigationService.Owner, "Ambil dari PO", new[] { "No. PO" }, new[] { open[0].JournalNo });
        if (!ok || string.IsNullOrWhiteSpace(vals[0])) return;
        string orderNo = vals[0].Trim().ToUpper();

        var order = open.FirstOrDefault(o => o.JournalNo == orderNo);
        if (order == null) { await MsgBox.Show(NavigationService.Owner, $"PO {orderNo} tidak ditemukan atau sudah diterima penuh untuk supplier ini."); return; }

        // Reloading replaces lines from the same PO instead of adding them twice.
        _items.RemoveAll(i => i.OrderRef == orderNo);
        foreach (var line in _service.GetOrderReceiptStatus(orderNo).Where(l => l.Remaining > 0))
        {
            var product = _productRepo.GetByCode(line.ProductCode);
            _items.Add(new PurchaseItem
            {
                ProductCode = line.ProductCode,
                ProductName = product?.Name ?? "",
                Quantity = line.Remaining,
                UnitPrice = line.UnitPrice,
                Value = line.UnitPrice * line.Remaining,
                OrderRef = orderNo,
                QtyOrder = line.Ordered
            });
        }
        _orderNo = orderNo;
        RefreshGrid();
        SetStatus($"PO {orderNo} dimuat — sisa qty diisi otomatis. F4 untuk ubah qty yang datang.");
    }

    private async void EditItem()
    {
        var row = DgvItems.SelectedItem as GoodsReceiptRow;
        if (row == null) return;
        var item = row.Tag;

        string pricePrefill = (item.UnitPrice / 100).ToString();
        var (ok, vals) = await InputDialogWindow.Show(NavigationService.Owner, $"Ubah {item.ProductCode}",
            new[] { "Qty", "Harga Beli" },
            new[] { item.Quantity.ToString(), pricePrefill });
        if (!ok) return;

        if (!int.TryParse(vals[0], out int qty) || qty <= 0)
        { await MsgBox.Show(NavigationService.Owner, "Qty tidak valid."); return; }
        if (!Formatting.TryParseRupiah(vals[1], out long priceLong) || priceLong < 0)
        { await MsgBox.Show(NavigationService.Owner, "Harga tidak valid."); return; }

        item.Quantity = qty;
        // Keep the exact (possibly sen) price unless the user actually typed a new one.
        if (vals[1].Trim() != pricePrefill) item.UnitPrice = priceLong * 100;
        item.Value = item.UnitPrice * qty;
        RefreshGrid();
    }

    private void DeleteItem()
    {
        var row = DgvItems.SelectedItem as GoodsReceiptRow;
        if (row == null) return;
        _items.Remove(row.Tag);
        RefreshGrid();
    }

    private void RefreshGrid()
    {
        _rows.Clear();
        long total = 0;
        int no = 1;
        foreach (var item in _items)
        {
            _rows.Add(new GoodsReceiptRow(
                no++.ToString(),
                item.OrderRef ?? "",
                item.ProductCode,
                item.ProductName,
                item.Quantity.ToString(),
                Formatting.FormatCurrencyShort(item.UnitPrice),
                Formatting.FormatCurrencyShort(item.Value),
                item));
            total += item.Value;
        }
        LblTotal.Text = $"TOTAL: {Formatting.FormatCurrency(total)}";
    }

    private async void Save()
    {
        if (string.IsNullOrEmpty(_vendorCode)) { await MsgBox.Show(NavigationService.Owner, "Pilih supplier."); return; }
        if (_items.Count == 0) { await MsgBox.Show(NavigationService.Owner, "Tambah item dulu."); return; }

        var receipt = new Purchase
        {
            SubCode = _vendorCode,
            DocDate = TxtDate.Text?.Trim() ?? "",
            RefNo = TxtInvoiceNo.Text?.Trim() ?? ""
        };
        // Unlinked lines don't count toward any PO; if the vendor has open POs the user
        // probably forgot F3, and the PO would stay open and later be received twice.
        if (_items.Any(i => string.IsNullOrEmpty(i.OrderRef)))
        {
            var openOrders = _service.GetOpenPurchaseOrders(_vendorCode);
            if (openOrders.Count > 0
                && !await MsgBox.Confirm(NavigationService.Owner,
                    "Supplier ini punya PO terbuka: " + string.Join(", ", openOrders.Select(o => o.JournalNo))
                    + ".\nItem tanpa PO tidak mengurangi sisa PO.\nGunakan F3 untuk menerima dari PO.\n\nTetap simpan tanpa PO?",
                    "Cek PO"))
                return;
        }

        string jnl;
        try
        {
            jnl = _service.CreateGoodsReceipt(receipt, _items, _userId);
        }
        catch (PurchaseValidationException ex)
        {
            await MsgBox.Show(NavigationService.Owner, "Tidak bisa disimpan:\n" + ex.Message, "Tidak Cocok dengan PO");
            return;
        }
        await MsgBox.Show(NavigationService.Owner, $"Goods Receipt disimpan: {jnl}\nStok diperbarui.");
        _items.Clear();
        RefreshGrid();
        _orderNo = "";
        _vendorCode = "";
        TxtVendor.Text = "";
        TxtInvoiceNo.Text = "";
        FooterStatus.Reset(StatusLabel);
    }

    private void SetStatus(string text) => FooterStatus.Show(StatusLabel, text);
}
