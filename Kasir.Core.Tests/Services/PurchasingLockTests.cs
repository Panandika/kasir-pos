using System.Linq;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using FluentAssertions;
using Kasir.Auth;
using Kasir.Data;
using Kasir.Data.Migrations;
using Kasir.Data.Repositories;
using Kasir.Services;
using Kasir.Tests.TestHelpers;

namespace Kasir.Tests.Services
{
    // WP-05: POS purchasing is locked by default (purchasing moves to the dashboard).
    // An owner can open it in an emergency; it locks again on the next app start.
    [TestFixture]
    public class PurchasingLockTests
    {
        private SqliteConnection _db;
        private ConfigRepository _config;

        [SetUp]
        public void SetUp()
        {
            _db = TestDb.Create();
            _config = new ConfigRepository(_db);
            SqlHelper.ExecuteNonQuery(_db,
                @"INSERT OR IGNORE INTO roles (id, name, permissions) VALUES
                  (1, 'admin', '[""*""]'),
                  (2, 'supervisor', '[""pos"",""transaction"",""transaction.purchase""]'),
                  (3, 'cashier', '[""pos""]')");
            AddUser(1, "OWNER", "rahasia", roleId: 1, active: 1);
            AddUser(2, "SPV", "spv123", roleId: 2, active: 1);
            AddUser(3, "OLDOWNER", "lama", roleId: 1, active: 0);
        }

        [TearDown]
        public void TearDown()
        {
            _db.Close();
            _db.Dispose();
        }

        private void AddUser(int id, string username, string password, int roleId, int active)
        {
            SqlHelper.ExecuteNonQuery(_db,
                @"INSERT INTO users (id, username, password_hash, password_salt, display_name, alias, role_id, is_active)
                  VALUES (@id, @u, @hash, '', @u, 'X', @role, @active)",
                SqlHelper.Param("@id", id),
                SqlHelper.Param("@u", username),
                SqlHelper.Param("@hash", AuthService.HashPassword(password)),
                SqlHelper.Param("@role", roleId),
                SqlHelper.Param("@active", active));
        }

        private PurchasingLockService Lock() => new PurchasingLockService(_db);

        private long AuditCount() =>
            SqlHelper.ExecuteScalar<long>(_db, "SELECT COUNT(*) FROM config_audit WHERE key = 'purchasing_locked'");

        [Test]
        public void purchasing_locked_blocks_form()
        {
            // Default schema: locked, with the message the four screens show.
            _config.Get(PurchasingLockService.ConfigKey).Should().Be("true");
            Lock().IsLocked.Should().BeTrue();
            PurchasingLockService.LockedMessage.Should().Be("Pembelian sekarang lewat dashboard.");
            PurchasingLockService.EmergencyLinkText.Should().Contain("buka darurat");
        }

        [Test]
        public void emergency_unlock_allows_purchasing()
        {
            var result = Lock().EmergencyUnlock("owner", "rahasia");

            result.Success.Should().BeTrue(result.Message);
            result.Username.Should().Be("OWNER");
            Lock().IsLocked.Should().BeFalse();
            _config.Get(PurchasingLockService.ConfigKey).Should().Be("false");
        }

        [Test]
        public void EmergencyUnlock_IsLogged_WithWhoAndOldNewValues()
        {
            Lock().EmergencyUnlock("OWNER", "rahasia").Success.Should().BeTrue();

            var entry = Lock().History().Single();
            entry.Key.Should().Be("purchasing_locked");
            entry.OldValue.Should().Be("true");
            entry.NewValue.Should().Be("false");
            entry.Username.Should().Be("OWNER");
            entry.Source.Should().Be(PurchasingLockService.SourceEmergency);
            entry.ChangedAt.Should().NotBeNullOrEmpty();
        }

        [Test]
        public void Unlock_WrongPassword_StaysLocked_AndLogsNothing()
        {
            var result = Lock().EmergencyUnlock("OWNER", "salah");

            result.Success.Should().BeFalse();
            result.Message.Should().Be("Nama pengguna atau kata sandi salah.");
            Lock().IsLocked.Should().BeTrue();
            AuditCount().Should().Be(0);
        }

        [Test]
        public void Unlock_ByNonOwner_IsRefused()
        {
            var result = Lock().EmergencyUnlock("SPV", "spv123");

            result.Success.Should().BeFalse();
            result.Message.Should().Contain("pemilik");
            Lock().IsLocked.Should().BeTrue();
        }

        [Test]
        public void Unlock_ByInactiveOwner_IsRefused()
        {
            Lock().EmergencyUnlock("OLDOWNER", "lama").Success.Should().BeFalse();
            Lock().IsLocked.Should().BeTrue();
        }

        [Test]
        public void Unlock_EmptyCredentials_IsRefused()
        {
            Lock().EmergencyUnlock("", "").Success.Should().BeFalse();
            Lock().EmergencyUnlock("OWNER", "").Success.Should().BeFalse();
            Lock().IsLocked.Should().BeTrue();
        }

        [Test]
        public void Unlock_SharesTheLoginThrottle()
        {
            // Three wrong passwords trip the persisted login lockout; even the right
            // password is then refused, so the prompt cannot be used to guess.
            for (int i = 0; i < 3; i++) Lock().EmergencyUnlock("OWNER", "salah" + i);

            var result = Lock().EmergencyUnlock("OWNER", "rahasia");
            result.Success.Should().BeFalse();
            result.Message.Should().Contain("Terlalu banyak percobaan");
            Lock().IsLocked.Should().BeTrue();
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("no")]
        [TestCase("0")]
        [TestCase("TRUE")]
        public void AnythingButFalse_IsLocked(string value)
        {
            if (value == null)
                SqlHelper.ExecuteNonQuery(_db, "DELETE FROM config WHERE key = 'purchasing_locked'");
            else
                _config.Set(PurchasingLockService.ConfigKey, value);

            Lock().IsLocked.Should().BeTrue();
        }

        [TestCase("false")]
        [TestCase(" FALSE ")]
        public void False_IsUnlocked(string value)
        {
            _config.Set(PurchasingLockService.ConfigKey, value);
            Lock().IsLocked.Should().BeFalse();
        }

        [Test]
        public void ReengageOnStartup_LocksAgainAfterAnUnlock()
        {
            Lock().EmergencyUnlock("OWNER", "rahasia").Success.Should().BeTrue();

            Lock().ReengageOnStartup().Should().BeTrue();

            Lock().IsLocked.Should().BeTrue();
            var latest = Lock().History().First();
            latest.OldValue.Should().Be("false");
            latest.NewValue.Should().Be("true");
            latest.Source.Should().Be(PurchasingLockService.SourceStartup);
            latest.Username.Should().Be("system");
        }

        [Test]
        public void ReengageOnStartup_WhenAlreadyLocked_DoesNothing()
        {
            Lock().ReengageOnStartup().Should().BeFalse();
            AuditCount().Should().Be(0);
        }

        [Test]
        public void ReengageOnStartup_RepairsAMissingKey()
        {
            SqlHelper.ExecuteNonQuery(_db, "DELETE FROM config WHERE key = 'purchasing_locked'");

            Lock().ReengageOnStartup().Should().BeTrue();

            _config.Get(PurchasingLockService.ConfigKey).Should().Be("true");
        }

        [Test]
        public void AdminLock_AndUnlock_AreLoggedAsAdmin()
        {
            Lock().Unlock("OWNER", "rahasia", PurchasingLockService.SourceAdmin).Success.Should().BeTrue();
            Lock().Lock("OWNER", PurchasingLockService.SourceAdmin);

            Lock().IsLocked.Should().BeTrue();
            var history = Lock().History();
            history.Should().HaveCount(2);
            history.Should().OnlyContain(h => h.Source == PurchasingLockService.SourceAdmin && h.Username == "OWNER");
            history[0].NewValue.Should().Be("true");
            history[1].NewValue.Should().Be("false");
        }

        [Test]
        public void Migration016_SeedsLockedOnAnOlderDb_AndKeepsAnOwnersChoice()
        {
            SqlHelper.ExecuteNonQuery(_db,
                "DROP TABLE config_audit; DELETE FROM config WHERE key = 'purchasing_locked';");

            new Migration_016().Up(_db);
            _config.Get(PurchasingLockService.ConfigKey).Should().Be("true");
            SqlHelper.ExecuteScalar<long>(_db,
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='config_audit'").Should().Be(1);

            _config.Set(PurchasingLockService.ConfigKey, "false");
            new Migration_016().Up(_db);
            _config.Get(PurchasingLockService.ConfigKey).Should().Be("false");
        }
    }
}
