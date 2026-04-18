using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Kasir.Models;

namespace Kasir.Data.Repositories
{
    public class ConfigRepository
    {
        private readonly SqliteConnection _db;

        public ConfigRepository(SqliteConnection db)
        {
            _db = db;
        }

        public string Get(string key)
        {
            return SqlHelper.ExecuteScalar<string>(_db,
                "SELECT value FROM config WHERE key = @key",
                SqlHelper.Param("@key", key));
        }

        public void Set(string key, string value)
        {
            int updated = SqlHelper.ExecuteNonQuery(_db,
                "UPDATE config SET value = @value WHERE key = @key",
                SqlHelper.Param("@value", value),
                SqlHelper.Param("@key", key));

            if (updated == 0)
            {
                SqlHelper.ExecuteNonQuery(_db,
                    "INSERT INTO config (key, value) VALUES (@key, @value)",
                    SqlHelper.Param("@key", key),
                    SqlHelper.Param("@value", value));
            }
        }

        public List<ConfigEntry> GetAll()
        {
            return SqlHelper.Query(_db,
                "SELECT * FROM config ORDER BY key",
                MapConfig);
        }

        private static ConfigEntry MapConfig(SqliteDataReader reader)
        {
            return new ConfigEntry
            {
                Id = SqlHelper.GetInt(reader, "id"),
                Key = SqlHelper.GetString(reader, "key"),
                Value = SqlHelper.GetString(reader, "value"),
                Description = SqlHelper.GetString(reader, "description")
            };
        }
    }
}
