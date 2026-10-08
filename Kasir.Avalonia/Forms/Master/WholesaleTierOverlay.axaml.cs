using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Kasir.Avalonia.Behaviors;
using Kasir.Models;
using Kasir.Utils;
using Kasir.Avalonia.Infrastructure;

namespace Kasir.Avalonia.Forms.Master;

public partial class WholesaleTierOverlay : UserControl
{
    private readonly Product _product;
    private readonly TaskCompletionSource<bool> _tcs = new();

    public WholesaleTierOverlay() : this(new Product()) { }

    public WholesaleTierOverlay(Product product)
    {
        InitializeComponent();
        _product = product;

        NumericInputBehavior.AttachLiveFormatting(TxtPrice1, allowDecimals: true);
        NumericInputBehavior.AttachLiveFormatting(TxtPrice2, allowDecimals: true);
        NumericInputBehavior.AttachLiveFormatting(TxtPrice3, allowDecimals: true);
        NumericInputBehavior.AttachLiveFormatting(TxtPrice4, allowDecimals: true);
        NumericInputBehavior.Attach(TxtQtyBreak2);
        NumericInputBehavior.Attach(TxtQtyBreak3);

        TxtPrice1.Text = FormatMoney(product.Price1);
        TxtPrice2.Text = FormatMoney(product.Price2);
        TxtPrice3.Text = FormatMoney(product.Price3);
        TxtPrice4.Text = FormatMoney(product.Price4);
        TxtQtyBreak2.Text = product.QtyBreak2.ToString();
        TxtQtyBreak3.Text = product.QtyBreak3.ToString();

        BtnOk.Click += (_, _) => OnSave();
        BtnCancel.Click += (_, _) => _tcs.TrySetResult(false);
        ViewShortcuts.FocusInputOnShow(this, TxtPrice1);
        KeyDown += OnKey;
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F10 || e.Key == Key.Enter)
        {
            e.Handled = true;
            OnSave();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            _tcs.TrySetResult(false);
        }
    }

    private void OnSave()
    {
        // An unparseable price would save as 0: keep the overlay open on the bad field instead.
        foreach (var box in new[] { TxtPrice1, TxtPrice2, TxtPrice3, TxtPrice4 })
        {
            if (!string.IsNullOrWhiteSpace(box.Text) && !Formatting.TryParseRupiahCents(box.Text, out _))
            {
                box.BorderBrush = global::Avalonia.Media.Brushes.IndianRed;
                box.Focus();
                box.SelectAll();
                return;
            }
        }
        _product.Price1 = ParseMoney(TxtPrice1.Text);
        _product.Price2 = ParseMoney(TxtPrice2.Text);
        _product.Price3 = ParseMoney(TxtPrice3.Text);
        _product.Price4 = ParseMoney(TxtPrice4.Text);
        _product.QtyBreak2 = ParseInt(TxtQtyBreak2.Text);
        _product.QtyBreak3 = ParseInt(TxtQtyBreak3.Text);
        _tcs.TrySetResult(true);
    }

    public Task<bool> Result => _tcs.Task;

    // Keep sen: showing whole rupiah and saving it back used to drop them silently.
    private static string FormatMoney(long cents)
    {
        return Formatting.FormatRupiahCentsInput(cents);
    }

    private static long ParseMoney(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0L;
        return Formatting.TryParseRupiahCents(text, out long cents) ? cents : 0L;
    }

    private static int ParseInt(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        string digits = new string((text ?? "").Where(char.IsDigit).ToArray());
        if (string.IsNullOrEmpty(digits)) return 0;
        return int.Parse(digits, CultureInfo.InvariantCulture);
    }
}

// Compatibility shim preserving the WholesaleTierDialog surface used by ProductView.
public static class WholesaleTierDialog
{
    public static async Task<bool> Show(Visual? owner, Product product)
    {
        ShellWindow? shell = TopLevel.GetTopLevel(owner) as ShellWindow;
        if (shell is null
            && Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            shell = desktop.MainWindow as ShellWindow;
        }
        if (shell is null) return false;

        var overlay = new WholesaleTierOverlay(product);
        shell.ShowOverlay(overlay);
        try { return await overlay.Result; }
        finally { shell.HideOverlay(); }
    }
}
