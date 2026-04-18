using System.Collections.Generic;
using System.Data.SQLite;
using Kasir.Models;

namespace Kasir.Data.Repositories
{
    public class FiscalPeriodRepository
    {
        private readonly SQLiteConnection _db;

        public FiscalPeriodRepository(SQLiteConnection db)
        {
            _db = db;
        }

        public void EnsurePeriod(string periodCode, int year, int month)
        {
            SqlHelper.ExecuteNonQuery(_db,
                @"INSERT OR IGNORE INTO fiscal_periods (period_code, year, month, status,
                  opened_at) VALUES (@code, @year, @month, 'O', datetime('now','localtime'))",
                SqlHelper.Param("@code", periodCode),
                SqlHelper.Param("@year", year),
                SqlHelper.Param("@month", month));
        }

        public FiscalPeriod GetByCode(string periodCode)
        {
            return SqlHelper.QuerySingle(_db,
                "SELECT * FROM fiscal_periods WHERE period_code = @code",
                MapPeriod, SqlHelper.Param("@code", periodCode));
        }

        public FiscalPeriod GetCurrentOpen()
        {
            return SqlHelper.QuerySingle(_db,
                "SELECT * FROM fiscal_periods WHERE status = 'O' ORDER BY period_code DESC LIMIT 1",
                MapPeriod);
        }

        public List<FiscalPeriod> GetAll()
        {
            return SqlHelper.Query(_db,
                "SELECT * FROM fiscal_periods ORDER BY period_code",
                MapPeriod);
        }

        public void ClosePeriod(string periodCode)
        {
            SqlHelper.ExecuteNonQuery(_db,
                @"UPDATE fiscal_periods SET status = 'C',
                  closed_at = datetime('now','localtime')
                  WHERE period_code = @code",
                SqlHelper.Param("@code", periodCode));
        }

        public bool IsClosed(string periodCode)
        {
            var period = GetByCode(periodCode);
            return period != null && period.Status == "C";
        }

        private static FiscalPeriod MapPeriod(SQLiteDataReader r)
        {
            return new FiscalPeriod
            {
                Id = SqlHelper.GetInt(r, "id"),
                PeriodCode = SqlHelper.GetString(r, "period_code"),
                Year = SqlHelper.GetInt(r, "year"),
                Month = SqlHelper.GetInt(r, "month"),
                Status = SqlHelper.GetString(r, "status"),
                OpenedAt = SqlHelper.GetString(r, "opened_at"),
                ClosedAt = SqlHelper.GetString(r, "closed_at")
            };
        }
    }
}
