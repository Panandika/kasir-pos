using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Kasir.Avalonia.Behaviors;
using Kasir.Models;

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

        NumericInputBehavior.AttachLiveFormatting(TxtPrice1);
        NumericInputBehavior.AttachLiveFormatting(TxtPrice2);
        NumericInputBehavior.AttachLiveFormatting(TxtPrice3);
        NumericInputBehavior.AttachLiveFormatting(TxtPrice4);
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
        AttachedToVisualTree += (_, _) => TxtPrice1.Focus();
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
        _product.Price1 = ParseMoney(TxtPrice1.Text);
        _product.Price2 = ParseMoney(TxtPrice2.Text);
        _product.Price3 = ParseMoney(TxtPrice3.Text);
        _product.Price4 = ParseMoney(TxtPrice4.Text);
        _product.QtyBreak2 = ParseInt(TxtQtyBreak2.Text);
        _product.QtyBreak3 = ParseInt(TxtQtyBreak3.Text);
        _tcs.TrySetResult(true);
    }

    public Task<bool> Result => _tcs.Task;

    private static string FormatMoney(long cents)
    {
        long whole = cents / 100;
        return whole.ToString("#,0", CultureInfo.GetCultureInfo("id-ID"));
    }

    private static long ParseMoney(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0L;
        string digits = new string((text ?? "").Where(char.IsDigit).ToArray());
        if (string.IsNullOrEmpty(digits)) return 0L;
        return long.Parse(digits, CultureInfo.InvariantCulture) * 100L;
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
