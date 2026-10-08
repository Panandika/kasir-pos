using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Kasir.Avalonia.Forms.POS;
using NUnit.Framework;

namespace Kasir.Avalonia.Tests.Forms.POS;

// Owner report #17: F9 Kalkulator must be a real pocket calculator, and typing must
// work the instant it opens (no click first), even when the sale code box had focus.
[TestFixture]
public class CalculatorOverlayTests
{
    private static (ShellWindow window, TextBox behind) ScreenWithFocusedInput()
    {
        var window = new ShellWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var behind = new TextBox { Name = "TxtBarcode" };
        window.FindControl<ContentControl>("ContentArea")!.Content = behind;
        Dispatcher.UIThread.RunJobs();
        behind.Focus();
        Dispatcher.UIThread.RunJobs();
        Assert.That(behind.IsFocused, Is.True, "precondition: sale input behind the calculator has focus");
        return (window, behind);
    }

    private static CalculatorDialogOverlay Overlay(ShellWindow window) =>
        (CalculatorDialogOverlay)window.FindControl<ContentControl>("OverlayHost")!.Content!;

    private static string Display(CalculatorDialogOverlay o) => o.FindControl<TextBlock>("LblDisplay")!.Text!;
    private static string Expression(CalculatorDialogOverlay o) => o.FindControl<TextBlock>("LblExpression")!.Text!;

    private static void Type(ShellWindow w, Key key, string? symbol, RawInputModifiers mods = RawInputModifiers.None) =>
        w.KeyPress(key, mods, PhysicalKey.None, symbol);

    [AvaloniaTest]
    public void Opening_PutsKeyboardFocusInsideCalculator_Immediately()
    {
        var (window, behind) = ScreenWithFocusedInput();
        _ = CalculatorDialogWindow.Show(window);

        // No RunJobs, no click: focus must already have moved off the sale input.
        var overlay = Overlay(window);
        var focused = window.FocusManager!.GetFocusedElement() as Control;
        Assert.That(focused, Is.Not.Null);
        Assert.That(ReferenceEquals(focused, overlay) || overlay.IsVisualAncestorOf(focused!), Is.True,
            $"focus should be inside the calculator, was {focused}");
        Assert.That(behind.IsFocused, Is.False);

        Type(window, Key.D7, "7");
        Assert.That(Display(overlay), Is.EqualTo("7"));
        Assert.That(behind.Text ?? "", Is.Empty, "typed digits must not leak into the sale input");
    }

    [AvaloniaTest]
    public void TypedKeys_ReachCalculator_WithoutClicking()
    {
        var (window, behind) = ScreenWithFocusedInput();
        var result = CalculatorDialogWindow.Show(window);
        Dispatcher.UIThread.RunJobs();
        var overlay = Overlay(window);

        Type(window, Key.D1, "1");
        Type(window, Key.D2, "2");
        Type(window, Key.D5, "5");
        Type(window, Key.D0, "0");
        Assert.That(Display(overlay), Is.EqualTo("1.250"));

        Type(window, Key.D8, "*", RawInputModifiers.Shift);   // Shift+8 = '*'
        Assert.That(Expression(overlay), Is.EqualTo("1.250 ×"));
        Type(window, Key.NumPad4, null);
        Type(window, Key.Enter, null);
        Assert.That(Display(overlay), Is.EqualTo("5.000"));
        Assert.That(Expression(overlay), Is.EqualTo("1.250 × 4 ="));

        Type(window, Key.Add, null);                             // numpad +
        Type(window, Key.D5, "5");
        Type(window, Key.OemPlus, "=");                          // '=' key
        Assert.That(Display(overlay), Is.EqualTo("5.005"));

        Type(window, Key.Delete, null);
        Assert.That(Display(overlay), Is.EqualTo("0"));

        Type(window, Key.D9, "9");
        Type(window, Key.D9, "9");
        Type(window, Key.Back, null);
        Assert.That(Display(overlay), Is.EqualTo("9"));

        Assert.That(result.IsCompleted, Is.False, "calculator stays open while typing");
        Assert.That(behind.Text ?? "", Is.Empty);
    }

