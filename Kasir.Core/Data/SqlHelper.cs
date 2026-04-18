using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace Kasir.Data
{
    public static class SqlHelper
    {
        public static List<T> Query<T>(
            SqliteConnection db,
            string sql,
            Func<SqliteDataReader, T> mapper,
            params SqliteParameter[] parameters)
        {
            var results = new List<T>();
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = sql;
                foreach (var p in parameters)
                {
                    cmd.Parameters.Add(p);
                }

                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        results.Add(mapper(reader));
                    }
                }
            }
            return results;
        }

        public static T QuerySingle<T>(
            SqliteConnection db,
            string sql,
            Func<SqliteDataReader, T> mapper,
            params SqliteParameter[] parameters) where T : class
        {
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = sql;
                foreach (var p in parameters)
                {
                    cmd.Parameters.Add(p);
                }

                using (var reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        return mapper(reader);
                    }
                }
            }
            return null;
        }

        public static T ExecuteScalar<T>(
            SqliteConnection db,
            string sql,
            params SqliteParameter[] parameters)
        {
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = sql;
                foreach (var p in parameters)
                {
                    cmd.Parameters.Add(p);
                }

                object result = cmd.ExecuteScalar();
                if (result == null || result == DBNull.Value)
                {
                    return default(T);
                }
                return (T)Convert.ChangeType(result, typeof(T));
            }
        }

        public static int ExecuteNonQuery(
            SqliteConnection db,
            string sql,
            params SqliteParameter[] parameters)
        {
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = sql;
                foreach (var p in parameters)
                {
                    cmd.Parameters.Add(p);
                }

                return cmd.ExecuteNonQuery();
            }
        }

        public static long LastInsertRowId(SqliteConnection db)
        {
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = "SELECT last_insert_rowid();";
                object result = cmd.ExecuteScalar();
                return result == null || result == DBNull.Value ? 0L : Convert.ToInt64(result);
            }
        }

        public static SqliteParameter Param(string name, object value)
        {
            return new SqliteParameter(name, value ?? DBNull.Value);
        }

        private static int FindOrdinal(SqliteDataReader reader, string column)
        {
            // Microsoft.Data.Sqlite's GetOrdinal is case-sensitive and throws when
            // the column is missing. Emulate System.Data.SQLite's lenient behavior:
            // case-insensitive lookup, returning -1 when absent.
            for (int i = 0; i < reader.FieldCount; i++)
            {
                if (string.Equals(reader.GetName(i), column, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
            return -1;
        }

        public static string GetString(SqliteDataReader reader, string column)
        {
            int ordinal = FindOrdinal(reader, column);
            if (ordinal < 0) return null;
            return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
        }

        public static int GetInt(SqliteDataReader reader, string column)
        {
            int ordinal = FindOrdinal(reader, column);
            if (ordinal < 0) return 0;
            return reader.IsDBNull(ordinal) ? 0 : reader.GetInt32(ordinal);
        }

        public static long GetLong(SqliteDataReader reader, string column)
        {
            int ordinal = FindOrdinal(reader, column);
            if (ordinal < 0) return 0L;
            return reader.IsDBNull(ordinal) ? 0L : reader.GetInt64(ordinal);
        }
    }
}
