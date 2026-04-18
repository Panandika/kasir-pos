using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using Kasir.Avalonia.Forms.Shared;
using Kasir.Data;
using Kasir.Services;
using Kasir.Utils;

namespace Kasir.Avalonia.Forms.Inventory;

public partial class OpnameWindow : Window
{
    private record OpnameRow(string Code, string Name, string System, string Physical, string Variance);

    private readonly ObservableCollection<OpnameRow> _rows = new();
    private readonly List<OpnameLine> _lines = new();
    private readonly StockOpnameService _service;

    public OpnameWindow()
    {
        InitializeComponent();
        var db = DbConnection.GetConnection();
        _service = new StockOpnameService(db, new ClockImpl());

        DgvOpname.ItemsSource = _rows;
        SetStatus("Stock Opname — F3: Load Sheet, Enter: Edit Fisik, F10: Save, Esc: Close");
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (KeyboardRouter.IsF3(e)) { LoadSheet(); e.Handled = true; }
        else if (KeyboardRouter.IsEnter(e)) { EditPhysical(); e.Handled = true; }
        else if (KeyboardRouter.IsF10(e)) { SaveAdjustments(); e.Handled = true; }
        else if (KeyboardRouter.IsEscape(e)) { Close(); e.Handled = true; }
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
            this,
            $"Edit Qty Fisik: {line.ProductCode}",
            new[] { "Qty Fisik" },
            new[] { line.PhysicalQty.ToString() });

        if (!ok) return;

        if (!int.TryParse(vals[0], out int physQty) || physQty < 0)
        {
            await MsgBox.Show(this, "Qty tidak valid.");
            return;
        }

        line.PhysicalQty = physQty;
        RefreshGrid();
        DgvOpname.SelectedIndex = idx;
    }

    private void RefreshGrid()
    {
        _rows.Clear();
        foreach (var line in _lines)
        {
            int variance = line.PhysicalQty - line.SystemQty;
            _rows.Add(new OpnameRow(
                line.ProductCode ?? "",
                line.ProductName ?? "",
                line.SystemQty.ToString(),
                line.PhysicalQty.ToString(),
                variance.ToString()));
        }
    }

    private async void SaveAdjustments()
    {
        if (_lines.Count == 0)
        {
            await MsgBox.Show(this, "Sheet belum dimuat. Tekan F3 untuk load.");
            return;
        }

        int varCount = 0;
        foreach (var line in _lines)
        {
            if (line.PhysicalQty != line.SystemQty) varCount++;
        }

        bool confirmed = await MsgBox.Confirm(this, $"Simpan opname dengan {varCount} selisih?");
        if (!confirmed) return;

        string journalNo = _service.CreateOpnameAdjustment(_lines, 1);
        await MsgBox.Show(this, $"Penyesuaian tersimpan: {journalNo}");

        _lines.Clear();
        _rows.Clear();
        SetStatus("Stock Opname — F3: Load Sheet, Enter: Edit Fisik, F10: Save, Esc: Close");
    }

    private void SetStatus(string text)
    {
        StatusLabel.Text = text;
    }
}
