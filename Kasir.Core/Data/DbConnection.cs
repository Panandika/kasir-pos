using System;
using Microsoft.Data.Sqlite;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;

namespace Kasir.Data
{
    /// <summary>
    /// Result returned by a first-run handler: the user's chosen schema path
    /// (either "seed" for embedded schema, or an absolute path to an import DB),
    /// or null to cancel startup.
    /// </summary>
    public class FirstRunResult
    {
        /// <summary>"seed" to create from embedded schema, "import" to copy ImportPath, or null to cancel.</summary>
        public string Choice { get; set; }
        public string ImportPath { get; set; }
    }

    public static class DbConnection
    {
        /// <summary>
        /// UI-agnostic handler invoked on first run. Host (WinForms/Avalonia) must
        /// set this before calling InitializeDatabase() on a fresh install.
        /// Return null to abort startup.
        /// </summary>
        public static Func<FirstRunResult> FirstRunHandler { get; set; }

        private static int _uiThreadId;
        private static readonly string DbDirectory = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "data");

        private static readonly string DbPath = Path.Combine(DbDirectory, "kasir.db");

        private static readonly string ConnectionString =
            string.Format("Data Source={0}", DbPath);

        private static SqliteConnection _connection;

        public static SqliteConnection GetConnection()
        {
            // Record UI thread on first call; warn if called from a background thread
            if (_uiThreadId == 0)
            {
                _uiThreadId = Thread.CurrentThread.ManagedThreadId;
            }
            Debug.Assert(Thread.CurrentThread.ManagedThreadId == _uiThreadId,
                "GetConnection() called from background thread — use CreateConnection() instead");

            if (_connection == null)
            {
                _connection = new SqliteConnection(ConnectionString);
                _connection.Open();
                ConfigurePragmas(_connection);
                TryLoadFts5(_connection);
            }

            if (_connection.State != System.Data.ConnectionState.Open)
            {
                _connection.Open();
                ConfigurePragmas(_connection);
                TryLoadFts5(_connection);
            }

            return _connection;
        }

        public static SqliteConnection CreateConnection()
        {
            var conn = new SqliteConnection(ConnectionString);
            conn.Open();
            ConfigurePragmas(conn);
            TryLoadFts5(conn);
            return conn;
        }

        public static bool IsFreshInstall()
        {
            if (!Directory.Exists(DbDirectory)) return true;
            return !File.Exists(DbPath) || new FileInfo(DbPath).Length == 0;
        }

        public static void InitializeDatabase()
        {
            if (!Directory.Exists(DbDirectory))
            {
                Directory.CreateDirectory(DbDirectory);
            }

            bool isFresh = !File.Exists(DbPath) || new FileInfo(DbPath).Length == 0;

            if (isFresh)
            {
                HandleFirstRun();
            }
            else
            {
                var validation = Kasir.Data.DatabaseValidator.Validate(DbPath);
                if (!validation.IsValid)
                {
                    throw new DatabaseCorruptException(validation.Errors);
                }
            }

            using (var conn = new SqliteConnection(ConnectionString))
            {
                conn.Open();
                ConfigurePragmas(conn);
                TryLoadFts5(conn);

                // Run pending schema migrations (for existing and imported databases)
                MigrationRunner.Run(conn);
            }
        }

        private static void HandleFirstRun()
        {
            if (FirstRunHandler == null)
            {
                throw new InvalidOperationException(
                    "DbConnection.FirstRunHandler must be set before InitializeDatabase() on a fresh install.");
            }

            FirstRunResult result = FirstRunHandler();
            if (result == null || string.IsNullOrEmpty(result.Choice))
            {
                Environment.Exit(0);
                return;
            }

            if (string.Equals(result.Choice, "seed", StringComparison.OrdinalIgnoreCase))
            {
                CreateFromEmbeddedSchema();
            }
            else if (string.Equals(result.Choice, "import", StringComparison.OrdinalIgnoreCase))
            {
                var validation = Kasir.Data.DatabaseValidator.Validate(
                    result.ImportPath, runIntegrityCheck: true);
                if (!validation.IsValid)
                {
                    throw new DatabaseCorruptException(validation.Errors);
                }
                File.Copy(result.ImportPath, DbPath, overwrite: true);
            }
            else
            {
                Environment.Exit(0);
            }
        }

