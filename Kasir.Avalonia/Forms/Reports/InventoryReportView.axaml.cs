using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Models;
using Kasir.Services;
using Kasir.Utils;
using Kasir.Avalonia.Forms.Shared;
using Kasir.Avalonia.Navigation;
using ClosedXML.Excel;

namespace Kasir.Avalonia.Forms.Reports;

public partial class InventoryReportView : UserControl
{
    private record InvRow(string C1, string C2, string C3, string C4, string C5,
        string C6, string C7, string C8, string C9);

    private readonly ObservableCollection<InvRow> _rows = new();

    public InventoryReportView(int preSelectedIndex = 0)
    {
        InitializeComponent();
        DgvReport.ItemsSource = _rows;
        CboReportType.ItemsSource = new[]
        {
            "Stock Position",
            "Purchase Register",
            "Purchase Returns",
            "Transfers",
            "Stock Out (Usage/Damage/Loss)",
            "Stock Opname",
            "Price History"
        };
        CboReportType.SelectedIndex = preSelectedIndex;
        TxtDateFrom.Text = DateTime.Now.ToString("yyyy-MM-dd");
        TxtDateTo.Text = DateTime.Now.ToString("yyyy-MM-dd");
        StatusLabel.Text = "Laporan Inventori — F5=Generate  F7=Export  Esc=Keluar";
        BtnGenerate.Click += (_, _) => GenerateReport();
        BtnExport.Click += (_, _) => ExportReport();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (KeyboardRouter.IsF5(e)) { e.Handled = true; GenerateReport(); }
        else if (KeyboardRouter.IsF7(e)) { e.Handled = true; ExportReport(); }
        else if (KeyboardRouter.IsEscape(e)) { e.Handled = true; NavigationService.GoBack(); }
    }

    private void SetColumns(string[] headers, string[] bindings, double[] stars)
    {
        DgvReport.Columns.Clear();
        for (int i = 0; i < headers.Length; i++)
        {
            var col = new DataGridTextColumn
            {
                Header = headers[i],
                Binding = new global::Avalonia.Data.Binding(bindings[i]),
                Width = new DataGridLength(stars[i], DataGridLengthUnitType.Star)
            };
            DgvReport.Columns.Add(col);
        }
    }

    private void GenerateReport()
    {
        _rows.Clear();
        switch (CboReportType.SelectedIndex)
        {
            case 0: GenerateStockPosition(); break;
            case 1: GeneratePurchaseRegister(); break;
            case 2: GeneratePurchaseReturns(); break;
            case 3: GenerateTransfers(); break;
            case 4: GenerateStockOut(); break;
            case 5: GenerateStockOpname(); break;
            case 6: GeneratePriceHistory(); break;
        }
    }

    private void GenerateStockPosition()
    {
        SetColumns(
            new[] { "Kode", "Nama", "Stok", "HPP", "Nilai" },
            new[] { "C1", "C2", "C3", "C4", "C5" },
            new[] { 1.5, 3.0, 1.0, 1.5, 1.5 });
        var db = DbConnection.GetConnection();
        var products = new ProductRepository(db).GetAllActive();
        var invSvc = new InventoryService(db);
        foreach (var p in products)
        {
            int stock = invSvc.GetStockOnHand(p.ProductCode);
            long cost = invSvc.CalculateAverageCost(p.ProductCode);
            long value = stock * cost;
            _rows.Add(new InvRow(p.ProductCode, p.Name,
                stock.ToString(),
                Formatting.FormatCurrencyShort(cost),
                Formatting.FormatCurrencyShort(value),
                "", "", "", ""));
        }
        LblSummary.Text = $"{products.Count} produk";
    }

    private void GeneratePurchaseRegister()
    {
        SetColumns(
            new[] { "No.Faktur", "Tanggal", "Supplier", "Total" },
            new[] { "C1", "C2", "C3", "C4" },
            new[] { 2.0, 1.5, 3.0, 1.5 });
        var purchases = new PurchaseRepository(DbConnection.GetConnection())
            .GetByDateRange(TxtDateFrom.Text?.Trim() ?? "", TxtDateTo.Text?.Trim() ?? "", "PURCHASE");
        long total = 0;
        foreach (var p in purchases)
        {
            total += p.TotalValue;
            _rows.Add(new InvRow(p.JournalNo, Formatting.FormatDate(p.DocDate),
                p.SubCode, Formatting.FormatCurrencyShort(p.TotalValue),
                "", "", "", "", ""));
        }
        LblSummary.Text = $"{purchases.Count} faktur  Total: {Formatting.FormatCurrency(total)}";
    }

