using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Kasir.Models;

namespace Kasir.Data.Repositories
{
    // Persists the in-progress POS cart to the pending_sales table so a crash mid-sale can
    // recover the cart on restart instead of silently losing it (F36). One draft cart per
    // register, keyed by a synthetic draft journal_no ("PENDING-<register>").
    public class PendingSaleRepository
    {
        private readonly SqliteConnection _db;

        public PendingSaleRepository(SqliteConnection db)
        {
            _db = db;
        }

        public void Save(string draftKey, List<SaleItem> items)
        {
            using (var txn = _db.BeginTransaction())
            {
                try
                {
                    SqlHelper.ExecuteNonQuery(_db,
                        "DELETE FROM pending_sales WHERE journal_no = @key",
                        SqlHelper.Param("@key", draftKey));

                    foreach (var it in items)
                    {
                        SqlHelper.ExecuteNonQuery(_db,
                            @"INSERT INTO pending_sales
                              (journal_no, product_code, quantity, unit_price, cogs, value, disc_pct, remark)
                              VALUES (@key, @pc, @qty, @price, @cogs, @val, @disc, @remark)",
                            SqlHelper.Param("@key", draftKey),
                            SqlHelper.Param("@pc", it.ProductCode),
                            SqlHelper.Param("@qty", it.Quantity),
                            SqlHelper.Param("@price", it.UnitPrice),
                            SqlHelper.Param("@cogs", it.Cogs),
                            SqlHelper.Param("@val", it.Value),
                            SqlHelper.Param("@disc", it.DiscPct),
                            SqlHelper.Param("@remark", it.Remark ?? ""));
                    }

                    txn.Commit();
                }
                catch { txn.Rollback(); throw; }
            }
        }

        public List<SaleItem> Load(string draftKey)
        {
            return SqlHelper.Query(_db,
                "SELECT * FROM pending_sales WHERE journal_no = @key ORDER BY id",
                r => new SaleItem
                {
                    ProductCode = SqlHelper.GetString(r, "product_code"),
                    Quantity = SqlHelper.GetInt(r, "quantity"),
                    UnitPrice = SqlHelper.GetLong(r, "unit_price"),
                    Cogs = SqlHelper.GetLong(r, "cogs"),
                    Value = SqlHelper.GetLong(r, "value"),
                    DiscPct = SqlHelper.GetInt(r, "disc_pct"),
                    Remark = SqlHelper.GetString(r, "remark")
                },
                SqlHelper.Param("@key", draftKey));
        }

        public void Clear(string draftKey)
        {
            SqlHelper.ExecuteNonQuery(_db,
                "DELETE FROM pending_sales WHERE journal_no = @key",
                SqlHelper.Param("@key", draftKey));
        }
    }
}
