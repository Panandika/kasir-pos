using System;
using System.Globalization;

namespace Kasir.Services
{
    // Scale of the stock ledger (stock_movements.qty_in / qty_out): quantity x 100,
    // the FoxPro QIN/QOUT N(13,2) convention that every legacy GHIST/GSMRY row and the
    // dashboard (pos_stock_requests.qty) already use. Documents (sale_items,
    // purchase_items, stock_adjustment_items, ...) keep the plain unit count the POS
    // has always written; convert at the ledger boundary with ToLedger.
    //
    // Before WP-02 the POS wrote plain units into the ledger, so GetStockOnHand mixed
    // x1 POS rows with x100 legacy rows on a snapshot-commissioned register.
    public static class StockQty
    {
        public const int Scale = 100;

        // Plain unit count -> ledger qty (x100). Checked: an overflow is a bug, not a
        // silent wrap into a negative stock movement.
        public static int ToLedger(int units)
        {
            return checked(units * Scale);
        }

        // Money value (x100) of a ledger qty at a unit cost (x100 money per whole unit):
        // unitCost * qty / 100, rounded half away from zero. The /100 undoes the qty
        // scale; without it val_in / val_out would be 100x too large.
        public static long Value(long unitCost, int ledgerQty)
        {
            decimal v = (decimal)unitCost * ledgerQty / Scale;
            return (long)Math.Round(v, MidpointRounding.AwayFromZero);
        }

        // Ledger qty -> display text in Indonesian style: "12", "12,5", "-0,25".
        public static string Format(int ledgerQty)
        {
            decimal units = (decimal)ledgerQty / Scale;
            var id = CultureInfo.GetCultureInfo("id-ID");
            return units.ToString("#,##0.##", id);
        }

        // Whole units of a ledger qty, truncated toward zero (for integer-only inputs).
        public static int ToUnits(int ledgerQty)
        {
            return ledgerQty / Scale;
        }
    }
}
