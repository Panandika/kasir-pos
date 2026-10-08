using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Kasir.Avalonia.Infrastructure;

public static class ViewShortcuts
{
    /// <summary>
    /// Catch Enter key on a DataGrid before its internal handler moves to the next cell.
    /// Invokes onEdit when Enter is pressed.
    /// </summary>
    public static void WireGridEnter(DataGrid grid, Action onEdit)
    {
        grid.AddHandler(InputElement.KeyDownEvent,
            (object? s, KeyEventArgs e) =>
            {
                if (e.Key == Key.Enter)
                {
                    e.Handled = true;
                    onEdit();
                }
            },
            RoutingStrategies.Tunnel);
    }

    /// <summary>
    /// Focus a control after the visual tree has settled (deferred to Background
    /// priority so it runs after layout pass).
    /// </summary>
    public static void AutoFocus(Control? target)
    {
        if (target == null) return;
        Dispatcher.UIThread.Post(() => target.Focus(), DispatcherPriority.Background);
    }

    /// <summary>
    /// Focus <paramref name="target"/> the first time <paramref name="host"/>
    /// attaches to a visual tree. Ensures key events have a focused listener
    /// immediately on entry — otherwise users must click inside the view
    /// before Esc / shortcut keys start firing.
    /// </summary>
    public static void AutoFocusOnAttach(Control host, Control? target)
    {
        if (target == null) return;
        host.AttachedToVisualTree += (_, _) =>
            Dispatcher.UIThread.Post(() => target.Focus(), DispatcherPriority.Background);
    }

    /// <summary>
    /// Put the keyboard in <paramref name="input"/> as soon as <paramref name="host"/> is
    /// shown, so typing works without a click. Calling Focus() from the host's
    /// AttachedToVisualTree does nothing: the host is attached before its children, so
    /// the input is not in the tree yet and the call is silently ignored, while
    /// SelectAll() still paints the text as highlighted. Deferred to Loaded priority
    /// (children attached, before input is processed). Re-runs on every attach, so a
    /// dialog shown again also gets focus.
    /// </summary>
    public static void FocusInputOnShow(Control host, InputElement input, bool selectAll = true)
    {
        host.AttachedToVisualTree += (_, _) =>
            Dispatcher.UIThread.Post(() => FocusInput(input, selectAll), DispatcherPriority.Loaded);
    }

    /// <summary>Focus <paramref name="input"/> now and (for a TextBox) select its text.</summary>
    public static void FocusInput(InputElement input, bool selectAll = true)
    {
        input.Focus();
        if (selectAll && input is TextBox tb) tb.SelectAll();
    }
}
