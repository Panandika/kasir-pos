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
using ClosedXML.Excel;

namespace Kasir.Avalonia.Forms.Reports;

public partial class FinancialReportWindow : Window
{
    private record FinRow(string Code, string Name, string Col3, string Col4, string Col5, string Col6);

    private readonly ObservableCollection<FinRow> _rows = new();
    private readonly AccountRepository _accountRepo;
    private readonly AccountBalanceRepository _balanceRepo;
    private readonly GlDetailRepository _glRepo;
    private readonly PayablesService _payablesService;

    public FinancialReportWindow()
    {
        InitializeComponent();
        var db = DbConnection.GetConnection();
        _accountRepo = new AccountRepository(db);
        _balanceRepo = new AccountBalanceRepository(db);
        _glRepo = new GlDetailRepository(db);
        _payablesService = new PayablesService(db);
        DgvReport.ItemsSource = _rows;
        CboReportType.ItemsSource = new[]
        {
            "Neraca Saldo (Trial Balance)",
            "Neraca (Balance Sheet)",
            "Laba/Rugi (Profit & Loss)",
            "Aging Hutang (AP Aging)",
            "Buku Besar (GL Detail)"
        };
        CboReportType.SelectedIndex = 0;
        TxtPeriod.Text = DateTime.Now.ToString("yyyyMM");
        StatusLabel.Text = "Laporan Keuangan — F5=Cetak  F10=Export Excel  Esc=Keluar";
        BtnGenerate.Click += (_, _) => GenerateReport();
        BtnExport.Click += (_, _) => ExportReport();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (KeyboardRouter.IsF5(e)) { e.Handled = true; GenerateReport(); }
        else if (KeyboardRouter.IsF10(e)) { e.Handled = true; ExportReport(); }
        else if (KeyboardRouter.IsEscape(e)) { e.Handled = true; Close(); }
    }

