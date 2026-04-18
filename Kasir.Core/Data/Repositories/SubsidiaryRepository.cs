using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Kasir.Models;

namespace Kasir.Data.Repositories
{
    public class SubsidiaryRepository
    {
        private readonly SqliteConnection _db;

        public SubsidiaryRepository(SqliteConnection db)
        {
            _db = db;
        }

        // Lightweight lookup (Phase 2 backward compat)
        public List<SubsidiaryLookup> GetVendors()
        {
            return SqlHelper.Query(_db,
                "SELECT sub_code, name FROM subsidiaries WHERE group_code = '1' ORDER BY name",
                reader => new SubsidiaryLookup
                {
                    SubCode = SqlHelper.GetString(reader, "sub_code"),
                    Name = SqlHelper.GetString(reader, "name")
                });
        }

        public SubsidiaryLookup GetLookupByCode(string subCode)
        {
            return SqlHelper.QuerySingle(_db,
                "SELECT sub_code, name FROM subsidiaries WHERE sub_code = @code",
                reader => new SubsidiaryLookup
                {
                    SubCode = SqlHelper.GetString(reader, "sub_code"),
                    Name = SqlHelper.GetString(reader, "name")
                },
                SqlHelper.Param("@code", subCode));
        }

        // Full CRUD (Phase 3)
        public Subsidiary GetByCode(string subCode)
        {
            return SqlHelper.QuerySingle(_db,
                "SELECT * FROM subsidiaries WHERE sub_code = @code",
                MapSubsidiary,
                SqlHelper.Param("@code", subCode));
        }

        public List<Subsidiary> GetAllByGroup(string groupCode, int limit, int offset)
        {
            return SqlHelper.Query(_db,
                @"SELECT * FROM subsidiaries WHERE group_code = @group
                  ORDER BY name LIMIT @limit OFFSET @offset",
                MapSubsidiary,
                SqlHelper.Param("@group", groupCode),
                SqlHelper.Param("@limit", limit),
                SqlHelper.Param("@offset", offset));
        }

        public List<Subsidiary> SearchByName(string query, string groupCode, int limit)
        {
            return SqlHelper.Query(_db,
                @"SELECT * FROM subsidiaries
                  WHERE group_code = @group AND (name LIKE @q OR sub_code LIKE @q)
                  ORDER BY name LIMIT @limit",
                MapSubsidiary,
                SqlHelper.Param("@group", groupCode),
                SqlHelper.Param("@q", "%" + query + "%"),
                SqlHelper.Param("@limit", limit));
        }

        public int Insert(Subsidiary sub)
        {
            SqlHelper.ExecuteNonQuery(_db,
                @"INSERT INTO subsidiaries (sub_code, name, account_code, contact_person,
                  credit_limit, address, city, country, npwp, phone, fax, group_code,
                  disc_pct, status, bank_name, bank_account_no, changed_by, changed_at)
                  VALUES (@code, @name, @acc, @contact, @limit, @addr, @city, @country,
                  @npwp, @phone, @fax, @group, @disc, @status, @bank, @bankNo,
                  @changedBy, datetime('now','localtime'))",
                SqlHelper.Param("@code", sub.SubCode),
                SqlHelper.Param("@name", sub.Name),
                SqlHelper.Param("@acc", sub.AccountCode),
                SqlHelper.Param("@contact", sub.ContactPerson),
                SqlHelper.Param("@limit", sub.CreditLimit),
                SqlHelper.Param("@addr", sub.Address),
                SqlHelper.Param("@city", sub.City),
                SqlHelper.Param("@country", sub.Country),
                SqlHelper.Param("@npwp", sub.Npwp),
                SqlHelper.Param("@phone", sub.Phone),
                SqlHelper.Param("@fax", sub.Fax),
                SqlHelper.Param("@group", sub.GroupCode ?? "1"),
                SqlHelper.Param("@disc", sub.DiscPct),
                SqlHelper.Param("@status", sub.Status ?? "A"),
                SqlHelper.Param("@bank", sub.BankName),
                SqlHelper.Param("@bankNo", sub.BankAccountNo),
                SqlHelper.Param("@changedBy", sub.ChangedBy));
            return (int)SqlHelper.LastInsertRowId(_db);
        }

        public void Update(Subsidiary sub)
        {
            SqlHelper.ExecuteNonQuery(_db,
                @"UPDATE subsidiaries SET name = @name, contact_person = @contact,
                  address = @addr, city = @city, phone = @phone, disc_pct = @disc,
                  status = @status, changed_by = @changedBy, changed_at = datetime('now','localtime')
                  WHERE id = @id",
                SqlHelper.Param("@name", sub.Name),
                SqlHelper.Param("@contact", sub.ContactPerson),
                SqlHelper.Param("@addr", sub.Address),
                SqlHelper.Param("@city", sub.City),
                SqlHelper.Param("@phone", sub.Phone),
                SqlHelper.Param("@disc", sub.DiscPct),
                SqlHelper.Param("@status", sub.Status),
                SqlHelper.Param("@changedBy", sub.ChangedBy),
                SqlHelper.Param("@id", sub.Id));
        }

        private static Subsidiary MapSubsidiary(SqliteDataReader reader)
        {
            return new Subsidiary
            {
                Id = SqlHelper.GetInt(reader, "id"),
                SubCode = SqlHelper.GetString(reader, "sub_code"),
                Name = SqlHelper.GetString(reader, "name"),
                AccountCode = SqlHelper.GetString(reader, "account_code"),
                ContactPerson = SqlHelper.GetString(reader, "contact_person"),
                CreditLimit = SqlHelper.GetLong(reader, "credit_limit"),
                Address = SqlHelper.GetString(reader, "address"),
                City = SqlHelper.GetString(reader, "city"),
                Country = SqlHelper.GetString(reader, "country"),
                Npwp = SqlHelper.GetString(reader, "npwp"),
                Phone = SqlHelper.GetString(reader, "phone"),
                Fax = SqlHelper.GetString(reader, "fax"),
                GroupCode = SqlHelper.GetString(reader, "group_code"),
                DiscPct = SqlHelper.GetInt(reader, "disc_pct"),
                Status = SqlHelper.GetString(reader, "status"),
                BankName = SqlHelper.GetString(reader, "bank_name"),
                BankAccountNo = SqlHelper.GetString(reader, "bank_account_no"),
                ChangedBy = SqlHelper.GetInt(reader, "changed_by"),
                ChangedAt = SqlHelper.GetString(reader, "changed_at")
            };
        }
    }

    public class SubsidiaryLookup
    {
        public string SubCode { get; set; }
        public string Name { get; set; }

        public override string ToString()
        {
            return string.Format("{0} — {1}", SubCode, Name);
        }
    }
}
