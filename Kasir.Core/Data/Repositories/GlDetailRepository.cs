using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Kasir.Models;

namespace Kasir.Data.Repositories
{
    public class GlDetailRepository
    {
        private readonly SqliteConnection _db;

        public GlDetailRepository(SqliteConnection db)
        {
            _db = db;
        }

        public int Insert(GlDetail detail)
        {
            SqlHelper.ExecuteNonQuery(_db,
                @"INSERT INTO gl_details (account_code, sub_code, product_code, alt_sub,
                  alias, journal_no, sales_code, remark, voucher_no, ref, doc_date,
                  debit, credit, qty_in, qty_out, period_code)
                  VALUES (@acc, @sub, @product, @altSub, @alias, @jnl, @sales, @remark,
                  @voucher, @ref, @date, @debit, @credit, @qtyIn, @qtyOut, @period)",
                SqlHelper.Param("@acc", detail.AccountCode),
                SqlHelper.Param("@sub", detail.SubCode ?? ""),
                SqlHelper.Param("@product", detail.ProductCode ?? ""),
                SqlHelper.Param("@altSub", detail.AltSub ?? ""),
                SqlHelper.Param("@alias", detail.Alias ?? ""),
                SqlHelper.Param("@jnl", detail.JournalNo),
                SqlHelper.Param("@sales", detail.SalesCode ?? ""),
                SqlHelper.Param("@remark", detail.Remark ?? ""),
                SqlHelper.Param("@voucher", detail.VoucherNo ?? ""),
                SqlHelper.Param("@ref", detail.Ref ?? ""),
                SqlHelper.Param("@date", detail.DocDate),
                SqlHelper.Param("@debit", detail.Debit),
                SqlHelper.Param("@credit", detail.Credit),
                SqlHelper.Param("@qtyIn", detail.QtyIn),
                SqlHelper.Param("@qtyOut", detail.QtyOut),
                SqlHelper.Param("@period", detail.PeriodCode));

            return (int)SqlHelper.LastInsertRowId(_db);
        }

        public List<GlDetail> GetByJournalNo(string journalNo)
        {
            return SqlHelper.Query(_db,
                "SELECT * FROM gl_details WHERE journal_no = @jnl ORDER BY id",
                MapGlDetail, SqlHelper.Param("@jnl", journalNo));
        }

        public List<GlDetail> GetByAccountAndPeriod(string accountCode, string periodCode)
        {
            return SqlHelper.Query(_db,
                @"SELECT * FROM gl_details
                  WHERE account_code = @acc AND period_code = @period
                  ORDER BY doc_date, id",
                MapGlDetail,
                SqlHelper.Param("@acc", accountCode),
                SqlHelper.Param("@period", periodCode));
        }

        public List<GlDetail> GetByPeriod(string periodCode)
        {
            return SqlHelper.Query(_db,
                "SELECT * FROM gl_details WHERE period_code = @period ORDER BY doc_date, id",
                MapGlDetail, SqlHelper.Param("@period", periodCode));
        }

        public long GetDebitTotal(string periodCode)
        {
            return SqlHelper.ExecuteScalar<long>(_db,
                "SELECT COALESCE(SUM(debit), 0) FROM gl_details WHERE period_code = @period",
                SqlHelper.Param("@period", periodCode));
        }

        public long GetCreditTotal(string periodCode)
        {
            return SqlHelper.ExecuteScalar<long>(_db,
                "SELECT COALESCE(SUM(credit), 0) FROM gl_details WHERE period_code = @period",
                SqlHelper.Param("@period", periodCode));
        }

        public long GetDebitTotalForAccount(string periodCode, string accountCode)
        {
            return SqlHelper.ExecuteScalar<long>(_db,
                "SELECT COALESCE(SUM(debit), 0) FROM gl_details WHERE period_code = @period AND account_code = @acc",
                SqlHelper.Param("@period", periodCode),
                SqlHelper.Param("@acc", accountCode));
        }

        public long GetCreditTotalForAccount(string periodCode, string accountCode)
        {
            return SqlHelper.ExecuteScalar<long>(_db,
                "SELECT COALESCE(SUM(credit), 0) FROM gl_details WHERE period_code = @period AND account_code = @acc",
                SqlHelper.Param("@period", periodCode),
                SqlHelper.Param("@acc", accountCode));
        }

        private static GlDetail MapGlDetail(SqliteDataReader r)
        {
            return new GlDetail
            {
                Id = SqlHelper.GetInt(r, "id"),
                AccountCode = SqlHelper.GetString(r, "account_code"),
                SubCode = SqlHelper.GetString(r, "sub_code"),
                ProductCode = SqlHelper.GetString(r, "product_code"),
                AltSub = SqlHelper.GetString(r, "alt_sub"),
                Alias = SqlHelper.GetString(r, "alias"),
                JournalNo = SqlHelper.GetString(r, "journal_no"),
                SalesCode = SqlHelper.GetString(r, "sales_code"),
                Remark = SqlHelper.GetString(r, "remark"),
                VoucherNo = SqlHelper.GetString(r, "voucher_no"),
                Ref = SqlHelper.GetString(r, "ref"),
                DocDate = SqlHelper.GetString(r, "doc_date"),
                Debit = SqlHelper.GetLong(r, "debit"),
                Credit = SqlHelper.GetLong(r, "credit"),
                QtyIn = SqlHelper.GetLong(r, "qty_in"),
                QtyOut = SqlHelper.GetLong(r, "qty_out"),
                PeriodCode = SqlHelper.GetString(r, "period_code")
            };
        }
    }
}
