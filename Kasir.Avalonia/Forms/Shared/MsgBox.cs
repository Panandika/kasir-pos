using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace Kasir.Avalonia.Forms.Shared;

public static class MsgBox
{
    private static ShellWindow? Resolve(Visual? owner)
    {
        ShellWindow? shell = TopLevel.GetTopLevel(owner) as ShellWindow;
        if (shell is null
            && Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            shell = desktop.MainWindow as ShellWindow;
        }
        return shell;
    }

    public static async Task<bool> Confirm(Visual? owner, string message, string title = "Konfirmasi")
    {
        var shell = Resolve(owner);
        if (shell is null) return false;
        var overlay = new MsgBoxOverlay(title, message, true);
        shell.ShowOverlay(overlay);
        try { return await overlay.Result; }
        finally { shell.HideOverlay(); }
    }

    public static async Task Show(Visual? owner, string message, string title = "Info")
    {
        var shell = Resolve(owner);
        if (shell is null) return;
        var overlay = new MsgBoxOverlay(title, message, false);
        shell.ShowOverlay(overlay);
        try { await overlay.Result; }
        finally { shell.HideOverlay(); }
    }
}
