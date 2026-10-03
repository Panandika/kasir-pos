using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Kasir.Avalonia.Forms.Shared;
using NUnit.Framework;

namespace Kasir.Avalonia.Tests.Forms.Shared;

// Owner report 2026-10-03: in "Tinggalkan transaksi…?" Enter/Esc did nothing —
// the keys went to the sale screen's input box behind the dialog.
[TestFixture]
public class MsgBoxKeyboardTests
{
    private static (ShellWindow window, TextBox behind) ScreenWithFocusedInput()
    {
        var window = new ShellWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs(); // let the shell's own startup content settle first
        var behind = new TextBox { Name = "Behind" };
        window.FindControl<ContentControl>("ContentArea")!.Content = behind;
        Dispatcher.UIThread.RunJobs();
        behind.Focus();
        Dispatcher.UIThread.RunJobs();
        Assert.That(behind.IsFocused, Is.True, "precondition: input behind the dialog has focus");
        return (window, behind);
    }

    private static bool PressAndGet(ShellWindow window, Task<bool> result, Key key)
    {
        Dispatcher.UIThread.RunJobs();
        window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
        Assert.That(result.IsCompleted, Is.True, $"{key} must answer the dialog");
        return result.Result;
    }

    [AvaloniaTest]
    public void Escape_AnswersNo()
    {
        var (window, _) = ScreenWithFocusedInput();
        var result = MsgBox.Confirm(window, "Tinggalkan transaksi?");
        Assert.That(PressAndGet(window, result, Key.Escape), Is.False);
    }

    [AvaloniaTest]
    public void Enter_AnswersYes()
    {
        var (window, _) = ScreenWithFocusedInput();
        var result = MsgBox.Confirm(window, "Tinggalkan transaksi?");
        Assert.That(PressAndGet(window, result, Key.Enter), Is.True);
    }

    [AvaloniaTest]
    public void InputDialog_Escape_Cancels_And_Enter_Accepts()
    {
        var (window, _) = ScreenWithFocusedInput();
        var cancelled = InputDialogWindow.Show(window, "Detail Item", new[] { "Qty" }, new[] { "1" });
        Dispatcher.UIThread.RunJobs();
        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
        Assert.That(cancelled.IsCompleted, Is.True, "Esc must close the input dialog");
        Assert.That(cancelled.Result.ok, Is.False);

        var accepted = InputDialogWindow.Show(window, "Detail Item", new[] { "Qty" }, new[] { "1" });
        Dispatcher.UIThread.RunJobs();
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
        Assert.That(accepted.IsCompleted, Is.True, "Enter must accept the input dialog");
        Assert.That(accepted.Result.ok, Is.True);
    }
}
