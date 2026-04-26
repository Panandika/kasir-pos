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
using Kasir.Avalonia.Utils;

namespace Kasir.Avalonia.Forms.Purchasing;

public partial class PurchaseInvoiceView : UserControl
{
    private record InvoiceItemRow(string No, string Code, string Name, string Qty, string Unit, string Price, string Total, PurchaseItem Tag);

    private readonly ObservableCollection<InvoiceItemRow> _rows = new();
    private readonly List<PurchaseItem> _items = new();
    private readonly PurchasingService _service;
    private readonly SubsidiaryRepository _vendorRepo;
    private readonly ProductRepository _productRepo;
    private string _vendorCode = "";

    public PurchaseInvoiceView()
    {
        InitializeComponent();
        var conn = DbConnection.GetConnection();
        _service = new PurchasingService(conn, new ClockImpl());
        _vendorRepo = new SubsidiaryRepository(conn);
        _productRepo = new ProductRepository(conn);
        DgvItems.ItemsSource = _rows;
        TxtDate.Text = DateTime.Now.ToString("yyyy-MM-dd");
        TxtReceivedDate.Text = DateTime.Now.ToString("yyyy-MM-dd");
        FooterStatus.RegisterDefault(StatusLabel, "Nota Pembelian — F2: Supplier, Ins: Tambah Item, Del: Hapus, F10: Simpan (buat AP), Esc: Keluar");

        // Auto-compute due date when terms changes
        TxtTerms.TextChanged += (_, _) => UpdateDueDate();
        TxtDate.TextChanged += (_, _) => UpdateDueDate();
        TxtDiscPct.TextChanged += (_, _) => UpdateTotals();
        TxtVatFlag.TextChanged += (_, _) => UpdateTotals();
        UpdateDueDate();
        UpdateTotals();
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

    private void UpdateDueDate()
    {
        if (DateTime.TryParse(TxtDate.Text, out var docDate) &&
            int.TryParse(TxtTerms.Text, out int terms) && terms >= 0)
        {
            TxtDueDate.Text = docDate.AddDays(terms).ToString("yyyy-MM-dd");
        }
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
        var (ok1, codeVals) = await InputDialogWindow.Show(NavigationService.Owner, "Tambah Item", new[] { "Kode Barang" }, new[] { "" });
        if (!ok1 || string.IsNullOrWhiteSpace(codeVals[0])) return;

        var product = _productRepo.GetByCode(codeVals[0].Trim().ToUpper());
        if (product == null) { await MsgBox.Show(NavigationService.Owner, "Barang tidak ditemukan."); return; }

        string defaultPrice = (product.BuyingPrice / 100.0).ToString("F0");
        var (ok2, vals) = await InputDialogWindow.Show(NavigationService.Owner, "Detail Item",
            new[] { "Qty", "Harga" },
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
            UnitPrice = (long)(price * 100m),
            Value = (long)(price * 100m) * qty
        };
        _items.Add(item);
        RefreshGrid();
    }

    private void DeleteItem()
    {
        var row = DgvItems.SelectedItem as InvoiceItemRow;
        if (row == null) return;
        _items.Remove(row.Tag);
        RefreshGrid();
    }

    private void RefreshGrid()
    {
        _rows.Clear();
        int no = 1;
        foreach (var item in _items)
        {
            _rows.Add(new InvoiceItemRow(
                no++.ToString(),
                item.ProductCode,
                item.ProductName,
                item.Quantity.ToString(),
                item.Unit ?? "",
                Formatting.FormatCurrencyShort(item.UnitPrice),
                Formatting.FormatCurrencyShort(item.Value),
                item));
        }
        UpdateTotals();
    }

    private (long gross, long disc, long vat, long netto) ComputeTotals()
    {
        long gross = 0;
        foreach (var item in _items) gross += item.Value;

        int discPct = 0;
        int.TryParse(TxtDiscPct.Text, out discPct);
        if (discPct < 0) discPct = 0;
        if (discPct > 100) discPct = 100;
        long disc = gross * discPct / 100;
        long afterDisc = gross - disc;

        // Indonesian PPN 11% applies when VatFlag == "Y"
        string vatFlag = (TxtVatFlag.Text ?? "N").Trim().ToUpper();
        long vat = vatFlag == "Y" ? afterDisc * 11 / 100 : 0;

        long netto = afterDisc + vat;
        return (gross, disc, vat, netto);
    }

    private void UpdateTotals()
    {
        var (gross, disc, vat, netto) = ComputeTotals();
        LblGross.Text = $"TOTAL BELI: {Formatting.FormatCurrency(gross)}";
        LblDisc.Text  = $"TOTAL DISC: {Formatting.FormatCurrency(disc)}";
        LblVat.Text   = $"TOTAL PPN: {Formatting.FormatCurrency(vat)}";
        LblNetto.Text = $"TOTAL NETTO: {Formatting.FormatCurrency(netto)}";
    }

    private async void Save()
    {
        if (string.IsNullOrEmpty(_vendorCode)) { await MsgBox.Show(NavigationService.Owner, "Pilih supplier."); return; }
        if (_items.Count == 0) { await MsgBox.Show(NavigationService.Owner, "Tambah item dulu."); return; }

        if (!int.TryParse(TxtTerms.Text, out int terms)) terms = 30;
        if (!int.TryParse(TxtDiscPct.Text, out int discPct)) discPct = 0;
        if (discPct < 0) discPct = 0;
        if (discPct > 100) discPct = 100;

        var (grossAmount, disc, vat, _) = ComputeTotals();
        string vatFlag = (TxtVatFlag.Text ?? "N").Trim().ToUpper();
        if (vatFlag != "Y") vatFlag = "N";

        var invoice = new Purchase
        {
            SubCode = _vendorCode,
            DocDate = TxtDate.Text?.Trim() ?? "",
            DueDate = TxtDueDate.Text?.Trim() ?? "",
            ReceivedDate = TxtReceivedDate.Text?.Trim() ?? "",
            Terms = terms,
            TaxInvoice = TxtTaxInvoice.Text?.Trim() ?? "",
            DeliveryNote = TxtDeliveryNote.Text?.Trim() ?? "",
            RefNo = TxtRefNo.Text?.Trim() ?? "",
            TaxInvDate = TxtTaxInvDate.Text?.Trim() ?? "",
            Warehouse = TxtWarehouse.Text?.Trim() ?? "",
            DiscPct = discPct,
            VatFlag = vatFlag,
            GrossAmount = grossAmount,
            TotalDisc = disc,
            VatAmount = vat
        };
        string jnl = _service.CreatePurchaseInvoice(invoice, _items, 1);
        await MsgBox.Show(NavigationService.Owner, $"Invoice disimpan: {jnl}\nAP entry dibuat.");
        _items.Clear();
        RefreshGrid();
        _vendorCode = "";
        TxtVendor.Text = "";
        TxtTaxInvoice.Text = "";
        TxtDeliveryNote.Text = "";
        TxtRefNo.Text = "";
        TxtTaxInvDate.Text = "";
        TxtReceivedDate.Text = DateTime.Now.ToString("yyyy-MM-dd");
        TxtWarehouse.Text = "";
        TxtDiscPct.Text = "0";
        TxtVatFlag.Text = "N";
        FooterStatus.Reset(StatusLabel);
    }

    private void SetStatus(string text) => FooterStatus.Show(StatusLabel, text);
}
