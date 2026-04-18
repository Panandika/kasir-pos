using System;
using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using Kasir.Data;
using Kasir.Services;
using Kasir.Utils;
using Kasir.Avalonia.Forms.Shared;

namespace Kasir.Avalonia.Forms.Accounting;

public partial class PayablesWindow : Window
{
    private record PayableRow(string JournalNo, string DocDate, string DueDate, string Amount, string Paid, string Remaining);

    private readonly ObservableCollection<PayableRow> _rows = new();
    private readonly PayablesService _payablesService;
    private string? _selectedVendor;

    public PayablesWindow()
    {
        InitializeComponent();

        var db = DbConnection.GetConnection();
        _payablesService = new PayablesService(db);

        DgvPayables.ItemsSource = _rows;

        TxtVendor.KeyDown += (_, e) =>
        {
            if (KeyboardRouter.IsEnter(e)) LoadVendorPayables();
        };

        SetStatus("Pembayaran Hutang — F2: Cari Supplier, F5: Bayar, Esc: Keluar");
    }

    private void LoadVendorPayables()
    {
        _selectedVendor = TxtVendor.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(_selectedVendor)) return;

        _rows.Clear();
        long grandTotal = 0;

        var entries = _payablesService.GetOutstanding(_selectedVendor);
        foreach (var p in entries)
        {
            long remaining = p.Amount - p.PaymentAmount;
            grandTotal += remaining;
            _rows.Add(new PayableRow(
                p.JournalNo,
                Formatting.FormatDate(p.DocDate),
                Formatting.FormatDate(p.DueDate ?? p.DocDate),
                Formatting.FormatMoney(p.Amount),
                Formatting.FormatMoney(p.PaymentAmount),
                Formatting.FormatMoney(remaining)));
        }

        LblTotal.Text = "Total Hutang: " + Formatting.FormatMoney(grandTotal);
    }

    private async void ProcessPayment()
    {
        if (string.IsNullOrEmpty(_selectedVendor))
        {
            await MsgBox.Show(this, "Pilih supplier terlebih dahulu.");
            return;
        }

        string paymentStr = TxtPayment.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(paymentStr))
        {
            await MsgBox.Show(this, "Masukkan jumlah pembayaran.");
            return;
        }

        if (!long.TryParse(paymentStr, out long paymentAmount) || paymentAmount <= 0)
        {
            await MsgBox.Show(this, "Jumlah pembayaran tidak valid.");
            return;
        }

        bool confirmed = await MsgBox.Confirm(this,
            $"Bayar hutang {_selectedVendor} sebesar {Formatting.FormatMoney(paymentAmount * 100)}?");
        if (!confirmed) return;

        string docDate    = Formatting.TodayIso();
        string periodCode = Formatting.CurrentPeriod();

        try
        {
            var result = _payablesService.AllocatePayment(
                _selectedVendor,
                paymentAmount * 100,
                "1-1101",
                docDate,
                periodCode,
                1,
                null);

            await MsgBox.Show(this,
                $"Pembayaran berhasil.\n" +
                $"Dialokasikan: {Formatting.FormatMoney(result.AmountAllocated)}\n" +
                $"Faktur lunas: {result.InvoicesPaid}\n" +
                $"Sisa: {Formatting.FormatMoney(result.AmountRemaining)}");

            TxtPayment.Text = "";
            LoadVendorPayables();
        }
        catch (Exception ex)
        {
            await MsgBox.Show(this, "Gagal memproses pembayaran: " + ex.Message);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (KeyboardRouter.IsF2(e))
        {
            e.Handled = true;
            TxtVendor.Focus();
            return;
        }

        if (KeyboardRouter.IsF5(e))
        {
            e.Handled = true;
            ProcessPayment();
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