    private void GeneratePurchaseReturns()
    {
        SetColumns(
            new[] { "No.Retur", "Tanggal", "Supplier", "Total" },
            new[] { "C1", "C2", "C3", "C4" },
            new[] { 2.0, 1.5, 3.0, 1.5 });
        var purchases = new PurchaseRepository(DbConnection.GetConnection())
            .GetByDateRange(TxtDateFrom.Text?.Trim() ?? "", TxtDateTo.Text?.Trim() ?? "", "PURCHASE_RETURN");
        long total = 0;
        foreach (var p in purchases)
        {
            total += p.TotalValue;
            _rows.Add(new InvRow(p.JournalNo, Formatting.FormatDate(p.DocDate),
                p.SubCode, Formatting.FormatCurrencyShort(p.TotalValue),
                "", "", "", "", ""));
        }
        LblSummary.Text = $"{purchases.Count} retur  Total: {Formatting.FormatCurrency(total)}";
    }

    private void GenerateTransfers()
    {
        SetColumns(
            new[] { "No.Transfer", "Tanggal", "Dari", "Ke" },
            new[] { "C1", "C2", "C3", "C4" },
            new[] { 2.0, 1.5, 2.0, 2.0 });
        var transfers = new StockTransferRepository(DbConnection.GetConnection())
            .GetByDateRange(TxtDateFrom.Text?.Trim() ?? "", TxtDateTo.Text?.Trim() ?? "");
        foreach (var t in transfers)
        {
            _rows.Add(new InvRow(t.JournalNo, Formatting.FormatDate(t.DocDate),
                t.FromLocation, t.ToLocation,
                "", "", "", "", ""));
        }
        LblSummary.Text = $"{transfers.Count} transfer";
    }

    private void GenerateStockOut()
    {
        SetColumns(
            new[] { "No.Dokumen", "Tanggal", "Jenis", "Kode", "Nama", "Qty", "Harga", "Nilai", "Ket" },
            new[] { "C1", "C2", "C3", "C4", "C5", "C6", "C7", "C8", "C9" },
            new[] { 1.5, 1.2, 1.0, 1.2, 2.5, 0.7, 1.2, 1.2, 1.5 });
        var items = new StockAdjustmentRepository(DbConnection.GetConnection())
            .GetAllItemsByDateRange(TxtDateFrom.Text?.Trim() ?? "", TxtDateTo.Text?.Trim() ?? "");
        foreach (var item in items)
        {
            _rows.Add(new InvRow(
                item.JournalNo,
                Formatting.FormatDate(item.DocDate),
                item.DocType,
                item.ProductCode,
                item.ProductName,
                item.Quantity.ToString(),
                Formatting.FormatCurrencyShort(item.CostPrice),
                Formatting.FormatCurrencyShort(item.Value),
                item.Reason ?? ""));
        }
        LblSummary.Text = $"{items.Count} item";
    }

    private void GenerateStockOpname()
    {
        SetColumns(
            new[] { "Kode", "Nama", "Stok Sistem", "Stok Fisik", "Selisih", "HPP", "Nilai Selisih" },
            new[] { "C1", "C2", "C3", "C4", "C5", "C6", "C7" },
            new[] { 1.5, 3.0, 1.2, 1.2, 1.0, 1.2, 1.5 });
        var opname = new StockAdjustmentRepository(DbConnection.GetConnection())
            .GetOpnameByDateRange(TxtDateFrom.Text?.Trim() ?? "", TxtDateTo.Text?.Trim() ?? "");
        long totalVariance = 0;
        foreach (var o in opname)
        {
            totalVariance += o.VarianceValue;
            _rows.Add(new InvRow(
                o.ProductCode, o.ProductName,
                o.QtySystem.ToString(), o.QtyActual.ToString(),
                o.Variance.ToString(),
                Formatting.FormatCurrencyShort(o.CostPrice),
                Formatting.FormatCurrencyShort(o.VarianceValue),
                "", ""));
        }
        LblSummary.Text = $"{opname.Count} item  Nilai Selisih: {Formatting.FormatCurrency(totalVariance)}";
    }

