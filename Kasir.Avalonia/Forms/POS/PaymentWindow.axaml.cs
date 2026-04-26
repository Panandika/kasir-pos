using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Models;
using Kasir.Avalonia.Behaviors;
using Kasir.Services;
using Kasir.Utils;

namespace Kasir.Avalonia.Forms.POS;

public partial class PaymentWindow : Window
{
    private readonly long _totalDue;
    private readonly PaymentCalculator _paymentCalc;
    private readonly List<CreditCard> _cards;

    public long CashAmount { get; private set; }
    public long CardAmount { get; private set; }
    public long VoucherAmount { get; private set; }
    public string CardCode { get; private set; } = "";
    public string CardType { get; private set; } = "";
    public long Change { get; private set; }
    public bool Accepted { get; private set; }

    public PaymentWindow(long totalDue)
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
        BtnCancel.Click += (_, _) => Close();

        Recalculate();
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
            CardType = "C";
        }
        Accepted = true;
        Close();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (KeyboardRouter.IsEnter(e)) { e.Handled = true; Accept(); }
        else if (KeyboardRouter.IsEscape(e)) { e.Handled = true; Close(); }
    }

    private static long ParseAmount(string? text)
    {
        if (long.TryParse((text ?? "").Replace(".", "").Replace(",", ""), out long v))
            return v * 100;
        return 0;
    }

    public static async Task<PaymentWindow?> Show(Window owner, long totalDue)
    {
        var dlg = new PaymentWindow(totalDue);
        await dlg.ShowDialog(owner);
        return dlg.Accepted ? dlg : null;
    }
}
