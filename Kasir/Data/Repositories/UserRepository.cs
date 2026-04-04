using System.Collections.Generic;
using System.Data.SQLite;
using Kasir.Models;

namespace Kasir.Data.Repositories
{
    public class UserRepository
    {
        private readonly SQLiteConnection _db;

        public UserRepository(SQLiteConnection db)
        {
            _db = db;
        }

        public User GetByUsername(string username)
        {
            return SqlHelper.QuerySingle(_db,
                "SELECT * FROM users WHERE username = @username",
                MapUser,
                SqlHelper.Param("@username", username));
        }

        public User GetById(int id)
        {
            return SqlHelper.QuerySingle(_db,
                "SELECT * FROM users WHERE id = @id",
                MapUser,
                SqlHelper.Param("@id", id));
        }

        public List<User> GetAll()
        {
            return SqlHelper.Query(_db,
                "SELECT * FROM users ORDER BY username",
                MapUser);
        }

        public int Insert(User user)
        {
            SqlHelper.ExecuteNonQuery(_db,
                @"INSERT INTO users (username, password_hash, password_salt, display_name, alias, role_id, is_active)
                  VALUES (@username, @hash, @salt, @display, @alias, @role, @active)",
                SqlHelper.Param("@username", user.Username),
                SqlHelper.Param("@hash", user.PasswordHash),
                SqlHelper.Param("@salt", user.PasswordSalt),
                SqlHelper.Param("@display", user.DisplayName),
                SqlHelper.Param("@alias", user.Alias),
                SqlHelper.Param("@role", user.RoleId),
                SqlHelper.Param("@active", user.IsActive));

            return (int)_db.LastInsertRowId;
        }

        public void Update(User user)
        {
            SqlHelper.ExecuteNonQuery(_db,
                @"UPDATE users SET display_name = @display, alias = @alias,
                  role_id = @role, is_active = @active,
                  updated_at = datetime('now','localtime')
                  WHERE id = @id",
                SqlHelper.Param("@display", user.DisplayName),
                SqlHelper.Param("@alias", user.Alias),
                SqlHelper.Param("@role", user.RoleId),
                SqlHelper.Param("@active", user.IsActive),
                SqlHelper.Param("@id", user.Id));
        }

        public void UpdatePassword(int userId, string passwordHash, string passwordSalt)
        {
            SqlHelper.ExecuteNonQuery(_db,
                @"UPDATE users SET password_hash = @hash, password_salt = @salt,
                  updated_at = datetime('now','localtime')
                  WHERE id = @id",
                SqlHelper.Param("@hash", passwordHash),
                SqlHelper.Param("@salt", passwordSalt),
                SqlHelper.Param("@id", userId));
        }

        public void Delete(int userId)
        {
            SqlHelper.ExecuteNonQuery(_db,
                "DELETE FROM users WHERE id = @id",
                SqlHelper.Param("@id", userId));
        }

        private static User MapUser(SQLiteDataReader reader)
        {
            return new User
            {
                Id = SqlHelper.GetInt(reader, "id"),
                Username = SqlHelper.GetString(reader, "username"),
                PasswordHash = SqlHelper.GetString(reader, "password_hash"),
                PasswordSalt = SqlHelper.GetString(reader, "password_salt"),
                DisplayName = SqlHelper.GetString(reader, "display_name"),
                Alias = SqlHelper.GetString(reader, "alias"),
                RoleId = SqlHelper.GetInt(reader, "role_id"),
                IsActive = SqlHelper.GetInt(reader, "is_active"),
                CreatedAt = SqlHelper.GetString(reader, "created_at"),
                UpdatedAt = SqlHelper.GetString(reader, "updated_at")
            };
        }
    }
}
