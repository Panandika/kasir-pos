using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Data.Sqlite;
using Kasir.Models;

namespace Kasir.Data.Repositories
{
    public class StockMovementRepository
    {
        private readonly SqliteConnection _db;

        public StockMovementRepository(SqliteConnection db)
        {
            _db = db;
        }

        // OB-13: stock_movements ids >= this are dashboard-originated (Kasir.CloudSync
        // PullService, WP-04). POS-written rows stay below it.
        public const long DashboardIdFloor = 5_000_000_000L;

        // The id is assigned explicitly as (highest id below DashboardIdFloor) + 1.
        // stock_movements.id is a plain INTEGER PRIMARY KEY, so SQLite's own choice
        // would be MAX(rowid) + 1 over the whole table: after the first pulled row
        // (id 5,000,000,000) every POS sale would land in the reserved range, collide
        // with the next pulled id, and be skipped by the cloud push (id < floor).
        // The ORDER BY id DESC LIMIT 1 form is a single rowid b-tree seek.
        public int Insert(StockMovement m)
        {
            return (int)InsertCore(m, null, null);
        }

        // Insert at a caller-chosen id (the dashboard range) with created_at and
        // changed_at set to `movedAt` ('yyyy-MM-dd HH:mm:ss' local) instead of now, so
        // GetMovementsSince places the movement at the time it happened. Returns the id.
        public long InsertWithId(StockMovement m, long id, string movedAt)
        {
            if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
            return InsertCore(m, id, movedAt);
        }

        private long InsertCore(StockMovement m, long? explicitId, string movedAt)
        {
            SqlHelper.ExecuteNonQuery(_db,
                @"INSERT INTO stock_movements (id, product_code, vendor_code, dept_code, location_code,
                  account_code, sub_code, journal_no, movement_type, doc_date, period_code,
                  qty_in, qty_out, val_in, val_out, cost_price, is_posted, changed_by, changed_at, created_at)
                  VALUES (COALESCE(@id,
                            COALESCE((SELECT id FROM stock_movements WHERE id < @floor ORDER BY id DESC LIMIT 1), 0) + 1),
                  @product, @vendor, @dept, @loc, @acc, @sub, @jnl, @type, @date, @period,
                  @qtyIn, @qtyOut, @valIn, @valOut, @cost, 0, @changedBy,
                  COALESCE(@movedAt, datetime('now','localtime')),
                  COALESCE(@movedAt, datetime('now','localtime')))",
                SqlHelper.Param("@id", explicitId.HasValue ? (object)explicitId.Value : null),
                SqlHelper.Param("@floor", DashboardIdFloor),
                SqlHelper.Param("@movedAt", movedAt),
                SqlHelper.Param("@product", m.ProductCode),
                SqlHelper.Param("@vendor", m.VendorCode ?? ""),
                SqlHelper.Param("@dept", m.DeptCode ?? ""),
                SqlHelper.Param("@loc", m.LocationCode ?? ""),
                SqlHelper.Param("@acc", m.AccountCode ?? ""),
                SqlHelper.Param("@sub", m.SubCode ?? ""),
                SqlHelper.Param("@jnl", m.JournalNo),
                SqlHelper.Param("@type", m.MovementType),
                SqlHelper.Param("@date", m.DocDate),
                SqlHelper.Param("@period", m.PeriodCode),
                SqlHelper.Param("@qtyIn", m.QtyIn),
                SqlHelper.Param("@qtyOut", m.QtyOut),
                SqlHelper.Param("@valIn", m.ValIn),
                SqlHelper.Param("@valOut", m.ValOut),
                SqlHelper.Param("@cost", m.CostPrice),
                SqlHelper.Param("@changedBy", m.ChangedBy));

            return SqlHelper.LastInsertRowId(_db);
        }

        public int GetStockOnHand(string productCode)
        {
            return SqlHelper.ExecuteScalar<int>(_db,
                "SELECT COALESCE(SUM(qty_in) - SUM(qty_out), 0) FROM stock_movements WHERE product_code = @code",
                SqlHelper.Param("@code", productCode));
        }

