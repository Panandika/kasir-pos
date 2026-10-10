using System;

namespace Kasir.Services
{
    // One cost rule for a purchase line (owner decision D28, RALPLAN OB-1), per stock
    // unit, money x100. Mirrors the dashboard's public.legacy_line_unit_cost
    // (migration 0071) and _LEGACY_UNIT_COST_SQL in win-deploy dbf_to_supabase.py;
    // keep the three in step.
    //   cogs (legacy POKOK: after line discount, after the line's share of the invoice
    //         discount, PPN included) when cogs > 0,
    //   else unit_price - disc_value (COST - DISCRP), never below 0.
    // Legacy DISCRP lands in purchase_items.disc_value on both kasir.db (migrate.py)
    // and Supabase (dbf_mappers.py); disc_amount is not the line discount.
    public static class LegacyCost
    {
        public static long UnitCost(long cogs, long unitPrice, long discValue) =>
            cogs > 0 ? cogs : Math.Max(unitPrice - discValue, 0);

        // The same rule as a SQLite expression over a purchase_items alias.
        public static string UnitCostSql(string alias) =>
            $"(CASE WHEN COALESCE({alias}.cogs, 0) > 0 THEN {alias}.cogs "
            + $"ELSE MAX(COALESCE({alias}.unit_price, 0) - COALESCE({alias}.disc_value, 0), 0) END)";
    }
}
