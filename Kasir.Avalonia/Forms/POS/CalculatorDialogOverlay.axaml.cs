using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;

namespace Kasir.Avalonia.Forms.POS;

public partial class CalculatorDialogOverlay : UserControl
{
    private readonly TaskCompletionSource<bool> _tcs = new();

    public CalculatorDialogOverlay()
    {
        InitializeComponent();

        TxtA.TextChanged += (_, _) => UpdateCalc();
        TxtB.TextChanged += (_, _) => UpdateCalc();
        TxtC.TextChanged += (_, _) => UpdateCalc();
        TxtD.TextChanged += (_, _) => UpdateCalc();

        BtnOk.Click += (_, _) => _tcs.TrySetResult(true);
        BtnTutup.Click += (_, _) => _tcs.TrySetResult(false);

        AttachedToVisualTree += (_, _) => TxtA.Focus();
        KeyDown += OnKey;

        UpdateCalc();
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (KeyboardRouter.IsEscape(e))
        {
            e.Handled = true;
            _tcs.TrySetResult(false);
        }
    }

    private void UpdateCalc()
    {
        long a = ParseNum(TxtA.Text), b = ParseNum(TxtB.Text);
        try
        {
            LblMultResult.Text = checked(a * b).ToString("N0");
        }
        catch
        {
            LblMultResult.Text = "OVERFLOW";
        }
        LblAddResult.Text = (ParseNum(TxtC.Text) + ParseNum(TxtD.Text)).ToString("N0");
    }

    private static long ParseNum(string? t)
    {
        long v;
        long.TryParse((t ?? "").Replace(",", "").Replace(".", ""), out v);
        return v;
    }

    public Task<bool> Result => _tcs.Task;
}
