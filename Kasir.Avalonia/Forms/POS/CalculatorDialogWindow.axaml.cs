using Avalonia.Controls;
using Avalonia.Input;

namespace Kasir.Avalonia.Forms.POS;

public partial class CalculatorDialogWindow : Window
{
    public bool Accepted { get; private set; }

    public CalculatorDialogWindow()
    {
        InitializeComponent();

        TxtA.TextChanged += (_, _) => UpdateCalc();
        TxtB.TextChanged += (_, _) => UpdateCalc();
        TxtC.TextChanged += (_, _) => UpdateCalc();
        TxtD.TextChanged += (_, _) => UpdateCalc();

        BtnOk.Click += (_, _) => { Accepted = true; Close(); };
        BtnTutup.Click += (_, _) => { Accepted = false; Close(); };

        UpdateCalc();
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

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (KeyboardRouter.IsEscape(e))
        {
            e.Handled = true;
            Accepted = false;
            Close();
        }
    }
}
