using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Kasir.Services;

namespace Kasir.Avalonia.Forms.POS;

// F9 Kalkulator on the sale screen (owner report #17). A standalone pocket calculator:
// the old dialog's OK result was never used by SaleView, so there is no "use result".
// Arithmetic lives in Kasir.Services.CalculatorEngine (immediate execution, decimal).
public partial class CalculatorDialogOverlay : UserControl
{
    private readonly TaskCompletionSource<bool> _tcs = new();
    private readonly CalculatorEngine _engine = new();

    public CalculatorDialogOverlay()
    {
        InitializeComponent();

        foreach (var child in Keypad.Children)
            if (child is Button b) b.Click += OnKeypadClick;
        BtnTutup.Click += (_, _) => Close();

        // Keypad buttons are not focusable, so the overlay itself owns keyboard focus.
        // Focus now and again once laid out (focus before layout can be lost to the
        // screen behind), so typing works without clicking first.
        AttachedToVisualTree += (_, _) =>
        {
            Focus();
            Dispatcher.UIThread.Post(() => Focus(), DispatcherPriority.Loaded);
        };
        KeyDown += OnKey;

        Refresh();
    }

    public Task<bool> Result => _tcs.Task;

    public CalculatorEngine Engine => _engine;

    private void Close() => _tcs.TrySetResult(false);

    private void OnKeypadClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string key }) Apply(key);
        Focus();
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (KeyboardRouter.IsEscape(e)) { e.Handled = true; Close(); return; }
        string? key = MapKey(e.Key, e.KeyModifiers, e.KeySymbol);
        if (key is null) return;
        e.Handled = true;
        Apply(key);
    }

    // Translates a key press to a keypad tag ("0".."9", "000", ",", "+", "-", "*", "/",
    // "=", "C", "back"). Non-character keys (Enter, Backspace, Delete, numpad) are
    // matched by Key; typed characters by KeySymbol so '+' '*' work on any layout,
    // with a US-layout fallback when no symbol is reported. '.' is treated as the
    // decimal mark too (numpad decimal key).
    public static string? MapKey(Key key, KeyModifiers mods, string? symbol)
    {
        if ((mods & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta)) != 0) return null;
        bool shift = (mods & KeyModifiers.Shift) != 0;
        switch (key)
        {
            case Key.Enter: return "=";
            case Key.Back: return "back";
            case Key.Delete: return "C";
            case Key.Add: return "+";
            case Key.Subtract: return "-";
            case Key.Multiply: return "*";
            case Key.Divide: return "/";
            case Key.Decimal: return ",";
            case >= Key.NumPad0 and <= Key.NumPad9: return ((char)('0' + (key - Key.NumPad0))).ToString();
        }
        if (symbol is { Length: 1 })
        {
            char c = symbol[0];
            if (c >= '0' && c <= '9') return symbol;
            switch (c)
            {
                case '+': return "+";
                case '-': return "-";
                case '*': case 'x': case 'X': return "*";
                case '/': case ':': return "/";
                case '=': return "=";
                case ',': case '.': return ",";
                case 'c': case 'C': return "C";
            }
        }
        switch (key)
        {
            case >= Key.D0 and <= Key.D9 when !shift:
                return ((char)('0' + (key - Key.D0))).ToString();
            case Key.D8 when shift: return "*";
            case Key.OemPlus: return shift ? "+" : "=";
            case Key.OemMinus when !shift: return "-";
            case Key.Oem2 when !shift: return "/";
            case Key.OemComma when !shift: return ",";
            case Key.OemPeriod when !shift: return ",";
            case Key.X: return "*";
            case Key.C: return "C";
        }
        return null;
    }

    public void Apply(string key)
    {
        switch (key)
        {
            case "000": _engine.TripleZero(); break;
            case ",": _engine.DecimalPoint(); break;
            case "+": _engine.Operator(CalculatorOperator.Add); break;
            case "-": _engine.Operator(CalculatorOperator.Subtract); break;
            case "*": _engine.Operator(CalculatorOperator.Multiply); break;
            case "/": _engine.Operator(CalculatorOperator.Divide); break;
            case "=": _engine.Evaluate(); break;
            case "C": _engine.Clear(); break;
            case "back": _engine.Backspace(); break;
            default:
                if (key.Length == 1 && key[0] >= '0' && key[0] <= '9') _engine.Digit(key[0] - '0');
                break;
        }
        Refresh();
    }

    private void Refresh()
    {
        LblDisplay.Text = _engine.Display;
        LblExpression.Text = _engine.Expression.Length == 0 ? " " : _engine.Expression;
        LblDisplay.Classes.Set("error", _engine.HasError);
    }
}
