using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using System.IO;

namespace Kasir.Data
{
    public class ValidationResult
    {
        public bool IsValid { get { return Errors.Count == 0; } }
        public List<string> Errors { get; private set; }
        public int SchemaVersion { get; set; }

        public ValidationResult()
        {
            Errors = new List<string>();
            SchemaVersion = 0;
        }

        public void AddError(string message)
        {
            Errors.Add(message);
        }
    }

    public static class DatabaseValidator
    {
        public static int ExpectedSchemaVersion => MigrationRunner.LatestVersion;

        // The 57 base tables the app relies on. Derived from Schema.sql.
        // FTS virtual tables (products_fts, products_fts_*) and views (v_*) excluded.
        private static readonly string[] RequiredTables = new string[]
        {
            "config", "counters", "roles", "users", "fiscal_periods",
            "accounts", "account_balances", "account_config",
            "subsidiaries", "products", "product_types",
            "departments", "locations", "credit_cards", "members",
            "discounts", "discount_partners", "promotional_prices",
            "stock_movements", "stock_register", "points_register",
            "gl_details", "payables_register", "giro_register", "order_register",
            "sales", "sale_items", "purchases", "purchase_items",
            "cash_transactions", "cash_transaction_lines",
            "memorial_journals", "memorial_journal_lines",
            "orders", "order_items",
            "stock_transfers", "stock_transfer_items",
            "stock_adjustments", "stock_adjustment_items",
            "pending_sales", "stock_opname", "price_history", "budget",
            "tax_invoices",
            "fixed_assets", "fixed_asset_accounts", "fixed_asset_details",
            "fixed_asset_transactions", "fixed_asset_transaction_items",
            "giro_conversions", "invoices", "invoice_lines",
            "sync_log", "sync_queue", "audit_log", "shifts"
        };

        private static readonly Dictionary<string, string[]> RequiredColumns =
            new Dictionary<string, string[]>
            {
                { "users", new[] { "id", "username", "password_hash", "role_id", "is_active" } },
                { "roles", new[] { "id", "name", "permissions" } },
                { "config", new[] { "key", "value" } },
                { "counters", new[] { "prefix", "register_id", "current_value" } },
                { "products", new[] { "id", "product_code", "name" } },
                { "sales", new[] { "id", "journal_no", "total_value" } },
                { "accounts", new[] { "account_code", "account_name" } }
            };

        public static ValidationResult Validate(string dbPath, bool runIntegrityCheck = false)
        {
            var result = new ValidationResult();

            if (!File.Exists(dbPath))
            {
                result.AddError("File not found: " + dbPath);
                return result;
            }

            if (new FileInfo(dbPath).Length == 0)
            {
                result.AddError("Database file is empty (0 bytes).");
                return result;
            }

            string connStr = string.Format("Data Source={0}", dbPath);

            try
            {
                using (var conn = new SqliteConnection(connStr))
                {
                    conn.Open();

                    if (runIntegrityCheck && !CheckIntegrity(conn, result))
                    {
                        return result;
                    }

                    var existingTables = LoadTableNames(conn);
                    CheckRequiredTables(existingTables, result);

                    if (!result.IsValid)
                    {
                        // If base tables missing, column checks would duplicate noise
                        return result;
                    }

                    CheckRequiredColumns(conn, result);
                    CheckSchemaVersion(conn, result);
                    CheckAtLeastOneActiveUser(conn, result);
                }
            }
            catch (SqliteException ex)
            {
                result.AddError("SQLite error opening database: " + ex.Message);
            }
            catch (Exception ex)
            {
                result.AddError("Unexpected error validating database: " + ex.Message);
            }

            return result;
        }

        private static bool CheckIntegrity(SqliteConnection conn, ValidationResult result)
        {
            using (var cmd = new SqliteCommand("PRAGMA integrity_check;", conn))
            using (var reader = cmd.ExecuteReader())
            {
                if (reader.Read())
                {
                    string status = reader.GetString(0);
                    if (!string.Equals(status, "ok", StringComparison.OrdinalIgnoreCase))
                    {
                        result.AddError("Integrity check failed: " + status);
                        return false;
                    }
                }
            }
            return true;
        }

        private static HashSet<string> LoadTableNames(SqliteConnection conn)
        {
            var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var cmd = new SqliteCommand(
                "SELECT name FROM sqlite_master WHERE type IN ('table','view');", conn))
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    tables.Add(reader.GetString(0));
                }
            }
            return tables;
        }

        private static void CheckRequiredTables(HashSet<string> existing, ValidationResult result)
        {
            var missing = new List<string>();
            foreach (var t in RequiredTables)
            {
                if (!existing.Contains(t))
                {
                    missing.Add(t);
                }
            }
            if (missing.Count > 0)
            {
                result.AddError("Missing required tables: " + string.Join(", ", missing));
            }
        }

        private static void CheckRequiredColumns(SqliteConnection conn, ValidationResult result)
        {
            foreach (var kv in RequiredColumns)
            {
                var cols = LoadColumns(conn, kv.Key);
                var missing = new List<string>();
                foreach (var c in kv.Value)
                {
                    if (!cols.Contains(c))
                    {
                        missing.Add(c);
                    }
                }
                if (missing.Count > 0)
                {
                    result.AddError(string.Format(
                        "Table '{0}' missing columns: {1}", kv.Key, string.Join(", ", missing)));
                }
            }
        }

        private static HashSet<string> LoadColumns(SqliteConnection conn, string table)
        {
            var cols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var cmd = new SqliteCommand("PRAGMA table_info(" + table + ");", conn))
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    cols.Add(reader.GetString(1));
                }
            }
            return cols;
        }

        private static void CheckSchemaVersion(SqliteConnection conn, ValidationResult result)
        {
            try
            {
                using (var cmd = new SqliteCommand(
                    "SELECT value FROM config WHERE key = 'schema_version';", conn))
                {
                    var raw = cmd.ExecuteScalar();
                    if (raw == null)
                    {
                        result.AddError("config.schema_version is missing.");
                        return;
                    }
                    int version;
                    if (!int.TryParse(raw.ToString(), out version))
                    {
                        result.AddError("config.schema_version is not an integer: " + raw);
                        return;
                    }
                    result.SchemaVersion = version;
                    if (version > ExpectedSchemaVersion)
                    {
                        result.AddError(string.Format(
                            "Database schema version {0} is newer than this app expects ({1}). Upgrade the application.",
                            version, ExpectedSchemaVersion));
                    }
                    // Older versions are acceptable — MigrationRunner will upgrade.
                }
            }
            catch (SqliteException ex)
            {
                result.AddError("Could not read schema_version: " + ex.Message);
            }
        }

        private static void CheckAtLeastOneActiveUser(SqliteConnection conn, ValidationResult result)
        {
            try
            {
                using (var cmd = new SqliteCommand(
                    "SELECT COUNT(*) FROM users WHERE is_active = 1;", conn))
                {
                    var raw = cmd.ExecuteScalar();
                    long count = raw == null ? 0 : Convert.ToInt64(raw);
                    if (count == 0)
                    {
                        result.AddError("No active users found — login would be impossible.");
                    }
                }
            }
            catch (SqliteException ex)
            {
                result.AddError("Could not count users: " + ex.Message);
            }
        }
    }
}
