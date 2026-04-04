using System.Collections.Generic;
using System.Data.SQLite;
using Kasir.Models;

namespace Kasir.Data.Repositories
{
    public class CreditCardRepository
    {
        private readonly SQLiteConnection _db;

        public CreditCardRepository(SQLiteConnection db)
        {
            _db = db;
        }

        public List<CreditCard> GetAll()
        {
            return SqlHelper.Query(_db,
                "SELECT * FROM credit_cards ORDER BY name",
                MapCard);
        }

        public CreditCard GetByCode(string cardCode)
        {
            return SqlHelper.QuerySingle(_db,
                "SELECT * FROM credit_cards WHERE card_code = @code",
                MapCard,
                SqlHelper.Param("@code", cardCode));
        }

        public int Insert(CreditCard card)
        {
            SqlHelper.ExecuteNonQuery(_db,
                @"INSERT INTO credit_cards (card_code, name, account_code, fee_pct, min_value, changed_by, changed_at)
                  VALUES (@code, @name, @acc, @fee, @min, @changedBy, datetime('now','localtime'))",
                SqlHelper.Param("@code", card.CardCode),
                SqlHelper.Param("@name", card.Name),
                SqlHelper.Param("@acc", card.AccountCode),
                SqlHelper.Param("@fee", card.FeePct),
                SqlHelper.Param("@min", card.MinValue),
                SqlHelper.Param("@changedBy", card.ChangedBy));

            return (int)_db.LastInsertRowId;
        }

        public void Update(CreditCard card)
        {
            SqlHelper.ExecuteNonQuery(_db,
                @"UPDATE credit_cards SET name = @name, account_code = @acc, fee_pct = @fee,
                  min_value = @min, changed_by = @changedBy, changed_at = datetime('now','localtime')
                  WHERE id = @id",
                SqlHelper.Param("@name", card.Name),
                SqlHelper.Param("@acc", card.AccountCode),
                SqlHelper.Param("@fee", card.FeePct),
                SqlHelper.Param("@min", card.MinValue),
                SqlHelper.Param("@changedBy", card.ChangedBy),
                SqlHelper.Param("@id", card.Id));
        }

        public void Delete(int id)
        {
            SqlHelper.ExecuteNonQuery(_db,
                "DELETE FROM credit_cards WHERE id = @id",
                SqlHelper.Param("@id", id));
        }

        private static CreditCard MapCard(SQLiteDataReader reader)
        {
            return new CreditCard
            {
                Id = SqlHelper.GetInt(reader, "id"),
                CardCode = SqlHelper.GetString(reader, "card_code"),
                Name = SqlHelper.GetString(reader, "name"),
                AccountCode = SqlHelper.GetString(reader, "account_code"),
                FeePct = SqlHelper.GetInt(reader, "fee_pct"),
                MinValue = SqlHelper.GetInt(reader, "min_value"),
                ChangedBy = SqlHelper.GetInt(reader, "changed_by"),
                ChangedAt = SqlHelper.GetString(reader, "changed_at")
            };
        }
    }
}
