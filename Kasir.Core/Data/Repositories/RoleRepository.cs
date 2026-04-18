using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Kasir.Models;

namespace Kasir.Data.Repositories
{
    public class RoleRepository
    {
        private readonly SqliteConnection _db;

        public RoleRepository(SqliteConnection db)
        {
            _db = db;
        }

        public Role GetById(int id)
        {
            return SqlHelper.QuerySingle(_db,
                "SELECT * FROM roles WHERE id = @id",
                MapRole,
                SqlHelper.Param("@id", id));
        }

        public Role GetByName(string name)
        {
            return SqlHelper.QuerySingle(_db,
                "SELECT * FROM roles WHERE name = @name",
                MapRole,
                SqlHelper.Param("@name", name));
        }

        public List<Role> GetAll()
        {
            return SqlHelper.Query(_db,
                "SELECT * FROM roles ORDER BY id",
                MapRole);
        }

        private static Role MapRole(SqliteDataReader reader)
        {
            return new Role
            {
                Id = SqlHelper.GetInt(reader, "id"),
                Name = SqlHelper.GetString(reader, "name"),
                Permissions = SqlHelper.GetString(reader, "permissions")
            };
        }
    }
}
