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

public partial class PurchaseInvoiceView : UserControl
{
    private record InvoiceItemRow(string No, string Ref, string Code, string Name, string Qty, string Unit, string Price, string Total, PurchaseItem Tag);

    private readonly ObservableCollection<InvoiceItemRow> _rows = new();
    private readonly List<PurchaseItem> _items = new();
    private readonly PurchasingService _service;
    private readonly SubsidiaryRepository _vendorRepo;
    private readonly ProductRepository _productRepo;
    private string _vendorCode = "";

    private readonly int _userId;

    public PurchaseInvoiceView(int userId)
    {
        _userId = userId;
        InitializeComponent();
        var conn = DbConnection.GetConnection();
        _service = new PurchasingService(conn, new ClockImpl());
        _vendorRepo = new SubsidiaryRepository(conn);
        _productRepo = new ProductRepository(conn);
        DgvItems.ItemsSource = _rows;
        TxtDate.Text = DateTime.Now.ToString("yyyy-MM-dd");
        TxtReceivedDate.Text = DateTime.Now.ToString("yyyy-MM-dd");
        FooterStatus.RegisterDefault(StatusLabel, "Nota Pembelian — F2: Supplier, F3: Ambil dari BPB/PO, Ins: Tambah, F4: Ubah, Del: Hapus, F10: Simpan, Esc: Keluar");

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
        else if (KeyboardRouter.IsF3(e))     { e.Handled = true; LoadFromDocument(); }
        else if (KeyboardRouter.IsF4(e))     { e.Handled = true; EditItem(); }
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
        if (vendor.SubCode != _vendorCode && _items.Any(i => !string.IsNullOrEmpty(i.OrderRef)))
        {
            _items.RemoveAll(i => !string.IsNullOrEmpty(i.OrderRef));
            RefreshGrid();
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

        string defaultPrice = Formatting.FormatRupiahCentsInput(product.BuyingPrice);
        var (ok2, vals) = await InputDialogWindow.Show(NavigationService.Owner, "Detail Item",
            new[] { "Qty", "Harga" },
            new[] { "1", defaultPrice });
        if (!ok2) return;

        if (!int.TryParse(vals[0], out int qty) || qty <= 0)
        { await MsgBox.Show(NavigationService.Owner, "Qty tidak valid."); return; }
        if (!Formatting.TryParseRupiahCents(vals[1], out long priceCents) || priceCents < 0)
        { await MsgBox.Show(NavigationService.Owner, "Harga tidak valid. Contoh: 11.208 atau 11.208,67"); return; }

        var item = new PurchaseItem
        {
            ProductCode = product.ProductCode,
            ProductName = product.Name,
            Quantity = qty,
            UnitPrice = priceCents,
            Value = priceCents * qty
        };
        _items.Add(item);
        RefreshGrid();
    }

    // Loads the unbilled lines of a goods receipt (BPB) or the unreceived lines of a PO.
    // BPB lines bill goods already in stock; PO lines receive and bill in one step.
    private async void LoadFromDocument()
    {
        if (string.IsNullOrEmpty(_vendorCode)) { await MsgBox.Show(NavigationService.Owner, "Pilih supplier dulu (F2)."); return; }

        var receipts = _service.GetUninvoicedReceipts(_vendorCode);
        var orders = _service.GetOpenPurchaseOrders(_vendorCode);
        if (receipts.Count == 0 && orders.Count == 0)
        { await MsgBox.Show(NavigationService.Owner, "Tidak ada BPB belum ditagih atau PO terbuka untuk supplier ini."); return; }

        var hints = receipts.Select(r => $"BPB {r.JournalNo}").Concat(orders.Select(o => $"PO {o.JournalNo}"));
        SetStatus("Bisa ditagih: " + string.Join(", ", hints));
        string suggested = receipts.Count > 0 ? receipts[0].JournalNo : orders[0].JournalNo;

        var (ok, vals) = await InputDialogWindow.Show(NavigationService.Owner, "Ambil dari BPB / PO", new[] { "No. BPB / PO" }, new[] { suggested });
        if (!ok || string.IsNullOrWhiteSpace(vals[0])) return;
        string refNo = vals[0].Trim().ToUpper();

        var lines = new List<PurchaseItem>();
        if (receipts.Any(r => r.JournalNo == refNo))
        {
            foreach (var line in _service.GetReceiptInvoiceStatus(refNo).Where(l => l.Uninvoiced > 0))
                lines.Add(NewLinkedItem(line.ProductCode, line.Uninvoiced, line.UnitPrice, refNo, 0));
        }
        else if (orders.Any(o => o.JournalNo == refNo))
        {
            foreach (var line in _service.GetOrderReceiptStatus(refNo).Where(l => l.Remaining > 0))
                lines.Add(NewLinkedItem(line.ProductCode, line.Remaining, line.UnitPrice, refNo, line.Ordered));
        }
        else
        {
            await MsgBox.Show(NavigationService.Owner, $"{refNo} bukan BPB belum ditagih / PO terbuka milik supplier ini.");
            return;
        }

        // Reloading replaces lines from the same document instead of adding them twice.
        _items.RemoveAll(i => i.OrderRef == refNo);
        _items.AddRange(lines);
        RefreshGrid();
        SetStatus($"{refNo} dimuat. F4 untuk sesuaikan qty/harga dengan nota supplier.");
    }

    private PurchaseItem NewLinkedItem(string productCode, int qty, long unitPrice, string refNo, int qtyOrder)
    {
        var product = _productRepo.GetByCode(productCode);
        return new PurchaseItem
        {
            ProductCode = productCode,
            ProductName = product?.Name ?? "",
            Unit = product?.Unit,
            Quantity = qty,
            UnitPrice = unitPrice,
            Value = unitPrice * qty,
            OrderRef = refNo,
            QtyOrder = qtyOrder
        };
    }

    private async void EditItem()
    {
        var row = DgvItems.SelectedItem as InvoiceItemRow;
        if (row == null) return;
        var item = row.Tag;

        string pricePrefill = Formatting.FormatRupiahCentsInput(item.UnitPrice);
        var (ok, vals) = await InputDialogWindow.Show(NavigationService.Owner, $"Ubah {item.ProductCode}",
            new[] { "Qty", "Harga" },
            new[] { item.Quantity.ToString(), pricePrefill });
        if (!ok) return;

        if (!int.TryParse(vals[0], out int qty) || qty <= 0)
        { await MsgBox.Show(NavigationService.Owner, "Qty tidak valid."); return; }
        if (!Formatting.TryParseRupiahCents(vals[1], out long priceCents) || priceCents < 0)
        { await MsgBox.Show(NavigationService.Owner, "Harga tidak valid. Contoh: 11.208 atau 11.208,67"); return; }

        item.Quantity = qty;
        item.UnitPrice = priceCents;
        item.Value = item.UnitPrice * qty;
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
                item.OrderRef ?? "",
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
        var match = _service.CheckInvoiceMatch(_vendorCode, _items);
        if (match.IsBlocked)
        {
            await MsgBox.Show(NavigationService.Owner, "Tidak bisa disimpan:\n" + string.Join("\n", match.Errors), "Tidak Cocok");
            return;
        }
        if (match.Warnings.Count > 0
            && !await MsgBox.Confirm(NavigationService.Owner, string.Join("\n", match.Warnings) + "\n\nTetap simpan?", "Harga Berbeda"))
            return;

        // Unlinked and PO-linked lines add stock. If this vendor has goods received on a BPB
        // that is not billed yet (and not fully billed by this invoice), the user probably
        // meant to bill that BPB — adding stock again would double count.
        bool addsStock = _items.Any(i => string.IsNullOrEmpty(i.OrderRef)
                                         || _service.GetLinkableDocType(i.OrderRef) == "PURCHASE_ORDER");
        var unbilled = addsStock
            ? _service.GetUninvoicedReceipts(_vendorCode).Where(r => !IsFullyBilledHere(r.JournalNo)).ToList()
            : new List<Purchase>();
        if (unbilled.Count > 0
            && !await MsgBox.Confirm(NavigationService.Owner,
                "Supplier ini punya BPB yang belum ditagih: " + string.Join(", ", unbilled.Select(r => r.JournalNo))
                + ".\nItem tanpa BPB (termasuk dari PO) akan MENAMBAH STOK lagi.\nGunakan F3 untuk menagih BPB.\n\nTetap simpan sebagai penerimaan baru?",
                "Cek BPB"))
            return;

        string jnl;
        try
        {
            jnl = _service.CreatePurchaseInvoice(invoice, _items, _userId);
        }
        catch (PurchaseValidationException ex)
        {
            await MsgBox.Show(NavigationService.Owner, "Tidak bisa disimpan:\n" + ex.Message, "Tidak Cocok");
            return;
        }
        bool stockAdded = _items.Any(i => string.IsNullOrEmpty(i.OrderRef) || _service.GetLinkableDocType(i.OrderRef) == "PURCHASE_ORDER");
        await MsgBox.Show(NavigationService.Owner, $"Invoice disimpan: {jnl}\nAP entry dibuat." + (stockAdded ? "\nStok diperbarui." : ""));
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

    // True when the lines on this invoice bill every unbilled unit of the given BPB.
    private bool IsFullyBilledHere(string receiptNo)
    {
        var billedHere = _items.Where(i => i.OrderRef == receiptNo)
            .GroupBy(i => i.ProductCode)
            .ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));
        return _service.GetReceiptInvoiceStatus(receiptNo)
            .All(l => l.Uninvoiced == 0 || (billedHere.TryGetValue(l.ProductCode, out int q) && q >= l.Uninvoiced));
    }

    private void SetStatus(string text) => FooterStatus.Show(StatusLabel, text);
}
