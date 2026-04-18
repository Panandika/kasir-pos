using System.Collections.Generic;
using System.Data.SQLite;
using Kasir.Models;

namespace Kasir.Data.Repositories
{
    public class GiroRepository
    {
        private readonly SQLiteConnection _db;

        public GiroRepository(SQLiteConnection db)
        {
            _db = db;
        }

        public int Insert(GiroEntry giro)
        {
            SqlHelper.ExecuteNonQuery(_db,
                @"INSERT INTO giro_register (account_code, sub_code, giro_no, giro_date,
                  doc_date, journal_no, value, remark, direction, status, control,
                  period_code, changed_by, changed_at)
                  VALUES (@acc, @sub, @giroNo, @giroDate, @docDate, @jnl, @val, @remark,
                  @dir, 'O', @control, @period, @changedBy, datetime('now','localtime'))",
                SqlHelper.Param("@acc", giro.AccountCode),
                SqlHelper.Param("@sub", giro.SubCode),
                SqlHelper.Param("@giroNo", giro.GiroNo),
                SqlHelper.Param("@giroDate", giro.GiroDate ?? ""),
                SqlHelper.Param("@docDate", giro.DocDate),
                SqlHelper.Param("@jnl", giro.JournalNo ?? ""),
                SqlHelper.Param("@val", giro.Value),
                SqlHelper.Param("@remark", giro.Remark ?? ""),
                SqlHelper.Param("@dir", giro.Direction),
                SqlHelper.Param("@control", giro.Control),
                SqlHelper.Param("@period", giro.PeriodCode),
                SqlHelper.Param("@changedBy", giro.ChangedBy));

            return (int)_db.LastInsertRowId;
        }

        public GiroEntry GetById(int id)
        {
            return SqlHelper.QuerySingle(_db,
                "SELECT * FROM giro_register WHERE id = @id",
                MapGiro, SqlHelper.Param("@id", id));
        }

        public List<GiroEntry> GetOpenByVendor(string vendorCode)
        {
            return SqlHelper.Query(_db,
                @"SELECT * FROM giro_register
                  WHERE sub_code = @sub AND status = 'O' AND control != 3
                  ORDER BY giro_date",
                MapGiro, SqlHelper.Param("@sub", vendorCode));
        }

        public void ClearGiro(int id, int approvedBy)
        {
            SqlHelper.ExecuteNonQuery(_db,
                @"UPDATE giro_register SET status = 'C', approved_by = @approved,
                  changed_at = datetime('now','localtime')
                  WHERE id = @id",
                SqlHelper.Param("@id", id),
                SqlHelper.Param("@approved", approvedBy));
        }

        public void RejectGiro(int id)
        {
            SqlHelper.ExecuteNonQuery(_db,
                @"UPDATE giro_register SET control = 3,
                  changed_at = datetime('now','localtime')
                  WHERE id = @id",
                SqlHelper.Param("@id", id));
        }

        private static GiroEntry MapGiro(SQLiteDataReader r)
        {
            return new GiroEntry
            {
                Id = SqlHelper.GetInt(r, "id"),
                AccountCode = SqlHelper.GetString(r, "account_code"),
                SubCode = SqlHelper.GetString(r, "sub_code"),
                GiroNo = SqlHelper.GetString(r, "giro_no"),
                GiroDate = SqlHelper.GetString(r, "giro_date"),
                DocDate = SqlHelper.GetString(r, "doc_date"),
                JournalNo = SqlHelper.GetString(r, "journal_no"),
                Value = SqlHelper.GetLong(r, "value"),
                Remark = SqlHelper.GetString(r, "remark"),
                Direction = SqlHelper.GetString(r, "direction"),
                Status = SqlHelper.GetString(r, "status"),
                ApprovedBy = SqlHelper.GetInt(r, "approved_by"),
                Control = SqlHelper.GetInt(r, "control"),
                PrintCount = SqlHelper.GetInt(r, "print_count"),
                PeriodCode = SqlHelper.GetString(r, "period_code"),
                ChangedBy = SqlHelper.GetInt(r, "changed_by"),
                ChangedAt = SqlHelper.GetString(r, "changed_at")
            };
        }
    }
}
