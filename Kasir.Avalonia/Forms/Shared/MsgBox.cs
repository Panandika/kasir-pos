using System.Threading.Tasks;
using Avalonia.Controls;

namespace Kasir.Avalonia.Forms.Shared;

public static class MsgBox
{
    public static async Task<bool> Confirm(Window owner, string message, string title = "Konfirmasi")
    {
        var dlg = new MsgBoxWindow(title, message, true);
        await dlg.ShowDialog(owner);
        return dlg.Result;
    }

    public static async Task Show(Window owner, string message, string title = "Info")
    {
        var dlg = new MsgBoxWindow(title, message, false);
        await dlg.ShowDialog(owner);
    }
}