    private void GeneratePriceHistory()
    {
        SetColumns(
            new[] { "Tanggal", "Kode", "Nama", "Harga Lama", "Harga Baru", "Supplier", "No.Dokumen" },
            new[] { "C1", "C2", "C3", "C4", "C5", "C6", "C7" },
            new[] { 1.2, 1.2, 2.5, 1.2, 1.2, 1.5, 1.5 });
        var db = DbConnection.GetConnection();
        var rows = SqlHelper.Query(db,
            @"SELECT ph.changed_at, ph.product_code, p.name, ph.old_price, ph.new_price,
                     ph.sub_code, ph.journal_no
              FROM price_history ph
              LEFT JOIN products p ON ph.product_code = p.product_code
              WHERE ph.changed_at >= @from AND ph.changed_at <= @to
              ORDER BY ph.changed_at",
            r => new InvRow(
                SqlHelper.GetString(r, "changed_at"),
                SqlHelper.GetString(r, "product_code"),
                SqlHelper.GetString(r, "name"),
                Formatting.FormatCurrencyShort(SqlHelper.GetLong(r, "old_price")),
                Formatting.FormatCurrencyShort(SqlHelper.GetLong(r, "new_price")),
                SqlHelper.GetString(r, "sub_code"),
                SqlHelper.GetString(r, "journal_no"),
                "",
                ""),
            SqlHelper.Param("@from", TxtDateFrom.Text?.Trim() ?? ""),
            SqlHelper.Param("@to", TxtDateTo.Text?.Trim() ?? ""));
        foreach (var r in rows) _rows.Add(r);
        LblSummary.Text = $"{rows.Count} perubahan harga";
    }

    private string[] GetCurrentHeaders()
    {
        switch (CboReportType.SelectedIndex)
        {
            case 0: return new[] { "Kode", "Nama", "Stok", "HPP", "Nilai" };
            case 1: return new[] { "No.Faktur", "Tanggal", "Supplier", "Total" };
            case 2: return new[] { "No.Retur", "Tanggal", "Supplier", "Total" };
            case 3: return new[] { "No.Transfer", "Tanggal", "Dari", "Ke" };
            case 4: return new[] { "No.Dokumen", "Tanggal", "Jenis", "Kode", "Nama", "Qty", "Harga", "Nilai", "Ket" };
            case 5: return new[] { "Kode", "Nama", "Stok Sistem", "Stok Fisik", "Selisih", "HPP", "Nilai Selisih" };
            case 6: return new[] { "Tanggal", "Kode", "Nama", "Harga Lama", "Harga Baru", "Supplier", "No.Dokumen" };
            default: return new[] { "C1", "C2", "C3", "C4", "C5", "C6", "C7", "C8", "C9" };
        }
    }

    private async void ExportReport()
    {
        if (_rows.Count == 0) { await MsgBox.Show(NavigationService.Owner, "Generate report dahulu."); return; }
        var file = await NavigationService.Owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Excel",
            SuggestedFileName = $"Inventori_{CboReportType.SelectedIndex}",
            FileTypeChoices = new[] { new FilePickerFileType("Excel") { Patterns = new[] { "*.xlsx" } } }
        });
        if (file == null) return;
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Inventori");
        var headers = GetCurrentHeaders();
        for (int c = 0; c < headers.Length; c++) ws.Cell(1, c + 1).Value = headers[c];
        var colProps = new System.Func<InvRow, string>[]
        {
            r => r.C1, r => r.C2, r => r.C3, r => r.C4, r => r.C5,
            r => r.C6, r => r.C7, r => r.C8, r => r.C9
        };
        int row = 2;
        foreach (var r in _rows)
        {
            for (int c = 0; c < headers.Length && c < colProps.Length; c++)
                ws.Cell(row, c + 1).Value = colProps[c](r);
            row++;
        }
        wb.SaveAs(file.Path.LocalPath);
        await MsgBox.Show(NavigationService.Owner, "Export berhasil: " + file.Name);
    }
}
