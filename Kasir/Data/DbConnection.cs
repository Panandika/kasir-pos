using System;
using System.Data.SQLite;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;

namespace Kasir.Data
{
    public static class DbConnection
    {
        private static int _uiThreadId;
        private static readonly string DbDirectory = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "data");

        private static readonly string DbPath = Path.Combine(DbDirectory, "kasir.db");

        private static readonly string ConnectionString =
            string.Format("Data Source={0};Version=3;", DbPath);

        private static SQLiteConnection _connection;

        public static SQLiteConnection GetConnection()
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
                _connection = new SQLiteConnection(ConnectionString);
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

        public static SQLiteConnection CreateConnection()
        {
            var conn = new SQLiteConnection(ConnectionString);
            conn.Open();
            ConfigurePragmas(conn);
            TryLoadFts5(conn);
            return conn;
        }

        public static void InitializeDatabase()
        {
            if (!Directory.Exists(DbDirectory))
            {
                Directory.CreateDirectory(DbDirectory);
            }

            bool isNew = !File.Exists(DbPath);

            using (var conn = new SQLiteConnection(ConnectionString))
            {
                conn.Open();
                ConfigurePragmas(conn);
                TryLoadFts5(conn);

                if (isNew)
                {
                    string schema = ReadEmbeddedSchema();
                    ExecuteSchema(conn, schema);
                    SeedDefaultData(conn);
                }

                // Run pending schema migrations (for existing databases)
                MigrationRunner.Run(conn);
            }
        }

        private static void ConfigurePragmas(SQLiteConnection conn)
        {
            using (var cmd = new SQLiteCommand(conn))
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

        private static void TryLoadFts5(SQLiteConnection conn)
        {
            try
            {
                conn.EnableExtensions(true);
                conn.LoadExtension("SQLite.Interop.dll", "sqlite3_fts5_init");
            }
            catch (Exception)
            {
                // FTS5 may be built-in (Win11) or unavailable
                // ProductRepository.Search will use LIKE fallback
            }
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

        private static void ExecuteSchema(SQLiteConnection conn, string schema)
        {
            // Split on semicolons to handle multi-statement schema
            // But we need to be careful with triggers that contain semicolons
            // So execute the entire schema as one command
            using (var cmd = new SQLiteCommand(conn))
            {
                cmd.CommandText = schema;
                cmd.ExecuteNonQuery();
            }
        }

        private static void SeedDefaultData(SQLiteConnection conn)
        {
            using (var txn = conn.BeginTransaction())
            {
                using (var cmd = new SQLiteCommand(conn))
                {
                    // Seed roles — UPSERT to update permissions on existing DBs
                    cmd.CommandText = @"
                        INSERT INTO roles (id, name, permissions) VALUES
                        (1, 'admin', '[""*""]'),
                        (2, 'supervisor', '[""pos"",""master"",""master.department"",""master.supplier"",""master.product"",""master.credit_card"",""master.price_change"",""master.stock_opname"",""transaction"",""transaction.purchase"",""transaction.sales"",""transaction.return"",""transaction.transfer"",""transaction.stock_out"",""reports"",""reports.sales"",""reports.purchase"",""reports.stock"",""reports.master"",""inventory"",""accounting"",""bank"",""utility.backup"",""utility.printer""]'),
                        (3, 'cashier', '[""pos"",""transaction.sales"",""reports.sales""]')
                        ON CONFLICT(id) DO UPDATE SET permissions = excluded.permissions;";
                    cmd.ExecuteNonQuery();

                    // Seed admin user (password: admin, BCrypt cost 10)
                    // BCrypt hash for "admin" — pre-computed so we don't need BCrypt dependency at init time
                    cmd.CommandText = @"
                        INSERT OR IGNORE INTO users (id, username, password_hash, password_salt, display_name, alias, role_id, is_active)
                        VALUES (1, 'ADMIN', '$2a$10$N9qo8uLOickgx2ZMRZoMyeIjZAgcfl7p92ldGxad68LJZdL17lhWy', '', 'Administrator', 'ADM', 1, 1);";
                    cmd.ExecuteNonQuery();

                    // Generate cryptographically random HMAC key for this installation
                    byte[] hmacKeyBytes = new byte[32];
                    using (var rng = new RNGCryptoServiceProvider())
                    {
                        rng.GetBytes(hmacKeyBytes);
                    }
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
