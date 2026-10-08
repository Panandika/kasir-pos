using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia;
using Kasir.Avalonia.Forms.Shared;
using Kasir.Avalonia.Navigation;
using Kasir.Avalonia.Infrastructure;
using Kasir.Avalonia.Utils;
using Kasir.Data;
using Kasir.Services;
using Kasir.Utils;

namespace Kasir.Avalonia.Forms.Inventory;

public partial class OpnameView : UserControl
{
    private record OpnameRow(string Code, string Name, string System, string Physical, string Variance, string CountedAt);

    private readonly ObservableCollection<OpnameRow> _rows = new();
    private readonly List<OpnameLine> _lines = new();
    private readonly StockOpnameService _service;

    private readonly int _userId;

    public OpnameView(int userId)
    {
        _userId = userId;
        InitializeComponent();
        var db = DbConnection.GetConnection();
        _service = new StockOpnameService(db, new ClockImpl());

        DgvOpname.ItemsSource = _rows;
        ViewShortcuts.WireGridEnter(DgvOpname, EditPhysical);
        ViewShortcuts.AutoFocusOnAttach(this, DgvOpname);
        FooterStatus.RegisterDefault(StatusLabel, "Stock Opname — F3: Load Sheet, Enter: Edit Fisik, F10: Save, Esc: Close");
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (KeyboardRouter.IsF3(e)) { LoadSheet(); e.Handled = true; }
        else if (KeyboardRouter.IsEnter(e)) { EditPhysical(); e.Handled = true; }
        else if (KeyboardRouter.IsF10(e)) { SaveAdjustments(); e.Handled = true; }
        else if (KeyboardRouter.IsEscape(e)) { NavigationService.GoBack(); e.Handled = true; }
    }

    private void LoadSheet()
    {
        _lines.Clear();
        _rows.Clear();

        var sheet = _service.GetOpnameSheet(500);
        _lines.AddRange(sheet);
        RefreshGrid();
        SetStatus($"Stock Opname — {_lines.Count} barang dimuat. Enter: Edit Fisik, F10: Save, Esc: Close");
    }

    private async void EditPhysical()
    {
        int idx = DgvOpname.SelectedIndex;
        if (idx < 0 || idx >= _lines.Count) return;

        var line = _lines[idx];

        var (ok, vals) = await InputDialogWindow.Show(
            NavigationService.Owner,
            $"Edit Qty Fisik: {line.ProductCode}",
            new[] { "Qty Fisik" },
            new[] { line.PhysicalQty.ToString() });

        if (!ok) return;

        if (!int.TryParse(vals[0], out int physQty) || physQty < 0)
        {
            await MsgBox.Show(NavigationService.Owner, "Qty tidak valid.");
            return;
        }

        // Stamps the count time and takes the system qty now (PR-K6); later sales and
        // receipts are allowed for when the opname is saved.
        _service.RecordCount(line, physQty);
        RefreshGrid();
        DgvOpname.SelectedIndex = idx;
    }

    private void RefreshGrid()
    {
        _rows.Clear();
        foreach (var line in _lines)
        {
            bool counted = line.IsCounted;
            _rows.Add(new OpnameRow(
                line.ProductCode ?? "",
                line.ProductName ?? "",
                counted ? line.SystemQty.ToString() : "-",
                counted ? line.PhysicalQty.ToString() : "belum dihitung",
                counted ? line.Variance.ToString() : "-",
                counted ? line.CountTime!.Value.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) : ""));
        }
    }

    private async void SaveAdjustments()
    {
        if (_lines.Count == 0)
        {
            await MsgBox.Show(NavigationService.Owner, "Sheet belum dimuat. Tekan F3 untuk load.");
            return;
        }

        int counted = 0;
        foreach (var line in _lines)
        {
            if (line.IsCounted) counted++;
        }
        if (counted == 0)
        {
            await MsgBox.Show(NavigationService.Owner, "Belum ada barang yang dihitung. Pilih barang lalu Enter untuk isi qty fisik.");
            return;
        }

        int uncounted = _lines.Count - counted;
        bool confirmed = await MsgBox.Confirm(NavigationService.Owner,
            $"Simpan opname: {counted} barang dihitung" +
            (uncounted > 0 ? $", {uncounted} belum dihitung (stok tidak diubah)" : "") +
            ".\nPenjualan/penerimaan setelah jam hitung ikut diperhitungkan.");
        if (!confirmed) return;

        string journalNo = _service.CreateOpnameAdjustment(_lines, _userId);
        await MsgBox.Show(NavigationService.Owner, $"Penyesuaian tersimpan: {journalNo}");

        _lines.Clear();
        _rows.Clear();
        FooterStatus.Reset(StatusLabel);
    }

    private void SetStatus(string text) => FooterStatus.Show(StatusLabel, text);
}