    private void SetColumns(string[] headers, double[] stars)
    {
        DgvReport.Columns.Clear();
        string[] bindings = new[] { "Code", "Name", "Col3", "Col4", "Col5", "Col6" };
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
            case 0: GenerateTrialBalance(); break;
            case 1: GenerateBalanceSheet(); break;
            case 2: GenerateProfitLoss(); break;
            case 3: GenerateApAging(); break;
            case 4: GenerateGlDetail(); break;
        }
    }

    private void GenerateTrialBalance()
    {
        SetColumns(
            new[] { "Kode", "Nama", "Debit", "Kredit" },
            new[] { 1.2, 3.0, 1.5, 1.5 });
        string period = TxtPeriod.Text?.Trim() ?? "";
        var accounts = _accountRepo.GetDetailAccounts();
        long totalDebit = 0;
        long totalCredit = 0;
        foreach (var acc in accounts)
        {
            var bal = _balanceRepo.GetBalance(acc.AccountCode, period);
            if (bal == null) continue;
            long net = bal.OpeningBalance + bal.DebitTotal - bal.CreditTotal;
            string debit = "";
            string credit = "";
            if (acc.NormalBalance == "D")
            {
                if (net >= 0) { debit = Formatting.FormatCurrencyShort(net); totalDebit += net; }
                else { credit = Formatting.FormatCurrencyShort(-net); totalCredit += -net; }
            }
            else
            {
                if (net >= 0) { credit = Formatting.FormatCurrencyShort(net); totalCredit += net; }
                else { debit = Formatting.FormatCurrencyShort(-net); totalDebit += -net; }
            }
            _rows.Add(new FinRow(acc.AccountCode, acc.AccountName, debit, credit, "", ""));
        }
        string balanced = totalDebit == totalCredit ? "BALANCE" : "TIDAK BALANCE";
        LblTotal.Text = $"Total Debit: {Formatting.FormatCurrency(totalDebit)}  Kredit: {Formatting.FormatCurrency(totalCredit)}  [{balanced}]";
    }

    private void GenerateBalanceSheet()
    {
        SetColumns(
            new[] { "Kode", "Nama", "Jumlah" },
            new[] { 1.2, 3.5, 1.5 });
        string period = TxtPeriod.Text?.Trim() ?? "";
        long totalAssets = 0;
        long totalLiabEquity = 0;

        _rows.Add(new FinRow("", "=== AKTIVA ===", "", "", "", ""));
        totalAssets = AddGroupRows(1, period);
        _rows.Add(new FinRow("", $"Total Aktiva: {Formatting.FormatCurrencyShort(totalAssets)}", "", "", "", ""));

        _rows.Add(new FinRow("", "=== PASIVA ===", "", "", "", ""));
        long liab = AddGroupRows(2, period);
        long equity = AddGroupRows(3, period);
        totalLiabEquity = liab + equity;
        _rows.Add(new FinRow("", $"Total Pasiva: {Formatting.FormatCurrencyShort(totalLiabEquity)}", "", "", "", ""));

        string balanced = totalAssets == totalLiabEquity ? "BALANCE" : "TIDAK BALANCE";
        LblTotal.Text = $"Aktiva: {Formatting.FormatCurrency(totalAssets)}  Pasiva: {Formatting.FormatCurrency(totalLiabEquity)}  [{balanced}]";
    }

    private long AddGroupRows(int group, string period)
    {
        var accounts = _accountRepo.GetByGroup(group);
        long groupTotal = 0;
        foreach (var acc in accounts)
        {
            if (acc.IsDetail != 1) continue;
            var bal = _balanceRepo.GetBalance(acc.AccountCode, period);
            if (bal == null) continue;
            long net = bal.OpeningBalance + bal.DebitTotal - bal.CreditTotal;
            if (acc.NormalBalance == "C") net = -net;
            groupTotal += net;
            _rows.Add(new FinRow(acc.AccountCode, acc.AccountName, Formatting.FormatCurrencyShort(net), "", "", ""));
        }
        return groupTotal;
    }

    private void GenerateProfitLoss()
    {
        SetColumns(
            new[] { "Kode", "Nama", "Jumlah" },
            new[] { 1.2, 3.5, 1.5 });
        string period = TxtPeriod.Text?.Trim() ?? "";

        _rows.Add(new FinRow("", "=== PENDAPATAN ===", "", "", "", ""));
        long revenue = AddGroupRows(4, period);
        _rows.Add(new FinRow("", $"Total Pendapatan: {Formatting.FormatCurrencyShort(revenue)}", "", "", "", ""));

        _rows.Add(new FinRow("", "=== BEBAN ===", "", "", "", ""));
        long expenses = AddGroupRows(5, period);
        _rows.Add(new FinRow("", $"Total Beban: {Formatting.FormatCurrencyShort(expenses)}", "", "", "", ""));

        long netIncome = revenue - expenses;
        _rows.Add(new FinRow("", "LABA / RUGI BERSIH", Formatting.FormatCurrencyShort(netIncome), "", "", ""));
        LblTotal.Text = $"Pendapatan: {Formatting.FormatCurrency(revenue)}  Beban: {Formatting.FormatCurrency(expenses)}  Laba: {Formatting.FormatCurrency(netIncome)}";
    }

    private void GenerateApAging()
    {
        SetColumns(
            new[] { "Vendor", "Nama", "Lancar", "30 Hari", "60+ Hari", "Total" },
            new[] { 1.2, 2.5, 1.2, 1.2, 1.2, 1.2 });
        var aging = _payablesService.GetAgingReport(DateTime.Now.ToString("yyyy-MM-dd"));
        long grandTotal = 0;
        foreach (var a in aging)
        {
            long over60 = a.Days60 + a.Days90;
            grandTotal += a.Total;
            _rows.Add(new FinRow(
                a.VendorCode, a.VendorName,
                Formatting.FormatCurrencyShort(a.Current),
                Formatting.FormatCurrencyShort(a.Days30),
                Formatting.FormatCurrencyShort(over60),
                Formatting.FormatCurrencyShort(a.Total)));
        }
        LblTotal.Text = $"Total Hutang: {Formatting.FormatCurrency(grandTotal)}  ({aging.Count} vendor)";
    }

    private void GenerateGlDetail()
    {
        SetColumns(
            new[] { "Tanggal", "Jurnal No", "Akun", "Keterangan", "Debit", "Kredit" },
            new[] { 1.2, 1.5, 1.2, 3.0, 1.5, 1.5 });
        string period = TxtPeriod.Text?.Trim() ?? "";
        var details = _glRepo.GetByPeriod(period);
        long totalDebit = 0;
        long totalCredit = 0;
        foreach (var d in details)
        {
            totalDebit += d.Debit;
            totalCredit += d.Credit;
            _rows.Add(new FinRow(
                Formatting.FormatDate(d.DocDate),
                d.JournalNo,
                d.AccountCode,
                d.Remark ?? "",
                Formatting.FormatCurrencyShort(d.Debit),
                Formatting.FormatCurrencyShort(d.Credit)));
        }
        LblTotal.Text = $"{details.Count} transaksi  Debit: {Formatting.FormatCurrency(totalDebit)}  Kredit: {Formatting.FormatCurrency(totalCredit)}";
    }

    private string[] GetCurrentHeaders()
    {
        switch (CboReportType.SelectedIndex)
        {
            case 0: return new[] { "Kode", "Nama", "Debit", "Kredit" };
            case 1: return new[] { "Kode", "Nama", "Jumlah" };
            case 2: return new[] { "Kode", "Nama", "Jumlah" };
            case 3: return new[] { "Vendor", "Nama", "Lancar", "30 Hari", "60+ Hari", "Total" };
            case 4: return new[] { "Tanggal", "Jurnal No", "Akun", "Keterangan", "Debit", "Kredit" };
            default: return new[] { "Kode", "Nama", "Col3", "Col4", "Col5", "Col6" };
        }
    }

    private async void ExportReport()
    {
        if (_rows.Count == 0) { await MsgBox.Show(this, "Generate report dahulu."); return; }
        var file = await this.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Excel",
            SuggestedFileName = $"Keuangan_{TxtPeriod.Text}",
            FileTypeChoices = new[] { new FilePickerFileType("Excel") { Patterns = new[] { "*.xlsx" } } }
        });
        if (file == null) return;
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Keuangan");
        var headers = GetCurrentHeaders();
        for (int c = 0; c < headers.Length; c++) ws.Cell(1, c + 1).Value = headers[c];
        var colProps = new System.Func<FinRow, string>[]
        {
            r => r.Code, r => r.Name, r => r.Col3, r => r.Col4, r => r.Col5, r => r.Col6
        };
        int row = 2;
        foreach (var r in _rows)
        {
            for (int c = 0; c < headers.Length && c < colProps.Length; c++)
                ws.Cell(row, c + 1).Value = colProps[c](r);
            row++;
        }
        wb.SaveAs(file.Path.LocalPath);
        await MsgBox.Show(this, "Export berhasil: " + file.Name);
    }
}
