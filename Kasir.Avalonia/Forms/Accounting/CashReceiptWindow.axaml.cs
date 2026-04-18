using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Models;
using Kasir.Utils;
using Kasir.Avalonia.Forms.Shared;

namespace Kasir.Avalonia.Forms.Accounting;

public partial class CashReceiptWindow : Window
{
    private record LineRow(string AccCode, string AccName, string Amount, string Remark);

    private readonly ObservableCollection<LineRow> _rows = new();
    private readonly List<CashTransactionLine> _lines = new();
    private readonly CashTransactionRepository _cashTxnRepo;
    private readonly CounterRepository _counterRepo;
    private readonly AccountRepository _accountRepo;
    private readonly bool _isBankMode;
    private readonly int _currentUserId;

    public CashReceiptWindow(bool bankMode = false, int userId = 1)
    {
        InitializeComponent();
        _isBankMode   = bankMode;
        _currentUserId = userId;

        var db = DbConnection.GetConnection();
        _cashTxnRepo = new CashTransactionRepository(db);
        _counterRepo = new CounterRepository(db);
        _accountRepo = new AccountRepository(db);

        DgvLines.ItemsSource = _rows;
        TxtDate.Text = DateTime.Now.ToString("yyyy-MM-dd");

        string title = _isBankMode ? "Penerimaan Bank" : "Penerimaan Kas";
        Title = title;
        SetStatus(title + " — F5: Simpan, Ins: Tambah Baris, Esc: Keluar");
        UpdateTotal();
    }

    private async void AddLine()
    {
        var (ok, vals) = await InputDialogWindow.Show(
            this,
            "Tambah Baris",
            new[] { "Kode Akun", "Jumlah", "Keterangan" },
            new[] { "", "", "" });

        if (!ok) return;

        string accCode = vals[0].Trim();
        string amountStr = vals[1].Trim();
        string remark = vals[2].Trim();

        if (string.IsNullOrEmpty(accCode) || string.IsNullOrEmpty(amountStr)) return;

        if (!long.TryParse(amountStr, out long amount) || amount <= 0)
        {
            await MsgBox.Show(this, "Jumlah tidak valid.");
            return;
        }

        var account = _accountRepo.GetByCode(accCode);
        string accName = account != null ? account.AccountName : accCode;

        var line = new CashTransactionLine
        {
            AccountCode = accCode,
            Direction   = "K",
            Value       = amount * 100,
            Remark      = remark
        };

        _lines.Add(line);
        _rows.Add(new LineRow(accCode, accName, Formatting.FormatMoney(line.Value), remark));
        UpdateTotal();
    }

    private void UpdateTotal()
    {
        long total = 0;
        foreach (var l in _lines) total += l.Value;
        LblTotal.Text = "Total: " + Formatting.FormatMoney(total);
    }

    private async void SaveTransaction()
    {
        if (_lines.Count == 0)
        {
            await MsgBox.Show(this, "Belum ada baris transaksi.");
            return;
        }

        string docDate = TxtDate.Text?.Trim() ?? DateTime.Now.ToString("yyyy-MM-dd");
        string remark  = TxtRemark.Text?.Trim() ?? "";
        string prefix  = _isBankMode ? "BMS" : "KMS";
        string docType = _isBankMode ? "BANK_IN" : "CASH_IN";

        string periodCode;
        if (docDate.Length >= 7)
            periodCode = docDate.Substring(0, 4) + docDate.Substring(5, 2);
        else
            periodCode = Formatting.CurrentPeriod();

        string journalNo = _counterRepo.GetNext(prefix, "01");

        long total = 0;
        foreach (var l in _lines) total += l.Value;

        var txn = new CashTransaction
        {
            DocType    = docType,
            JournalNo  = journalNo,
            DocDate    = docDate,
            Remark     = remark,
            TotalValue = total,
            PeriodCode = periodCode,
            RegisterId = "01",
            ChangedBy  = _currentUserId
        };

        foreach (var l in _lines)
        {
            l.JournalNo = journalNo;
        }

        try
        {
            _cashTxnRepo.Insert(txn, _lines);
            await MsgBox.Show(this, $"Transaksi {journalNo} berhasil disimpan.");
            ClearForm();
        }
        catch (Exception ex)
        {
            await MsgBox.Show(this, "Gagal menyimpan: " + ex.Message);
        }
    }

    private void ClearForm()
    {
        _lines.Clear();
        _rows.Clear();
        TxtDate.Text   = DateTime.Now.ToString("yyyy-MM-dd");
        TxtRemark.Text = "";
        UpdateTotal();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (KeyboardRouter.IsInsert(e))
        {
            e.Handled = true;
            AddLine();
            return;
        }

        if (KeyboardRouter.IsF5(e))
        {
            e.Handled = true;
            SaveTransaction();
            return;
        }

        if (KeyboardRouter.IsEscape(e))
        {
            e.Handled = true;
            Close();
        }
    }

    private void SetStatus(string text)
    {
        StatusLabel.Text = text;
    }
}
