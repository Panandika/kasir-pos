using System.Globalization;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Kasir.Avalonia.Behaviors;
using Kasir.Models;

namespace Kasir.Avalonia.Forms.Master;

public partial class WholesaleTierDialog : Window
{
    private readonly Product _product;

    public WholesaleTierDialog() : this(new Product()) { }

    public WholesaleTierDialog(Product product)
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
        BtnCancel.Click += (_, _) => Close(false);
        KeyDown += OnKeyDown;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F10 || e.Key == Key.Enter)
        {
            e.Handled = true;
            OnSave();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close(false);
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
        Close(true);
    }

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
