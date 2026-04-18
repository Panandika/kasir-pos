using System;
using System.Collections.Generic;
using System.Data.SQLite;

namespace Kasir.Data
{
    public static class SqlHelper
    {
        public static List<T> Query<T>(
            SQLiteConnection db,
            string sql,
            Func<SQLiteDataReader, T> mapper,
            params SQLiteParameter[] parameters)
        {
            var results = new List<T>();
            using (var cmd = new SQLiteCommand(sql, db))
            {
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
            SQLiteConnection db,
            string sql,
            Func<SQLiteDataReader, T> mapper,
            params SQLiteParameter[] parameters) where T : class
        {
            using (var cmd = new SQLiteCommand(sql, db))
            {
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
            SQLiteConnection db,
            string sql,
            params SQLiteParameter[] parameters)
        {
            using (var cmd = new SQLiteCommand(sql, db))
            {
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
            SQLiteConnection db,
            string sql,
            params SQLiteParameter[] parameters)
        {
            using (var cmd = new SQLiteCommand(sql, db))
            {
                foreach (var p in parameters)
                {
                    cmd.Parameters.Add(p);
                }

                return cmd.ExecuteNonQuery();
            }
        }

        public static SQLiteParameter Param(string name, object value)
        {
            return new SQLiteParameter(name, value ?? DBNull.Value);
        }

        public static string GetString(SQLiteDataReader reader, string column)
        {
            int ordinal = reader.GetOrdinal(column);
            return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
        }

        public static int GetInt(SQLiteDataReader reader, string column)
        {
            int ordinal = reader.GetOrdinal(column);
            return reader.IsDBNull(ordinal) ? 0 : reader.GetInt32(ordinal);
        }

        public static long GetLong(SQLiteDataReader reader, string column)
        {
            int ordinal = reader.GetOrdinal(column);
            return reader.IsDBNull(ordinal) ? 0L : reader.GetInt64(ordinal);
        }
    }
}
