using System.Collections.Generic;
using System.Data.SQLite;
using Kasir.Models;

namespace Kasir.Data.Repositories
{
    public class LocationRepository
    {
        private readonly SQLiteConnection _db;

        public LocationRepository(SQLiteConnection db)
        {
            _db = db;
        }

        public List<Location> GetAll()
        {
            return SqlHelper.Query(_db,
                "SELECT * FROM locations ORDER BY location_code",
                MapLocation);
        }

        public Location GetByCode(string locationCode)
        {
            return SqlHelper.QuerySingle(_db,
                "SELECT * FROM locations WHERE location_code = @code",
                MapLocation,
                SqlHelper.Param("@code", locationCode));
        }

        public int Insert(Location loc)
        {
            SqlHelper.ExecuteNonQuery(_db,
                @"INSERT INTO locations (location_code, name, remark, changed_by, changed_at)
                  VALUES (@code, @name, @remark, @changedBy, datetime('now','localtime'))",
                SqlHelper.Param("@code", loc.LocationCode),
                SqlHelper.Param("@name", loc.Name),
                SqlHelper.Param("@remark", loc.Remark),
                SqlHelper.Param("@changedBy", loc.ChangedBy));
            return (int)_db.LastInsertRowId;
        }

        private static Location MapLocation(SQLiteDataReader reader)
        {
            return new Location
            {
                Id = SqlHelper.GetInt(reader, "id"),
                LocationCode = SqlHelper.GetString(reader, "location_code"),
                Name = SqlHelper.GetString(reader, "name"),
                Remark = SqlHelper.GetString(reader, "remark"),
                ChangedBy = SqlHelper.GetInt(reader, "changed_by"),
                ChangedAt = SqlHelper.GetString(reader, "changed_at")
            };
        }
    }
}
