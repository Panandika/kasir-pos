using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Kasir.Utils;

namespace Kasir.Avalonia.Behaviors;

/// <summary>
/// Behaviour helpers for numeric TextBoxes. Matches the static-helper style of
/// <see cref="Infrastructure.ViewShortcuts"/> instead of using
/// Avalonia's Behavior&lt;T&gt; — keeps wiring at the call site explicit.
///
/// Two effects:
/// 1. <see cref="Attach"/> — on focus, clears the box if it currently shows
///    "0", "0.00" or "0,00"; otherwise selects all so the next keystroke
///    overwrites the previous value.
/// 2. <see cref="AttachLiveFormatting"/> — additionally applies live
///    Indonesian thousands formatting via <see cref="IndonesianMoneyFormatter"/>
///    while preserving the caret's distance-from-right.
/// </summary>
public static class NumericInputBehavior
{
    /// <summary>
    /// Wires the clear-on-focus / select-all-on-focus behaviour for a numeric
    /// TextBox.
    /// </summary>
    public static void Attach(TextBox textBox)
    {
        if (textBox == null) return;
        textBox.GotFocus += OnGotFocus;
    }

    /// <summary>
    /// Wires <see cref="Attach"/> plus live Indonesian thousands formatting on
    /// every TextChanged. Caret position is preserved by counting digits from
    /// the right.
    /// </summary>
    /// <param name="allowDecimals">Keep a ",dd" sen part (price fields). Leave false for
    /// cash/tender fields so a typed "50,000" can never become Rp 50.</param>
    public static void AttachLiveFormatting(TextBox textBox, bool allowDecimals = false)
    {
        if (textBox == null) return;
        Attach(textBox);
        if (allowDecimals) _decimalBoxes.AddOrUpdate(textBox, new StrongBox<bool>(true));
        textBox.TextChanged += OnTextChanged;
    }

    private static readonly ConditionalWeakTable<TextBox, StrongBox<bool>> _decimalBoxes = new();

    private static void OnGotFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is not TextBox tb) return;
        string s = tb.Text ?? "";
        if (s == "0" || s == "0.00" || s == "0,00")
        {
            tb.Clear();
        }
        else
        {
            tb.SelectAll();
        }
    }

    // Re-entry guard: setting Text from inside TextChanged would otherwise
    // fire the handler again and cause an infinite loop. Per-TextBox so that
    // formatting one box never blocks formatting on another, and entries are
    // collected automatically when the TextBox is GC'd.
    private static readonly ConditionalWeakTable<TextBox, StrongBox<bool>> _suppressMap = new();

    private static void OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox tb) return;
        var flag = _suppressMap.GetValue(tb, _ => new StrongBox<bool>(false));
        if (flag.Value) return;

        string original = tb.Text ?? "";
        int caret = tb.CaretIndex;

        bool allowDecimals = _decimalBoxes.TryGetValue(tb, out _);
        var (formatted, newCaret) = IndonesianMoneyFormatter.ReformatPreserveCaret(original, caret, allowDecimals);
        if (formatted == original) return;

        flag.Value = true;
        try
        {
            tb.Text = formatted;
            // CaretIndex must be set after Text — schedule on the dispatcher
            // so Avalonia doesn't reset it during its own text-change pass.
            int target = newCaret;
            Dispatcher.UIThread.Post(() =>
            {
                if (target >= 0 && target <= (tb.Text ?? "").Length)
                    tb.CaretIndex = target;
            }, DispatcherPriority.Background);
        }
        finally
        {
            flag.Value = false;
        }
    }
}
