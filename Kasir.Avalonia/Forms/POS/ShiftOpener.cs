using System;
using System.Threading.Tasks;
using Kasir.Avalonia.Forms.Shared;
using Kasir.Avalonia.Navigation;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Models;
using Kasir.Utils;

namespace Kasir.Avalonia.Forms.POS;

// Asks for the opening cash and opens a shift for this register. Used by the shift
// screen and directly by the sale screen, so the cashier can open the shift without
// leaving Penjualan.
public static class ShiftOpener
{
    /// <summary>Returns the opened shift, or null when cancelled or the amount is invalid.</summary>
    public static async Task<Shift?> PromptAndOpenAsync(int cashierId, string label = "Kas awal (Rp)")
    {
        var (ok, vals) = await InputDialogWindow.Show(NavigationService.Owner,
            "Buka Shift",
            new[] { label },
            new[] { "0" });

        if (!ok) return null;

        if (!Formatting.TryParseRupiah(vals[0], out long openingCash))
        {
            await MsgBox.Show(NavigationService.Owner, "Jumlah tidak valid.");
            return null;
        }

        var conn = DbConnection.GetConnection();
        string regId = new ConfigRepository(conn).Get("register_id") ?? "01";
        var shiftRepo = new ShiftRepository(conn);
        string today = DateTime.Now.ToString("yyyy-MM-dd");
        var shift = new Shift
        {
            RegisterId = regId,
            CashierId = cashierId,
            OpenedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            OpeningCash = openingCash * 100
        };
        shiftRepo.OpenShiftAtomic(shift, today);
        return shiftRepo.GetOpenShift(regId);
    }
}
