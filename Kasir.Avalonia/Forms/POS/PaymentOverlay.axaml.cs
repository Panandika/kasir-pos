using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Models;
using Kasir.Avalonia.Behaviors;
using Kasir.Services;
using Kasir.Utils;

namespace Kasir.Avalonia.Forms.POS;

public partial class PaymentOverlay : UserControl
{
    private readonly long _totalDue;
    private readonly PaymentCalculator _paymentCalc;
    private readonly List<CreditCard> _cards;
    private readonly TaskCompletionSource<bool> _tcs = new();

    public long CashAmount { get; private set; }
    public long CardAmount { get; private set; }
    public long VoucherAmount { get; private set; }
    public string CardCode { get; private set; } = "";
    public string CardType { get; private set; } = "";
    public long Change { get; private set; }
    public bool Accepted { get; private set; }

    public PaymentOverlay(long totalDue)
    {
        InitializeComponent();
        _totalDue = totalDue;
        _paymentCalc = new PaymentCalculator();
        _cards = new CreditCardRepository(DbConnection.GetConnection()).GetAll();

        LblTotal.Text = $"TOTAL: {Formatting.FormatCurrency(_totalDue)}";
        TxtCash.Text = IndonesianMoneyFormatter.Format(_totalDue / 100);
        TxtCard.Text = "0";
        TxtVoucher.Text = "0";

        NumericInputBehavior.AttachLiveFormatting(TxtCash);
        NumericInputBehavior.AttachLiveFormatting(TxtCard);
        NumericInputBehavior.AttachLiveFormatting(TxtVoucher);

        var cardItems = new List<string> { "(none)" };
        foreach (var c in _cards)
            cardItems.Add($"{c.Name} ({c.FeePct / 100.0:F1}%)");
        CboCardType.ItemsSource = cardItems;
        CboCardType.SelectedIndex = 0;

        TxtCash.TextChanged += (_, _) => Recalculate();
        TxtCard.TextChanged += (_, _) => Recalculate();
        TxtVoucher.TextChanged += (_, _) => Recalculate();

        BtnOk.Click += (_, _) => Accept();
        BtnCancel.Click += (_, _) => _tcs.TrySetResult(false);

        AttachedToVisualTree += (_, _) => { TxtCash.Focus(); TxtCash.SelectAll(); };
        KeyDown += OnKey;

        Recalculate();
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (KeyboardRouter.IsEnter(e)) { e.Handled = true; Accept(); }
        else if (KeyboardRouter.IsEscape(e)) { e.Handled = true; _tcs.TrySetResult(false); }
    }

    private void Recalculate()
    {
        long cash = ParseAmount(TxtCash.Text);
        long card = ParseAmount(TxtCard.Text);
        long voucher = ParseAmount(TxtVoucher.Text);
        var result = _paymentCalc.ValidatePayment(_totalDue, cash, card, voucher);
        if (result.IsValid)
        {
            LblChange.Text = $"KEMBALI: {Formatting.FormatCurrency(result.Change)}";
            BtnOk.IsEnabled = true;
        }
        else
        {
            LblChange.Text = $"KURANG: {Formatting.FormatCurrency(result.Shortfall)}";
            BtnOk.IsEnabled = false;
        }
    }

    private void Accept()
    {
        CashAmount = ParseAmount(TxtCash.Text);
        CardAmount = ParseAmount(TxtCard.Text);
        VoucherAmount = ParseAmount(TxtVoucher.Text);
        var result = _paymentCalc.ValidatePayment(_totalDue, CashAmount, CardAmount, VoucherAmount);
        if (!result.IsValid) return;
        Change = result.Change;
        if (CboCardType.SelectedIndex > 0)
        {
            var card = _cards[CboCardType.SelectedIndex - 1];
            CardCode = card.CardCode;
            CardType = string.IsNullOrEmpty(card.CardType) ? "C" : card.CardType;
        }
        Accepted = true;
        _tcs.TrySetResult(true);
    }

    public Task<bool> Result => _tcs.Task;

    private static long ParseAmount(string? text)
    {
        if (long.TryParse((text ?? "").Replace(".", "").Replace(",", ""), out long v))
            return v * 100;
        return 0;
    }
}

// Compatibility shim preserving the PaymentWindow.Show() surface used by SaleView.
public static class PaymentWindow
{
    public static async Task<PaymentOverlay?> Show(Visual? owner, long totalDue)
    {
        ShellWindow? shell = TopLevel.GetTopLevel(owner) as ShellWindow;
        if (shell is null
            && Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            shell = desktop.MainWindow as ShellWindow;
        }
        if (shell is null) return null;

        var overlay = new PaymentOverlay(totalDue);
        shell.ShowOverlay(overlay);
        try { await overlay.Result; }
        finally { shell.HideOverlay(); }
        return overlay.Accepted ? overlay : null;
    }
}
