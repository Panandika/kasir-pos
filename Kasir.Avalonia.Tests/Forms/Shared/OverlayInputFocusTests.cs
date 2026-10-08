using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Kasir.Avalonia.Forms.Shared;
using NUnit.Framework;

namespace Kasir.Avalonia.Tests.Forms.Shared;

// Owner report (v2.10.1, #15/#18): a dialog opened with its input highlighted, but typing
// did nothing until the box was clicked. Rule: an overlay that opens with an input must
// take typing straight away.
[TestFixture]
public class OverlayInputFocusTests
{
    private static ShellWindow ScreenWithFocusedInput()
    {
        var window = new ShellWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var behind = new TextBox { Name = "Behind" };
        window.FindControl<ContentControl>("ContentArea")!.Content = behind;
        Dispatcher.UIThread.RunJobs();
        behind.Focus();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static TextBox OverlayInput(ShellWindow window) =>
        window.FindControl<ContentControl>("OverlayHost")!.GetVisualDescendants().OfType<TextBox>().First();

    [AvaloniaTest]
    public void InputDialog_FirstInput_HasKeyboardFocus_AndTypingReplacesDefault()
    {
        var window = ScreenWithFocusedInput();
        var result = InputDialogWindow.Show(window, "Buka Shift", new[] { "Kas awal (Rp)" }, new[] { "0" });
        Dispatcher.UIThread.RunJobs();

        var input = OverlayInput(window);
        Assert.That(input.IsFocused, Is.True, "the dialog's input must own keyboard focus");

        window.KeyTextInput("150000");
        Dispatcher.UIThread.RunJobs();
        Assert.That(input.Text, Is.EqualTo("150.000"), "typed digits must land in the input, replacing the default");

        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
        Assert.That(result.IsCompleted, Is.True);
        Assert.That(result.Result.values[0], Is.EqualTo("150.000"));
    }

    [AvaloniaTest]
    public void InputDialog_ShownTwiceInARow_SecondInputAlsoTakesTyping()
    {
        var window = ScreenWithFocusedInput();
        var first = InputDialogWindow.Show(window, "Ubah Qty", new[] { "Qty" }, new[] { "1" });
        Dispatcher.UIThread.RunJobs();
        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
        Assert.That(first.IsCompleted, Is.True);

        InputDialogWindow.Show(window, "Ubah Qty", new[] { "Qty" }, new[] { "1" });
        Dispatcher.UIThread.RunJobs();
        var input = OverlayInput(window);
        window.KeyTextInput("7");
        Dispatcher.UIThread.RunJobs();
        Assert.That(input.Text, Is.EqualTo("7"));
    }

    // #21: after any dialog closes, the keyboard goes back to the field that had it
    // (on the sale screen: the code box), no click needed.
    [AvaloniaTest]
    public void AfterDialogCloses_FocusReturnsToFieldBehind()
    {
        var window = ScreenWithFocusedInput();
        var behind = (TextBox)window.FindControl<ContentControl>("ContentArea")!.Content!;

        var info = MsgBox.Show(window, "Struk tidak tercetak.");
        Dispatcher.UIThread.RunJobs();
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
        Assert.That(info.IsCompleted, Is.True);
        Assert.That(behind.IsFocused, Is.True, "focus is back in the field behind the message");

        var input = InputDialogWindow.Show(window, "Ubah Qty", new[] { "Qty" }, new[] { "1" });
        Dispatcher.UIThread.RunJobs();
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
        Assert.That(input.IsCompleted, Is.True);
        window.KeyTextInput("123");
        Dispatcher.UIThread.RunJobs();
        Assert.That(behind.Text, Is.EqualTo("123"), "typing lands in the field behind the closed dialog");
    }
}
