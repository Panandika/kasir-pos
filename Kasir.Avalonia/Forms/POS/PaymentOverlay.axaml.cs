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
using Kasir.Avalonia.Infrastructure;

namespace Kasir.Avalonia.Forms.POS;

public partial class PaymentOverlay : UserControl
{
    private readonly long _totalDue;
    private readonly PaymentCalculator _paymentCalc;
    private readonly List<CreditCard> _cards;
    private readonly TaskCompletionSource<bool> _tcs = new();
    // Last cash value the screen filled in itself. While the cash box still shows it, the
    // cashier has not typed cash, so it follows card/voucher entry instead of being read
    // as extra cash tendered (which showed the whole card amount as KEMBALI) (#19).
    private string _autoCashText = "";
    private bool _settingCash;

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
        _autoCashText = IndonesianMoneyFormatter.Format(_paymentCalc.SuggestedCash(_totalDue, 0, 0) / 100);
        TxtCash.Text = _autoCashText;
        TxtCard.Text = "0";
        TxtVoucher.Text = "0";

        NumericInputBehavior.AttachLiveFormatting(TxtCash);
        NumericInputBehavior.AttachLiveFormatting(TxtCard);
        NumericInputBehavior.AttachLiveFormatting(TxtVoucher);

        var cardItems = new List<string> { "(none)" };
        foreach (var c in _cards)
            // Name only: the card fee (MDR) is borne by the store and never charged to the
            // customer (PBI 23/6/PBI/2021 bans surcharging), so showing "%" here misled (#19).
            cardItems.Add(c.Name);
        CboCardType.ItemsSource = cardItems;
        CboCardType.SelectedIndex = 0;

        TxtCash.TextChanged += (_, _) => Recalculate();
        TxtCard.TextChanged += (_, _) => { SyncAutoCash(); Recalculate(); };
        TxtVoucher.TextChanged += (_, _) => { SyncAutoCash(); Recalculate(); };
        CboCardType.SelectionChanged += (_, _) => Recalculate();

        BtnOk.Click += (_, _) => Accept();
        BtnCancel.Click += (_, _) => _tcs.TrySetResult(false);

        ViewShortcuts.FocusInputOnShow(this, TxtCash);
        KeyDown += OnKey;

        Recalculate();
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (KeyboardRouter.IsEnter(e)) { e.Handled = true; Accept(); }
        else if (KeyboardRouter.IsEscape(e)) { e.Handled = true; _tcs.TrySetResult(false); }
    }

    private void SyncAutoCash()
    {
        if (_settingCash || (TxtCash.Text ?? "") != _autoCashText) return;
        if (!TryParseAmount(TxtCard.Text, out long card) || !TryParseAmount(TxtVoucher.Text, out long voucher)) return;
        string suggested = IndonesianMoneyFormatter.Format(_paymentCalc.SuggestedCash(_totalDue, card, voucher) / 100);
        if (suggested == _autoCashText) return;
        _settingCash = true;
        try
        {
            _autoCashText = suggested;
            TxtCash.Text = suggested;
        }
        finally { _settingCash = false; }
    }

    // Null when the tender can be accepted; otherwise the reason shown in place of KEMBALI.
    private string? Validate(out PaymentValidation result)
    {
        result = new PaymentValidation();
        if (!TryParseAmount(TxtCash.Text, out long cash)
            || !TryParseAmount(TxtCard.Text, out long card)
            || !TryParseAmount(TxtVoucher.Text, out long voucher))
            return "JUMLAH TIDAK VALID";
        result = _paymentCalc.ValidatePayment(_totalDue, cash, card, voucher);
        if (result.NonCashOverpayment > 0)
            return $"KARTU+VOUCHER LEBIH: {Formatting.FormatCurrency(result.NonCashOverpayment)}";
        if (!result.IsValid)
            return $"KURANG: {Formatting.FormatCurrency(result.Shortfall)}";
        // Legacy JUAL1: "Jenis card harus di-isi" — the card decides the GL account.
        if (card > 0 && CboCardType.SelectedIndex <= 0)
            return "PILIH JENIS KARTU";
        return null;
    }

    private void Recalculate()
    {
        string? error = Validate(out var result);
        LblChange.Text = error ?? $"KEMBALI: {Formatting.FormatCurrency(result.Change)}";
        BtnOk.IsEnabled = error == null;
    }

    private void Accept()
    {
        if (Validate(out var result) != null) return;
        CashAmount = result.CashAmount;
        CardAmount = result.CardAmount;
        VoucherAmount = result.VoucherAmount;
        Change = result.Change;
        CardCode = "";
        CardType = "";
        if (CardAmount > 0 && CboCardType.SelectedIndex > 0)
        {
            var card = _cards[CboCardType.SelectedIndex - 1];
            CardCode = card.CardCode;
            CardType = string.IsNullOrEmpty(card.CardType) ? "C" : card.CardType;
        }
        Accepted = true;
        _tcs.TrySetResult(true);
    }

    public Task<bool> Result => _tcs.Task;

    // Whole-Rupiah tender ("1.250.000") to cents. Blank = 0. Rejects input too large for
    // long cents instead of letting "v * 100" wrap negative (#19).
    private static bool TryParseAmount(string? text, out long cents)
    {
        cents = 0;
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (!Formatting.TryParseRupiah(text, out long rupiah) || rupiah < 0) return false;
        cents = rupiah * 100;
        return true;
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
