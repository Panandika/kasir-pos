using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Kasir.Models;

namespace Kasir.Data.Repositories
{
    public class DepartmentRepository
    {
        private readonly SqliteConnection _db;

        public DepartmentRepository(SqliteConnection db)
        {
            _db = db;
        }

        public Department GetByCode(string deptCode)
        {
            return SqlHelper.QuerySingle(_db,
                "SELECT * FROM departments WHERE dept_code = @code",
                MapDepartment,
                SqlHelper.Param("@code", deptCode));
        }

        public List<Department> GetAll()
        {
            return SqlHelper.Query(_db,
                "SELECT * FROM departments ORDER BY dept_code",
                MapDepartment);
        }

        public int Insert(Department dept)
        {
            SqlHelper.ExecuteNonQuery(_db,
                @"INSERT INTO departments (dept_code, name, changed_by, changed_at)
                  VALUES (@code, @name, @changedBy, datetime('now','localtime'))",
                SqlHelper.Param("@code", dept.DeptCode),
                SqlHelper.Param("@name", dept.Name),
                SqlHelper.Param("@changedBy", dept.ChangedBy));

            return (int)SqlHelper.LastInsertRowId(_db);
        }

        public void Update(Department dept)
        {
            SqlHelper.ExecuteNonQuery(_db,
                @"UPDATE departments SET name = @name, changed_by = @changedBy,
                  changed_at = datetime('now','localtime')
                  WHERE id = @id",
                SqlHelper.Param("@name", dept.Name),
                SqlHelper.Param("@changedBy", dept.ChangedBy),
                SqlHelper.Param("@id", dept.Id));
        }

        public void Delete(int id)
        {
            SqlHelper.ExecuteNonQuery(_db,
                "DELETE FROM departments WHERE id = @id",
                SqlHelper.Param("@id", id));
        }

        private static Department MapDepartment(SqliteDataReader reader)
        {
            return new Department
            {
                Id = SqlHelper.GetInt(reader, "id"),
                DeptCode = SqlHelper.GetString(reader, "dept_code"),
                Name = SqlHelper.GetString(reader, "name"),
                ChangedBy = SqlHelper.GetInt(reader, "changed_by"),
                ChangedAt = SqlHelper.GetString(reader, "changed_at")
            };
        }
    }
}
