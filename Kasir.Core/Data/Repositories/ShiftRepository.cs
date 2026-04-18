using System.Collections.Generic;
using System.Data.SQLite;
using Kasir.Models;

namespace Kasir.Data.Repositories
{
    public class ShiftRepository
    {
        private readonly SQLiteConnection _db;

        public ShiftRepository(SQLiteConnection db)
        {
            _db = db;
        }

        public int OpenShift(Shift shift)
        {
            SqlHelper.ExecuteNonQuery(_db,
                @"INSERT INTO shifts (register_id, shift_number, cashier_id, opened_at, opening_cash, status)
                  VALUES (@reg, @shift, @cashier, @opened, @cash, 'O')",
                SqlHelper.Param("@reg", shift.RegisterId),
                SqlHelper.Param("@shift", shift.ShiftNumber),
                SqlHelper.Param("@cashier", shift.CashierId),
                SqlHelper.Param("@opened", shift.OpenedAt),
                SqlHelper.Param("@cash", shift.OpeningCash));

            return (int)_db.LastInsertRowId;
        }

        public void CloseShift(int id, long closingCash, long expectedCash)
        {
            SqlHelper.ExecuteNonQuery(_db,
                @"UPDATE shifts SET status = 'C', closed_at = datetime('now','localtime'),
                  closing_cash = @closing, expected_cash = @expected
                  WHERE id = @id",
                SqlHelper.Param("@closing", closingCash),
                SqlHelper.Param("@expected", expectedCash),
                SqlHelper.Param("@id", id));
        }

        public Shift GetOpenShift(string registerId)
        {
            return SqlHelper.QuerySingle(_db,
                "SELECT * FROM shifts WHERE register_id = @reg AND status = 'O' ORDER BY id DESC LIMIT 1",
                MapShift,
                SqlHelper.Param("@reg", registerId));
        }

        public Shift GetById(int id)
        {
            return SqlHelper.QuerySingle(_db,
                "SELECT * FROM shifts WHERE id = @id",
                MapShift,
                SqlHelper.Param("@id", id));
        }

        public List<Shift> GetByDateRange(string dateFrom, string dateTo)
        {
            return SqlHelper.Query(_db,
                @"SELECT * FROM shifts
                  WHERE opened_at >= @from AND opened_at <= @to
                  ORDER BY opened_at DESC",
                MapShift,
                SqlHelper.Param("@from", dateFrom),
                SqlHelper.Param("@to", dateTo + " 23:59:59"));
        }

        private static Shift MapShift(SQLiteDataReader reader)
        {
            return new Shift
            {
                Id = SqlHelper.GetInt(reader, "id"),
                RegisterId = SqlHelper.GetString(reader, "register_id"),
                ShiftNumber = SqlHelper.GetString(reader, "shift_number"),
                CashierId = SqlHelper.GetInt(reader, "cashier_id"),
                OpenedAt = SqlHelper.GetString(reader, "opened_at"),
                ClosedAt = SqlHelper.GetString(reader, "closed_at"),
                OpeningCash = SqlHelper.GetLong(reader, "opening_cash"),
                ClosingCash = SqlHelper.GetLong(reader, "closing_cash"),
                ExpectedCash = SqlHelper.GetLong(reader, "expected_cash"),
                Status = SqlHelper.GetString(reader, "status")
            };
        }
    }
}
