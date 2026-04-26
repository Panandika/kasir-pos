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
using Kasir.Avalonia.Navigation;
using Kasir.Avalonia.Utils;
using ClosedXML.Excel;

namespace Kasir.Avalonia.Forms.Reports;

public partial class SupplierReportView : UserControl
{
    private record SupplierRow(string Code, string Name, string Address, string Phone, string Contact);

    private readonly ObservableCollection<SupplierRow> _rows = new();
    private readonly List<SupplierRow> _allRows = new();

    public SupplierReportView()
    {
        InitializeComponent();
        DgvReport.ItemsSource = _rows;
        FooterStatus.RegisterDefault(StatusLabel, "Cetak Master Supplier — F5=Refresh  F7=Export Excel  Esc=Keluar");
        TxtSearch.TextChanged += (_, _) => FilterGrid();
        GenerateReport();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (KeyboardRouter.IsF5(e)) { e.Handled = true; GenerateReport(); }
        else if (KeyboardRouter.IsF7(e)) { e.Handled = true; ExportReport(); }
        else if (KeyboardRouter.IsEscape(e)) { e.Handled = true; NavigationService.GoBack(); }
    }

    private void GenerateReport()
    {
        _rows.Clear();
        _allRows.Clear();
        var vendors = new SubsidiaryRepository(DbConnection.GetConnection()).GetAllByGroup("1", 10000, 0);
        foreach (var v in vendors)
        {
            var r = new SupplierRow(v.SubCode, v.Name, v.Address ?? "", v.Phone ?? "", v.ContactPerson ?? "");
            _allRows.Add(r);
            _rows.Add(r);
        }
        LblSummary.Text = $"{vendors.Count} suppliers";
    }

    private void FilterGrid()
    {
        _rows.Clear();
        string q = (TxtSearch.Text ?? "").Trim().ToUpperInvariant();
        foreach (var r in _allRows)
        {
            if (string.IsNullOrEmpty(q) || r.Code.ToUpperInvariant().Contains(q) || r.Name.ToUpperInvariant().Contains(q))
                _rows.Add(r);
        }
    }

    private async void ExportReport()
    {
        if (_rows.Count == 0) { await MsgBox.Show(NavigationService.Owner, "Generate report dahulu."); return; }
        var file = await NavigationService.Owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Excel",
            SuggestedFileName = "Master_Supplier",
            FileTypeChoices = new[] { new FilePickerFileType("Excel") { Patterns = new[] { "*.xlsx" } } }
        });
        if (file == null) return;
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Supplier");
        var headers = new[] { "Kode", "Nama", "Alamat", "Telepon", "Contact Person" };
        for (int c = 0; c < headers.Length; c++) ws.Cell(1, c + 1).Value = headers[c];
        int row = 2;
        foreach (var r in _rows)
        {
            ws.Cell(row, 1).Value = r.Code;
            ws.Cell(row, 2).Value = r.Name;
            ws.Cell(row, 3).Value = r.Address;
            ws.Cell(row, 4).Value = r.Phone;
            ws.Cell(row, 5).Value = r.Contact;
            row++;
        }
        wb.SaveAs(file.Path.LocalPath);
        await MsgBox.Show(NavigationService.Owner, "Export berhasil: " + file.Name);
    }
}
