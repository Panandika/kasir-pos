using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;

namespace Kasir.Avalonia.Forms.Shared;

public static class MsgBox
{
    private static Window? Resolve(Visual? owner)
    {
        if (owner is Window w) return w;
        return TopLevel.GetTopLevel(owner) as Window;
    }

    public static async Task<bool> Confirm(Visual? owner, string message, string title = "Konfirmasi")
    {
        var dlg = new MsgBoxWindow(title, message, true);
        await dlg.ShowDialog(Resolve(owner)!);
        return dlg.Result;
    }

    public static async Task Show(Visual? owner, string message, string title = "Info")
    {
        var dlg = new MsgBoxWindow(title, message, false);
        await dlg.ShowDialog(Resolve(owner)!);
    }
}