        public int GetStockOnHandByLocation(string productCode, string locationCode)
        {
            return SqlHelper.ExecuteScalar<int>(_db,
                @"SELECT COALESCE(SUM(qty_in) - SUM(qty_out), 0) FROM stock_movements
                  WHERE product_code = @code AND location_code = @loc",
                SqlHelper.Param("@code", productCode),
                SqlHelper.Param("@loc", locationCode));
        }

        // Stock moved after a point in time (PR-K6 opname): compares changed_at, the local
        // 'yyyy-MM-dd HH:mm:ss' the row was written. A movement in the same second as
        // `since` counts as before it.
        public (int QtyOut, int QtyIn) GetMovementsSince(string productCode, DateTime since)
        {
            using (var cmd = _db.CreateCommand())
            {
                cmd.CommandText =
                    @"SELECT COALESCE(SUM(qty_out), 0), COALESCE(SUM(qty_in), 0)
                      FROM stock_movements
                      WHERE product_code = @code AND changed_at > @since";
                cmd.Parameters.Add(SqlHelper.Param("@code", productCode));
                cmd.Parameters.Add(SqlHelper.Param("@since", since.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)));
                using (var reader = cmd.ExecuteReader())
                {
                    reader.Read();
                    return (reader.GetInt32(0), reader.GetInt32(1));
                }
            }
        }

        public List<StockMovement> GetPurchaseMovements(string productCode)
        {
            return SqlHelper.Query(_db,
                @"SELECT * FROM stock_movements
                  WHERE product_code = @code AND movement_type = 'PURCHASE' AND qty_in > 0
                  ORDER BY doc_date ASC, id ASC",
                MapMovement,
                SqlHelper.Param("@code", productCode));
        }

        public List<StockMovement> GetByProduct(string productCode, string dateFrom, string dateTo)
        {
            return SqlHelper.Query(_db,
                @"SELECT * FROM stock_movements
                  WHERE product_code = @code AND doc_date >= @from AND doc_date <= @to
                  ORDER BY doc_date ASC, id ASC",
                MapMovement,
                SqlHelper.Param("@code", productCode),
                SqlHelper.Param("@from", dateFrom),
                SqlHelper.Param("@to", dateTo));
        }

        public List<StockMovement> GetByJournal(string journalNo)
        {
            return SqlHelper.Query(_db,
                "SELECT * FROM stock_movements WHERE journal_no = @jnl ORDER BY id",
                MapMovement,
                SqlHelper.Param("@jnl", journalNo));
        }

        private static StockMovement MapMovement(SqliteDataReader reader)
        {
            return new StockMovement
            {
                Id = SqlHelper.GetLong(reader, "id"),
                ProductCode = SqlHelper.GetString(reader, "product_code"),
                VendorCode = SqlHelper.GetString(reader, "vendor_code"),
                DeptCode = SqlHelper.GetString(reader, "dept_code"),
                LocationCode = SqlHelper.GetString(reader, "location_code"),
                AccountCode = SqlHelper.GetString(reader, "account_code"),
                SubCode = SqlHelper.GetString(reader, "sub_code"),
                JournalNo = SqlHelper.GetString(reader, "journal_no"),
                MovementType = SqlHelper.GetString(reader, "movement_type"),
                DocDate = SqlHelper.GetString(reader, "doc_date"),
                PeriodCode = SqlHelper.GetString(reader, "period_code"),
                QtyIn = SqlHelper.GetInt(reader, "qty_in"),
                QtyOut = SqlHelper.GetInt(reader, "qty_out"),
                ValIn = SqlHelper.GetLong(reader, "val_in"),
                ValOut = SqlHelper.GetLong(reader, "val_out"),
                CostPrice = SqlHelper.GetLong(reader, "cost_price"),
                IsPosted = SqlHelper.GetInt(reader, "is_posted"),
                ChangedBy = SqlHelper.GetInt(reader, "changed_by"),
                ChangedAt = SqlHelper.GetString(reader, "changed_at")
            };
        }
    }
}