    [AvaloniaTest]
    public void DivideByZero_ShowsMessage()
    {
        var (window, _) = ScreenWithFocusedInput();
        _ = CalculatorDialogWindow.Show(window);
        Dispatcher.UIThread.RunJobs();
        var overlay = Overlay(window);

        Type(window, Key.D5, "5");
        Type(window, Key.Divide, null);
        Type(window, Key.D0, "0");
        Type(window, Key.Enter, null);
        Assert.That(Display(overlay), Is.EqualTo("Tidak bisa dibagi 0"));
    }

    [AvaloniaTest]
    public void Escape_ClosesCalculator()
    {
        var (window, _) = ScreenWithFocusedInput();
        var result = CalculatorDialogWindow.Show(window);
        Dispatcher.UIThread.RunJobs();

        Type(window, Key.D3, "3");
        Type(window, Key.Escape, null);
        Dispatcher.UIThread.RunJobs();

        Assert.That(result.IsCompleted, Is.True, "Esc must close the calculator");
        var host = window.FindControl<ContentControl>("OverlayHost")!;
        Assert.That(host.IsVisible, Is.False);
        Assert.That(host.Content, Is.Null);
    }

    [AvaloniaTest]
    public void KeypadButtons_AreClickable_AndKeyboardStillWorksAfter()
    {
        var (window, _) = ScreenWithFocusedInput();
        _ = CalculatorDialogWindow.Show(window);
        Dispatcher.UIThread.RunJobs();
        var overlay = Overlay(window);
        var keypad = overlay.FindControl<Grid>("Keypad")!;

        Button Btn(string tag)
        {
            foreach (var c in keypad.Children)
                if (c is Button { Tag: string t } b && t == tag) return b;
            throw new AssertionException("no key " + tag);
        }

        foreach (var tag in new[] { "2", "000", "+", "5", "=" })
            Btn(tag).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.That(Display(overlay), Is.EqualTo("2.005"));

        Type(window, Key.Delete, null);
        Assert.That(Display(overlay), Is.EqualTo("0"), "keyboard keeps working after a click");
    }

    [TestCase(Key.D7, RawInputModifiers.None, "7", ExpectedResult = "7")]
    [TestCase(Key.D7, RawInputModifiers.None, null, ExpectedResult = "7")]
    [TestCase(Key.NumPad3, RawInputModifiers.None, null, ExpectedResult = "3")]
    [TestCase(Key.Subtract, RawInputModifiers.None, null, ExpectedResult = "-")]
    [TestCase(Key.OemMinus, RawInputModifiers.None, "-", ExpectedResult = "-")]
    [TestCase(Key.Multiply, RawInputModifiers.None, null, ExpectedResult = "*")]
    [TestCase(Key.D8, RawInputModifiers.Shift, null, ExpectedResult = "*")]
    [TestCase(Key.Divide, RawInputModifiers.None, null, ExpectedResult = "/")]
    [TestCase(Key.Oem2, RawInputModifiers.None, "/", ExpectedResult = "/")]
    [TestCase(Key.OemPlus, RawInputModifiers.Shift, "+", ExpectedResult = "+")]
    [TestCase(Key.OemPlus, RawInputModifiers.None, "=", ExpectedResult = "=")]
    [TestCase(Key.Enter, RawInputModifiers.None, null, ExpectedResult = "=")]
    [TestCase(Key.Back, RawInputModifiers.None, null, ExpectedResult = "back")]
    [TestCase(Key.Delete, RawInputModifiers.None, null, ExpectedResult = "C")]
    [TestCase(Key.C, RawInputModifiers.None, "c", ExpectedResult = "C")]
    [TestCase(Key.OemComma, RawInputModifiers.None, ",", ExpectedResult = ",")]
    [TestCase(Key.Decimal, RawInputModifiers.None, null, ExpectedResult = ",")]
    [TestCase(Key.D1, RawInputModifiers.Shift, "!", ExpectedResult = null)]
    [TestCase(Key.C, RawInputModifiers.Control, "c", ExpectedResult = null)]
    public string? MapKey(Key key, RawInputModifiers mods, string? symbol) =>
        CalculatorDialogOverlay.MapKey(key, (KeyModifiers)(int)mods, symbol);
}
