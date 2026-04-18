using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using Kasir.Avalonia.Forms.Shared;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Models;
using Kasir.Utils;

namespace Kasir.Avalonia.Forms.Bank;

public partial class BankGiroWindow : Window
{
    private record GiroRow(string GiroNo, string GiroDate, string DocDate, string Value, string Status, string Remark, GiroEntry Tag);

    private readonly ObservableCollection<GiroRow> _rows = new();
    private readonly GiroRepository _giroRepo;
    private readonly bool _readOnly;

    public BankGiroWindow(bool readOnly = false)
    {
        InitializeComponent();
        _readOnly = readOnly;
        _giroRepo = new GiroRepository(DbConnection.GetConnection());
        DgvGiros.ItemsSource = _rows;

        string actions = readOnly
            ? "Giro Bank — F2: Cari Supplier, Esc: Keluar"
            : "Giro Bank — F2: Cari Supplier, F5: Clear Giro, F8: Tolak Giro, Esc: Keluar";
        SetStatus(actions);

        TxtVendor.KeyDown += (_, e) =>
        {
            if (KeyboardRouter.IsEnter(e))
            {
                LoadGiros();
                e.Handled = true;
            }
        };
    }

    private void LoadGiros()
    {
        _rows.Clear();
        string vendor = TxtVendor.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(vendor)) return;

        var giros = _giroRepo.GetOpenByVendor(vendor);
        foreach (var g in giros)
        {
            _rows.Add(new GiroRow(
                g.GiroNo ?? "",
                Formatting.FormatDate(g.GiroDate),
                Formatting.FormatDate(g.DocDate),
                Formatting.FormatMoney(g.Value),
                g.Status == "O" ? "Open" : "Cair",
                g.Remark ?? "",
                g));
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (KeyboardRouter.IsF2(e)) { TxtVendor.Focus(); e.Handled = true; }
        else if (KeyboardRouter.IsF5(e)) { if (!_readOnly) ClearSelectedGiro(); e.Handled = true; }
        else if (KeyboardRouter.IsF8(e)) { if (!_readOnly) RejectSelectedGiro(); e.Handled = true; }
        else if (KeyboardRouter.IsEscape(e)) { Close(); e.Handled = true; }
    }

    private async void ClearSelectedGiro()
    {
        var sel = DgvGiros.SelectedItem as GiroRow;
        if (sel == null) return;

        bool confirmed = await MsgBox.Confirm(this, $"Clear giro {sel.GiroNo}?");
        if (!confirmed) return;

        _giroRepo.ClearGiro(sel.Tag.Id, 1);
        LoadGiros();
    }

    private async void RejectSelectedGiro()
    {
        var sel = DgvGiros.SelectedItem as GiroRow;
        if (sel == null) return;

        bool confirmed = await MsgBox.Confirm(this, $"Tolak giro {sel.GiroNo}?");
        if (!confirmed) return;

        _giroRepo.RejectGiro(sel.Tag.Id);
        LoadGiros();
    }

    private void SetStatus(string text)
    {
        StatusLabel.Text = text;
    }
}
