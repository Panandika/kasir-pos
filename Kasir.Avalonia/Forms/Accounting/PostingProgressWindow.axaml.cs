using System;
using Avalonia.Controls;
using Avalonia.Input;
using Kasir.Data;
using Kasir.Services;
using Kasir.Utils;
using Kasir.Avalonia.Forms.Shared;

namespace Kasir.Avalonia.Forms.Accounting;

public partial class PostingProgressWindow : Window
{
    private readonly PostingService _postingService;
    private readonly string _periodCode;

    public PostingProgressWindow()
    {
        _periodCode = DateTime.Now.ToString("yyyyMM");
        InitializeComponent();
        _postingService = new PostingService(DbConnection.GetConnection());
        SetStatus("F1=Post POS  F2=Post Pembelian  F3=Post Kas  F5=Tutup Periode  F10=Cek Saldo  Esc=Keluar");
    }

    private void Log(string msg)
    {
        TxtLog.Text += $"{DateTime.Now:HH:mm:ss} {msg}\n";
        TxtLog.CaretIndex = TxtLog.Text.Length;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (KeyboardRouter.IsF1(e))
        {
            e.Handled = true;
            RunPostSales();
        }
        else if (KeyboardRouter.IsF2(e))
        {
            e.Handled = true;
            RunPostPurchases();
        }
        else if (KeyboardRouter.IsF3(e))
        {
            e.Handled = true;
            RunPostCash();
        }
        else if (KeyboardRouter.IsF5(e))
        {
            e.Handled = true;
            RunClosePeriod();
        }
        else if (KeyboardRouter.IsF10(e))
        {
            e.Handled = true;
            RunBalanceCheck();
        }
        else if (KeyboardRouter.IsEscape(e))
        {
            e.Handled = true;
            Close();
        }
    }

    private async void RunPostSales()
    {
        try
        {
            Log($"Posting penjualan {_periodCode}...");
            var r = _postingService.PostSales(_periodCode);
            Log($"Selesai: {r.PostedCount} diposting, {r.ErrorCount} error");
            foreach (var err in r.Errors)
                Log("  ERROR: " + err);
        }
        catch (Exception ex)
        {
            Log("GAGAL: " + ex.Message);
        }
        await System.Threading.Tasks.Task.CompletedTask;
    }

    private async void RunPostPurchases()
    {
        try
        {
            Log($"Posting pembelian {_periodCode}...");
            var r = _postingService.PostPurchases(_periodCode);
            Log($"Selesai posting pembelian: {r.PostedCount} diposting, {r.ErrorCount} error");
            foreach (var err in r.Errors)
                Log("  ERROR: " + err);

            Log($"Posting retur {_periodCode}...");
            var r2 = _postingService.PostReturns(_periodCode);
            Log($"Selesai posting retur: {r2.PostedCount} diposting, {r2.ErrorCount} error");
            foreach (var err in r2.Errors)
                Log("  ERROR: " + err);
        }
        catch (Exception ex)
        {
            Log("GAGAL: " + ex.Message);
        }
        await System.Threading.Tasks.Task.CompletedTask;
    }

    private async void RunPostCash()
    {
        try
        {
            Log($"Posting transaksi kas {_periodCode}...");
            var r = _postingService.PostCashTransactions(_periodCode);
            Log($"Selesai: {r.PostedCount} diposting, {r.ErrorCount} error");
            foreach (var err in r.Errors)
                Log("  ERROR: " + err);
        }
        catch (Exception ex)
        {
            Log("GAGAL: " + ex.Message);
        }
        await System.Threading.Tasks.Task.CompletedTask;
    }

    private async void RunClosePeriod()
    {
        try
        {
            bool c = await MsgBox.Confirm(this, $"Tutup periode {_periodCode}? Tidak bisa dibatalkan.");
            if (!c) return;
            _postingService.ClosePeriod(_periodCode);
            Log($"Periode {_periodCode} ditutup.");
        }
        catch (Exception ex)
        {
            Log("GAGAL: " + ex.Message);
        }
    }

    private async void RunBalanceCheck()
    {
        try
        {
            var result = _postingService.CheckBalance(_periodCode);
            if (result.IsBalanced)
            {
                Log($"Saldo seimbang — Debit: {result.TotalDebits}  Kredit: {result.TotalCredits}");
            }
            else
            {
                Log($"TIDAK SEIMBANG — Debit: {result.TotalDebits}  Kredit: {result.TotalCredits}  Selisih: {result.Difference}");
                foreach (var acc in result.DiscrepancyAccounts)
                    Log("  Akun: " + acc);
            }
        }
        catch (Exception ex)
        {
            Log("GAGAL: " + ex.Message);
        }
        await System.Threading.Tasks.Task.CompletedTask;
    }

    private void SetStatus(string text)
    {
        StatusLabel.Text = text;
    }
}