        private static void CreateFromEmbeddedSchema()
        {
            using (var conn = new SqliteConnection(ConnectionString))
            {
                conn.Open();
                ConfigurePragmas(conn);
                TryLoadFts5(conn);

                string schema = ReadEmbeddedSchema();
                ExecuteSchema(conn, schema);
                SeedDefaultData(conn);
            }
        }

        private static void ConfigurePragmas(SqliteConnection conn)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "PRAGMA journal_mode=WAL;";
                cmd.ExecuteNonQuery();

                cmd.CommandText = "PRAGMA busy_timeout=5000;";
                cmd.ExecuteNonQuery();

                cmd.CommandText = "PRAGMA synchronous=NORMAL;";
                cmd.ExecuteNonQuery();

                cmd.CommandText = "PRAGMA foreign_keys=ON;";
                cmd.ExecuteNonQuery();

                cmd.CommandText = "PRAGMA cache_size=-2000;";
                cmd.ExecuteNonQuery();

                cmd.CommandText = "PRAGMA temp_store=MEMORY;";
                cmd.ExecuteNonQuery();
            }
        }

        private static void TryLoadFts5(SqliteConnection conn)
        {
            // FTS5 is compiled into SQLitePCLRaw.bundle_e_sqlite3 — no extension loading needed
        }

        private static string ReadEmbeddedSchema()
        {
            var assembly = Assembly.GetExecutingAssembly();
            string resourceName = "Kasir.Data.Schema.sql";

            using (var stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    throw new InvalidOperationException(
                        "Embedded resource not found: " + resourceName);
                }

                using (var reader = new StreamReader(stream))
                {
                    return reader.ReadToEnd();
                }
            }
        }

        private static void ExecuteSchema(SqliteConnection conn, string schema)
        {
            // Split on semicolons to handle multi-statement schema
            // But we need to be careful with triggers that contain semicolons
            // So execute the entire schema as one command
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = schema;
                cmd.ExecuteNonQuery();
            }
        }

        private static void SeedDefaultData(SqliteConnection conn)
        {
            using (var txn = conn.BeginTransaction())
            {
                using (var cmd = conn.CreateCommand())
                {
                    // Seed roles — UPSERT to update permissions on existing DBs
                    cmd.CommandText = @"
                        INSERT INTO roles (id, name, permissions) VALUES
                        (1, 'admin', '[""*""]'),
                        (2, 'supervisor', '[""pos"",""master"",""master.department"",""master.supplier"",""master.product"",""master.credit_card"",""master.price_change"",""master.stock_opname"",""transaction"",""transaction.purchase"",""transaction.sales"",""transaction.return"",""transaction.transfer"",""transaction.stock_out"",""reports"",""reports.sales"",""reports.purchase"",""reports.stock"",""reports.master"",""inventory"",""accounting"",""bank"",""utility.backup"",""utility.printer""]'),
                        (3, 'cashier', '[""pos"",""transaction.sales"",""reports.sales""]')
                        ON CONFLICT(id) DO UPDATE SET permissions = excluded.permissions;";
                    cmd.ExecuteNonQuery();

                    // Seed default user (username SM, password 74121, BCrypt cost 10)
                    // Pre-computed hash so we don't need BCrypt dependency at init time.
                    // NOTE: This is a development/first-run seed credential. Change immediately
                    // in production by editing the user's password via Admin > User Management.
                    cmd.CommandText = @"
                        INSERT OR IGNORE INTO users (id, username, password_hash, password_salt, display_name, alias, role_id, is_active)
                        VALUES (1, 'SM', '$2a$10$NY6KtUrBIR4TrEXaYB/do.3q4.2hQPBZmoFDYqRcf2hxdpnsdvlY.', '', 'Sinar Makmur', 'SM', 1, 1);";
                    cmd.ExecuteNonQuery();

                    // Generate cryptographically random HMAC key for this installation
                    byte[] hmacKeyBytes = new byte[32];
                    RandomNumberGenerator.Fill(hmacKeyBytes);
                    string hmacKey = Convert.ToBase64String(hmacKeyBytes);

                    // Seed config
                    cmd.CommandText = @"
                        INSERT OR IGNORE INTO config (key, value, description) VALUES
                        ('register_id', '01', 'This register machine ID'),
                        ('schema_version', '1', 'Database schema version'),
                        ('store_name', 'TOKO SINAR MAKMUR', 'Store name for receipts'),
                        ('store_address', 'JL PULAU BATAM NO. 26', 'Store address for receipts'),
                        ('store_tagline', 'ALAT LISTRIK & KEBUTUHAN SEHARI-HARI', 'Store tagline for receipts'),
                        ('store_brand', 'Semoga Berbahagia', 'Brand name for receipts'),
                        ('store_footer', 'Sadhu Sadhu Sadhu', 'Footer text for receipts'),
                        ('printer_name', 'EPSON TM-U220 Receipt', 'Receipt printer name'),
                        ('sync_enabled', 'false', 'Enable multi-register sync'),
                        ('sync_role', 'hub', 'Sync role: hub or slave'),
                        ('sync_hub_share', '\\\\KASIR01\\kasir\\sync', 'UNC path to sync share'),
                        ('sync_hmac_key', @hmacKey, 'HMAC-SHA256 key for sync and update signing'),
                        ('update_share', '\\\\KASIR01\\kasir\\updates\\latest', 'UNC path to update share'),
                        ('update_auto_check', 'false', 'Auto-check for updates after login'),
                        ('last_update_check', '', 'Timestamp of last update check');";
                    cmd.Parameters.AddWithValue("@hmacKey", hmacKey);
                    cmd.ExecuteNonQuery();

                    // Seed counter prefixes to avoid race on first use
                    cmd.CommandText = @"
                        INSERT OR IGNORE INTO counters (prefix, register_id, current_value, format) VALUES
                        ('KLR', '01', 0, '{prefix}-{REG}-{YYMM}-{SEQ:04d}'),
                        ('OMS', '01', 0, '{prefix}-{REG}-{YYMM}-{SEQ:04d}'),
                        ('BPB', '01', 0, '{prefix}-{REG}-{YYMM}-{SEQ:04d}'),
                        ('MSK', '01', 0, '{prefix}-{REG}-{YYMM}-{SEQ:04d}'),
                        ('RMS', '01', 0, '{prefix}-{REG}-{YYMM}-{SEQ:04d}'),
                        ('TRM', '01', 0, '{prefix}-{REG}-{YYMM}-{SEQ:04d}'),
                        ('OPN', '01', 0, '{prefix}-{REG}-{YYMM}-{SEQ:04d}'),
                        ('OTM', '01', 0, '{prefix}-{REG}-{YYMM}-{SEQ:04d}'),
                        ('UMH', '01', 0, '{prefix}-{REG}-{YYMM}-{SEQ:04d}'),
                        ('KMS', '01', 0, '{prefix}-{REG}-{YYMM}-{SEQ:04d}'),
                        ('KKL', '01', 0, '{prefix}-{REG}-{YYMM}-{SEQ:04d}'),
                        ('BMS', '01', 0, '{prefix}-{REG}-{YYMM}-{SEQ:04d}'),
                        ('BKL', '01', 0, '{prefix}-{REG}-{YYMM}-{SEQ:04d}');";
                    cmd.ExecuteNonQuery();
                }

                txn.Commit();
            }
        }

        public static void CloseConnection()
        {
            if (_connection != null)
            {
                if (_connection.State != System.Data.ConnectionState.Closed)
                {
                    _connection.Close();
                }
                _connection.Dispose();
                _connection = null;
            }
        }
    }
}
