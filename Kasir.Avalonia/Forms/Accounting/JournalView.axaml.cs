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

namespace Kasir.Avalonia.Forms.Accounting;

public partial class JournalView : UserControl
{
    private class JournalLineRow
    {
        public string AccCode { get; set; } = "";
        public string AccName { get; set; } = "";
        public string Remark  { get; set; } = "";
        public string Debit   { get; set; } = "";
        public string Credit  { get; set; } = "";
    }

    private readonly ObservableCollection<JournalLineRow> _rows = new();
    private readonly AccountingService _accountingService;
    private readonly AccountRepository _accountRepo;
    private readonly bool _readOnly;
    private readonly int _userId;

    public JournalView(bool readOnly = false, int userId = 1)
    {
        _readOnly = readOnly;
        _userId   = userId;
        InitializeComponent();

        var db = DbConnection.GetConnection();
        _accountingService = new AccountingService(db);
        _accountRepo       = new AccountRepository(db);

        TxtDate.Text       = DateTime.Now.ToString("yyyy-MM-dd");
        TxtDate.IsReadOnly = _readOnly;
        TxtRemark.IsReadOnly = _readOnly;
        DgvLines.ItemsSource = _rows;
        DgvLines.IsReadOnly  = _readOnly;

        if (!_readOnly)
            DgvLines.CellEditEnded += OnCellEditEnded;

        string action = _readOnly
            ? "Info Jurnal — Esc=Keluar"
            : "Jurnal Memorial — F5=Simpan  Ins=Tambah Baris  Del=Hapus Baris  Esc=Keluar";
        SetStatus(action);
        UpdateTotals();
    }

    private void OnCellEditEnded(object? sender, DataGridCellEditEndedEventArgs e)
    {
        if (e.Column.DisplayIndex == 0 && e.EditAction == DataGridEditAction.Commit)
        {
            var row = e.Row.DataContext as JournalLineRow;
            if (row != null && !string.IsNullOrEmpty(row.AccCode))
            {
                var acc = _accountRepo.GetByCode(row.AccCode);
                if (acc != null)
                {
                    row.AccName = acc.AccountName;
                    DgvLines.ItemsSource = null;
                    DgvLines.ItemsSource = _rows;
                }
            }
            UpdateTotals();
        }
    }

    private void UpdateTotals()
    {
        long td = 0, tc = 0;
        foreach (var r in _rows)
        {
            long.TryParse(r.Debit,  out long d);
            long.TryParse(r.Credit, out long c);
            td += d;
            tc += c;
        }
        LblDebit.Text  = $"Debit: {Formatting.FormatMoney(td)}";
        LblCredit.Text = $"Kredit: {Formatting.FormatMoney(tc)}";
        long diff = td - tc;
        LblDiff.Text = $"Selisih: {Formatting.FormatMoney(Math.Abs(diff))}";
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (KeyboardRouter.IsF5(e) && !_readOnly)
        {
            e.Handled = true;
            SaveJournal();
        }
        else if (KeyboardRouter.IsInsert(e) && !_readOnly)
        {
            e.Handled = true;
            _rows.Add(new JournalLineRow());
        }
        else if (KeyboardRouter.IsDelete(e) && !_readOnly)
        {
            e.Handled = true;
            DeleteLine();
        }
        else if (KeyboardRouter.IsEscape(e))
        {
            e.Handled = true;
            NavigationService.GoBack();
        }
    }

    private void DeleteLine()
    {
        var row = DgvLines.SelectedItem as JournalLineRow;
        if (row != null)
        {
            _rows.Remove(row);
            UpdateTotals();
        }
    }

    private async void SaveJournal()
    {
        if (_readOnly) return;

        string dateText = TxtDate.Text?.Trim() ?? "";
        string periodCode = dateText.Length >= 7
            ? dateText.Substring(0, 4) + dateText.Substring(5, 2)
            : "";

        var entry = new JournalEntry
        {
            DocDate    = dateText,
            Remark     = TxtRemark.Text?.Trim() ?? "",
            PeriodCode = periodCode,
            ChangedBy  = _userId,
            Lines      = new List<JournalLine>()
        };

        foreach (var r in _rows)
        {
            if (string.IsNullOrEmpty(r.AccCode)) continue;
            long.TryParse(r.Debit,  out long d);
            long.TryParse(r.Credit, out long c);
            entry.Lines.Add(new JournalLine
            {
                AccountCode = r.AccCode,
                Remark      = r.Remark,
                Debit       = d,
                Credit      = c
            });
        }

        try
        {
            string jnl = _accountingService.CreateJournalEntry(entry);
            await MsgBox.Show(NavigationService.Owner, $"Jurnal {jnl} tersimpan.");
            _rows.Clear();
            TxtRemark.Text = "";
            UpdateTotals();
        }
        catch (Exception ex)
        {
            await MsgBox.Show(NavigationService.Owner, "Gagal: " + ex.Message);
        }
    }

    private void SetStatus(string text)
    {
        StatusLabel.Text = text;
    }
}
