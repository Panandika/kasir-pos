using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace Kasir.Avalonia.Forms.POS;

// Compatibility shim: callers invoke CalculatorDialogWindow.Show(...). The dialog
// now renders as an in-window overlay hosted by ShellWindow rather than a separate
// OS-level Window.
public static class CalculatorDialogWindow
{
    public static async Task<bool> Show(Visual? owner)
    {
        ShellWindow? shell = TopLevel.GetTopLevel(owner) as ShellWindow;
        if (shell is null
            && Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            shell = desktop.MainWindow as ShellWindow;
        }
        if (shell is null) return false;

        var overlay = new CalculatorDialogOverlay();
        shell.ShowOverlay(overlay);
        try { return await overlay.Result; }
        finally { shell.HideOverlay(); }
    }
}
