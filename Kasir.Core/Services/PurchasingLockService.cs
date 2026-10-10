using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Kasir.Auth;
using Kasir.Data;
using Kasir.Data.Repositories;

namespace Kasir.Services
{
    /// <summary>
    /// WP-05 POS purchasing lock. Purchasing (order, goods receipt, invoice, return) is
    /// entered on the dashboard; the four POS purchasing screens stay locked while
    /// config.purchasing_locked is anything but 'false' (missing or garbled = locked, so a
    /// bad value can never open the screens and cause a double entry).
    ///
    /// Unlocking needs an owner login (a user with the purchasing.unlock permission; the
    /// admin role has every permission). It is temporary: <see cref="ReengageOnStartup"/>
    /// locks again on the next app start. Every change is written to config_audit.
    /// </summary>
    public class PurchasingLockService
    {
        public const string ConfigKey = "purchasing_locked";
        public const string UnlockPermission = "purchasing.unlock";

        public const string LockedMessage = "Pembelian sekarang lewat dashboard.";
        public const string LockedDetail =
            "Pesanan, penerimaan barang, nota, dan retur pembelian dicatat di dashboard supaya tidak tercatat dua kali.";
        public const string EmergencyLinkText = "Internet mati? Ketuk di sini untuk buka darurat.";

        public const string SourceEmergency = "emergency";
        public const string SourceAdmin = "admin";
        public const string SourceStartup = "startup";

        private const string Description = "When true, POS purchasing is disabled; use dashboard instead";

        private readonly SqliteConnection _db;
        private readonly ConfigRepository _config;
        private readonly Func<string, string, LoginResult> _login;
        private readonly PermissionService _permissions;

        public PurchasingLockService(SqliteConnection db)
            : this(db, new AuthService(db).Login)
        {
        }

        // Test seam: the credential check (AuthService.Login, which carries the shared
        // failed-login throttle so the unlock prompt cannot be used to guess passwords).
        public PurchasingLockService(SqliteConnection db, Func<string, string, LoginResult> login)
        {
            _db = db;
            _config = new ConfigRepository(db);
            _login = login;
            _permissions = new PermissionService(db);
        }

        public bool IsLocked
        {
            get
            {
                string value;
                try { value = _config.Get(ConfigKey); }
                catch (SqliteException) { return true; }
                return !string.Equals(value?.Trim(), "false", StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Opens POS purchasing until the next app start. Needs the credentials of an
        /// active owner. <paramref name="source"/> is <see cref="SourceEmergency"/> from the
        /// lock banner or <see cref="SourceAdmin"/> from the Utility menu.
        /// </summary>
        public PurchasingLockResult Unlock(string username, string password, string source)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
                return PurchasingLockResult.Fail("Isi nama pengguna dan kata sandi pemilik.");

            LoginResult login = _login(username, password);
            if (login == null || !login.Success || login.User == null)
            {
                string reason = login?.ErrorMessage ?? "";
                if (reason.StartsWith("Account locked", StringComparison.OrdinalIgnoreCase))
                    return PurchasingLockResult.Fail("Terlalu banyak percobaan. Coba lagi sebentar lagi.");
                return PurchasingLockResult.Fail("Nama pengguna atau kata sandi salah.");
            }

            if (!_permissions.HasPermission(login.User, UnlockPermission))
                return PurchasingLockResult.Fail("Hanya pemilik toko yang boleh membuka kunci pembelian.");

            Change("false", login.User.Username, source);
            return PurchasingLockResult.Ok(login.User.Username);
        }

        public PurchasingLockResult EmergencyUnlock(string username, string password)
        {
            return Unlock(username, password, SourceEmergency);
        }

        /// <summary>Locks purchasing again. Locking is always safe, so no password.</summary>
        public void Lock(string username, string source)
        {
            Change("true", username, source);
        }

        /// <summary>
        /// Called once per app start: an unlock lasts only until the app restarts.
        /// Returns true when it had to lock (purchasing was left open).
        /// </summary>
        public bool ReengageOnStartup()
        {
            string current = _config.Get(ConfigKey);
            if (current != null && current.Trim() == "true") return false;
            Change("true", "system", SourceStartup);
            return true;
        }

        public List<ConfigAuditEntry> History(int limit = 20)
        {
            return SqlHelper.Query(_db,
                @"SELECT id, key, old_value, new_value, changed_at, username, source
                  FROM config_audit WHERE key = @key ORDER BY id DESC LIMIT @limit",
                r => new ConfigAuditEntry
                {
                    Id = SqlHelper.GetLong(r, "id"),
                    Key = SqlHelper.GetString(r, "key"),
                    OldValue = SqlHelper.GetString(r, "old_value"),
                    NewValue = SqlHelper.GetString(r, "new_value"),
                    ChangedAt = SqlHelper.GetString(r, "changed_at"),
                    Username = SqlHelper.GetString(r, "username"),
                    Source = SqlHelper.GetString(r, "source"),
                },
                SqlHelper.Param("@key", ConfigKey),
                SqlHelper.Param("@limit", limit));
        }

        private void Change(string newValue, string username, string source)
        {
            using (var txn = _db.BeginTransaction())
            {
                string oldValue = _config.Get(ConfigKey);
                int updated = SqlHelper.ExecuteNonQuery(_db,
                    "UPDATE config SET value = @value WHERE key = @key",
                    SqlHelper.Param("@value", newValue),
                    SqlHelper.Param("@key", ConfigKey));
                if (updated == 0)
                {
                    SqlHelper.ExecuteNonQuery(_db,
                        "INSERT INTO config (key, value, description) VALUES (@key, @value, @desc)",
                        SqlHelper.Param("@key", ConfigKey),
                        SqlHelper.Param("@value", newValue),
                        SqlHelper.Param("@desc", Description));
                }
                SqlHelper.ExecuteNonQuery(_db,
                    @"INSERT INTO config_audit (key, old_value, new_value, username, source)
                      VALUES (@key, @old, @new, @user, @source)",
                    SqlHelper.Param("@key", ConfigKey),
                    SqlHelper.Param("@old", oldValue),
                    SqlHelper.Param("@new", newValue),
                    SqlHelper.Param("@user", username),
                    SqlHelper.Param("@source", source));
                txn.Commit();
            }
        }
    }

    public class PurchasingLockResult
    {
        public bool Success { get; private set; }
        public string Message { get; private set; }
        public string Username { get; private set; }

        public static PurchasingLockResult Ok(string username)
        {
            return new PurchasingLockResult
            {
                Success = true,
                Username = username,
                Message = "Pembelian di kasir dibuka sementara. Terkunci lagi saat aplikasi dibuka ulang.",
            };
        }

        public static PurchasingLockResult Fail(string message)
        {
            return new PurchasingLockResult { Success = false, Message = message };
        }
    }

    public class ConfigAuditEntry
    {
        public long Id { get; set; }
        public string Key { get; set; }
        public string OldValue { get; set; }
        public string NewValue { get; set; }
        public string ChangedAt { get; set; }
        public string Username { get; set; }
        public string Source { get; set; }
    }
}
