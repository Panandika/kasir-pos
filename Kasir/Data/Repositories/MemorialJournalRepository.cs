using System.Collections.Generic;
using System.Data.SQLite;
using Kasir.Models;

namespace Kasir.Data.Repositories
{
    public class MemorialJournalRepository
    {
        private readonly SQLiteConnection _db;

        public MemorialJournalRepository(SQLiteConnection db)
        {
            _db = db;
        }

        public MemorialJournal GetByJournalNo(string journalNo)
        {
            return SqlHelper.QuerySingle(_db,
                "SELECT * FROM memorial_journals WHERE journal_no = @jnl",
                MapJournal, SqlHelper.Param("@jnl", journalNo));
        }

        public List<MemorialJournal> GetByPeriod(string periodCode)
        {
            return SqlHelper.Query(_db,
                @"SELECT * FROM memorial_journals
                  WHERE period_code = @period AND control != 3
                  ORDER BY journal_no",
                MapJournal, SqlHelper.Param("@period", periodCode));
        }

        public List<MemorialJournalLine> GetLines(string journalNo)
        {
            return SqlHelper.Query(_db,
                "SELECT * FROM memorial_journal_lines WHERE journal_no = @jnl ORDER BY id",
                MapLine, SqlHelper.Param("@jnl", journalNo));
        }

        private static MemorialJournal MapJournal(SQLiteDataReader r)
        {
            return new MemorialJournal
            {
                Id = SqlHelper.GetInt(r, "id"),
                DocType = SqlHelper.GetString(r, "doc_type"),
                JournalNo = SqlHelper.GetString(r, "journal_no"),
                DocDate = SqlHelper.GetString(r, "doc_date"),
                Ref = SqlHelper.GetString(r, "ref"),
                RefNo = SqlHelper.GetString(r, "ref_no"),
                Remark = SqlHelper.GetString(r, "remark"),
                GroupCode = SqlHelper.GetString(r, "group_code"),
                Control = SqlHelper.GetInt(r, "control"),
                PrintCount = SqlHelper.GetInt(r, "print_count"),
                PeriodCode = SqlHelper.GetString(r, "period_code"),
                RegisterId = SqlHelper.GetString(r, "register_id"),
                ChangedBy = SqlHelper.GetInt(r, "changed_by"),
                ChangedAt = SqlHelper.GetString(r, "changed_at")
            };
        }

        private static MemorialJournalLine MapLine(SQLiteDataReader r)
        {
            return new MemorialJournalLine
            {
                Id = SqlHelper.GetInt(r, "id"),
                JournalNo = SqlHelper.GetString(r, "journal_no"),
                SubCode = SqlHelper.GetString(r, "sub_code"),
                AccountCode = SqlHelper.GetString(r, "account_code"),
                ProductCode = SqlHelper.GetString(r, "product_code"),
                AltSub = SqlHelper.GetString(r, "alt_sub"),
                Remark = SqlHelper.GetString(r, "remark"),
                Name = SqlHelper.GetString(r, "name"),
                Quantity = SqlHelper.GetInt(r, "quantity"),
                UnitPrice = SqlHelper.GetInt(r, "unit_price"),
                Direction = SqlHelper.GetString(r, "direction"),
                Value = SqlHelper.GetLong(r, "value"),
                GroupCode = SqlHelper.GetString(r, "group_code"),
                Roll = SqlHelper.GetLong(r, "roll"),
                StickerCount = SqlHelper.GetInt(r, "sticker_count"),
                MaxSticker = SqlHelper.GetInt(r, "max_sticker")
            };
        }
    }
}
