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
}
