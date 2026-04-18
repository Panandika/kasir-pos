using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Models;
using Kasir.Services;
using Kasir.Utils;
using Kasir.Avalonia.Forms.Shared;
using Kasir.Avalonia.Navigation;

namespace Kasir.Avalonia.Forms.Purchasing;

public partial class ReturnView : UserControl
{
    private record ReturnItemRow(string No, string Code, string Name, string Qty, string Price, string Total, PurchaseItem Tag);

    private readonly ObservableCollection<ReturnItemRow> _rows = new();
    private readonly List<PurchaseItem> _items = new();
    private readonly PurchasingService _service;
    private readonly SubsidiaryRepository _vendorRepo;
    private readonly ProductRepository _productRepo;
    private string _vendorCode = "";

    public ReturnView()
    {
        InitializeComponent();
        var conn = DbConnection.GetConnection();
        _service = new PurchasingService(conn, new ClockImpl());
        _vendorRepo = new SubsidiaryRepository(conn);
        _productRepo = new ProductRepository(conn);
        DgvItems.ItemsSource = _rows;
        TxtDate.Text = DateTime.Now.ToString("yyyy-MM-dd");
        SetStatus("Retur Pembelian — F2: Supplier, Ins: Tambah Item, Del: Hapus, F10: Simpan, Esc: Keluar");
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (KeyboardRouter.IsF2(e))          { e.Handled = true; SelectVendor(); }
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
        _vendorCode = vendor.SubCode;
        TxtVendor.Text = $"{vendor.SubCode} — {vendor.Name}";
        SetStatus($"Supplier: {vendor.Name}");
    }

    private async void AddItem()
    {
        var (ok1, codeVals) = await InputDialogWindow.Show(NavigationService.Owner, "Tambah Item Retur", new[] { "Kode Barang" }, new[] { "" });
        if (!ok1 || string.IsNullOrWhiteSpace(codeVals[0])) return;

        var product = _productRepo.GetByCode(codeVals[0].Trim().ToUpper());
        if (product == null) { await MsgBox.Show(NavigationService.Owner, "Barang tidak ditemukan."); return; }

        string defaultPrice = (product.BuyingPrice / 100.0).ToString("F0");
        var (ok2, vals) = await InputDialogWindow.Show(NavigationService.Owner, "Detail Retur",
            new[] { "Qty Retur", "Harga" },
            new[] { "1", defaultPrice });
        if (!ok2) return;

        if (!int.TryParse(vals[0], out int qty) || qty <= 0)
        { await MsgBox.Show(NavigationService.Owner, "Qty tidak valid."); return; }
        if (!decimal.TryParse(vals[1], out decimal price) || price < 0)
        { await MsgBox.Show(NavigationService.Owner, "Harga tidak valid."); return; }

        var item = new PurchaseItem
        {
            ProductCode = product.ProductCode,
            ProductName = product.Name,
            Quantity = qty,
            UnitPrice = (int)(price * 100m),
            Value = (long)(price * 100m) * qty
        };
        _items.Add(item);
        RefreshGrid();
    }

    private void DeleteItem()
    {
        var row = DgvItems.SelectedItem as ReturnItemRow;
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
            _rows.Add(new ReturnItemRow(
                no++.ToString(),
                item.ProductCode,
                item.ProductName,
                item.Quantity.ToString(),
                Formatting.FormatCurrencyShort(item.UnitPrice),
                Formatting.FormatCurrencyShort(item.Value),
                item));
            total += item.Value;
        }
        LblTotal.Text = $"TOTAL RETUR: {Formatting.FormatCurrency(total)}";
    }

    private async void Save()
    {
        if (string.IsNullOrEmpty(_vendorCode)) { await MsgBox.Show(NavigationService.Owner, "Pilih supplier."); return; }
        if (_items.Count == 0) { await MsgBox.Show(NavigationService.Owner, "Tambah item dulu."); return; }

        bool withInvoice = ChkWithInvoice.IsChecked == true;

        var ret = new Purchase
        {
            SubCode = _vendorCode,
            DocDate = TxtDate.Text?.Trim() ?? "",
            RefNo = TxtRefInvoice.Text?.Trim() ?? ""
        };
        string jnl = _service.CreatePurchaseReturn(ret, _items, withInvoice, 1);

        string msg = $"Retur disimpan: {jnl}\nStok disesuaikan.";
        if (withInvoice) msg += "\nAP offset diterapkan.";
        await MsgBox.Show(NavigationService.Owner, msg);

        _items.Clear();
        RefreshGrid();
        _vendorCode = "";
        TxtVendor.Text = "";
        TxtRefInvoice.Text = "";
        SetStatus("Retur Pembelian — F2: Supplier, Ins: Tambah Item, Del: Hapus, F10: Simpan, Esc: Keluar");
    }

    private void SetStatus(string text) => StatusLabel.Text = text;
}
