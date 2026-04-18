using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Models;
using Kasir.Utils;
using Kasir.Avalonia.Forms.Shared;
using ClosedXML.Excel;

namespace Kasir.Avalonia.Forms.Reports;

public partial class SalesReportWindow : Window
{
    private record SaleRow(int No, string JournalNo, string Date, string Cashier, int Items,
        string Gross, string Disc, string Total, string Cash, string Card, string Status);

    private readonly ObservableCollection<SaleRow> _rows = new();
    private readonly SaleRepository _saleRepo;

    public SalesReportWindow()
    {
        InitializeComponent();
        _saleRepo = new SaleRepository(DbConnection.GetConnection());
        DgvReport.ItemsSource = _rows;
        TxtDateFrom.Text = DateTime.Now.ToString("yyyy-MM-dd");
        TxtDateTo.Text = DateTime.Now.ToString("yyyy-MM-dd");
        StatusLabel.Text = "Laporan Penjualan — F5=Generate  F7=Export Excel  Esc=Keluar";
        BtnGenerate.Click += (_, _) => GenerateReport();
        BtnExport.Click += (_, _) => ExportReport();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (KeyboardRouter.IsF5(e)) { e.Handled = true; GenerateReport(); }
        else if (KeyboardRouter.IsF7(e)) { e.Handled = true; ExportReport(); }
        else if (KeyboardRouter.IsEscape(e)) { e.Handled = true; Close(); }
    }

    private void GenerateReport()
    {
        _rows.Clear();
        var sales = _saleRepo.GetByDateRange(TxtDateFrom.Text.Trim(), TxtDateTo.Text.Trim());
        long total = 0;
        int no = 1;
        foreach (var sale in sales)
        {
            var items = _saleRepo.GetItemsByJournalNo(sale.JournalNo);
            string status = sale.Control == 3 ? "VOID" : "OK";
            _rows.Add(new SaleRow(no++, sale.JournalNo, Formatting.FormatDate(sale.DocDate),
                sale.Cashier, items.Count,
                Formatting.FormatCurrencyShort(sale.GrossAmount),
                Formatting.FormatCurrencyShort(sale.TotalDisc),
                Formatting.FormatCurrencyShort(sale.TotalValue),
                Formatting.FormatCurrencyShort(sale.CashAmount),
                Formatting.FormatCurrencyShort(sale.NonCash),
                status));
            if (sale.Control != 3) total += sale.TotalValue;
        }
        LblSummary.Text = $"GRAND TOTAL: {Formatting.FormatCurrency(total)}  ({sales.Count} transaksi)";
    }

    private async void ExportReport()
    {
        if (_rows.Count == 0) { await MsgBox.Show(this, "Generate report dahulu."); return; }
        var file = await this.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Excel",
            SuggestedFileName = $"Penjualan_{TxtDateFrom.Text}_{TxtDateTo.Text}",
            FileTypeChoices = new[] { new FilePickerFileType("Excel") { Patterns = new[] { "*.xlsx" } } }
        });
        if (file == null) return;
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Penjualan");
        var headers = new[] { "No", "No.Nota", "Tanggal", "Kasir", "Items", "Bruto", "Diskon", "Total", "Tunai", "Kartu", "Status" };
        for (int c = 0; c < headers.Length; c++) ws.Cell(1, c + 1).Value = headers[c];
        int row = 2;
        foreach (var r in _rows)
        {
            ws.Cell(row, 1).Value = r.No;
            ws.Cell(row, 2).Value = r.JournalNo;
            ws.Cell(row, 3).Value = r.Date;
            ws.Cell(row, 4).Value = r.Cashier;
            ws.Cell(row, 5).Value = r.Items;
            ws.Cell(row, 6).Value = r.Gross;
            ws.Cell(row, 7).Value = r.Disc;
            ws.Cell(row, 8).Value = r.Total;
            ws.Cell(row, 9).Value = r.Cash;
            ws.Cell(row, 10).Value = r.Card;
            ws.Cell(row, 11).Value = r.Status;
            row++;
        }
        wb.SaveAs(file.Path.LocalPath);
        await MsgBox.Show(this, "Export berhasil: " + file.Name);
    }
}
