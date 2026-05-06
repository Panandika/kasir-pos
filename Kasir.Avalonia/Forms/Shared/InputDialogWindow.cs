using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace Kasir.Avalonia.Forms.Shared;

// Compatibility shim: callers still invoke InputDialogWindow.Show(...). The dialog
// now renders as an in-window overlay hosted by ShellWindow rather than as a
// separate OS-level Window (which had macOS focus/transparency issues).
public static class InputDialogWindow
{
    public static async Task<(bool ok, string[] values)> Show(
        Visual? owner, string title, string[] labels, string[] defaults)
    {
        ShellWindow? shell = TopLevel.GetTopLevel(owner) as ShellWindow;
        if (shell is null
            && Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            shell = desktop.MainWindow as ShellWindow;
        }

        if (shell is null)
        {
            return (false, Array.Empty<string>());
        }

        var overlay = new InputDialogOverlay(title, labels, defaults);
        shell.ShowOverlay(overlay);
        try
        {
            return await overlay.Result;
        }
        finally
        {
            shell.HideOverlay();
        }
    }
}
