using Microsoft.Data.Sqlite;
using System.Diagnostics;
using NUnit.Framework;
using FluentAssertions;
using Kasir.Auth;
using Kasir.Data;
using Kasir.Tests.TestHelpers;

namespace Kasir.Tests.Auth
{
    [TestFixture]
    public class AuthServiceTests
    {
        private SqliteConnection _db;
        private AuthService _auth;

        [SetUp]
        public void SetUp()
        {
            _db = TestDb.Create();
            SeedTestUser(_db);
            _auth = new AuthService(_db);
        }

        [TearDown]
        public void TearDown()
        {
            _db.Close();
            _db.Dispose();
        }

        [Test]
        public void Login_CorrectPassword_ReturnsSuccess()
        {
            var result = _auth.Login("ADMIN", "admin");

            result.Success.Should().BeTrue();
            result.User.Should().NotBeNull();
            result.User.Username.Should().Be("ADMIN");
            _auth.IsLoggedIn.Should().BeTrue();
        }

        [Test]
        public void Login_WrongPassword_ReturnsFail()
        {
            var result = _auth.Login("ADMIN", "wrongpassword");

            result.Success.Should().BeFalse();
            result.ErrorMessage.Should().Contain("Invalid");
            _auth.IsLoggedIn.Should().BeFalse();
        }

        [Test]
        public void Login_EmptyUsername_ReturnsFail()
        {
            var result = _auth.Login("", "admin");

            result.Success.Should().BeFalse();
            result.ErrorMessage.Should().Contain("required");
        }

        [Test]
        public void Login_EmptyPassword_ReturnsFail()
        {
            var result = _auth.Login("ADMIN", "");

            result.Success.Should().BeFalse();
            result.ErrorMessage.Should().Contain("required");
        }

        [Test]
        public void Login_NonexistentUser_ReturnsFail()
        {
            var result = _auth.Login("NOBODY", "admin");

            result.Success.Should().BeFalse();
            result.ErrorMessage.Should().Contain("Invalid");
        }

        [Test]
        public void Login_InactiveUser_ReturnsFail()
        {
            SqlHelper.ExecuteNonQuery(_db,
                "UPDATE users SET is_active = 0 WHERE username = 'ADMIN'");

            var result = _auth.Login("ADMIN", "admin");

            result.Success.Should().BeFalse();
        }

        [Test]
        public void Login_ThreeFailedAttempts_LocksOut()
        {
            _auth.Login("ADMIN", "wrong1");
            _auth.Login("ADMIN", "wrong2");
            _auth.Login("ADMIN", "wrong3");

            _auth.IsLockedOut.Should().BeTrue();

            var result = _auth.Login("ADMIN", "admin");
            result.Success.Should().BeFalse();
            result.ErrorMessage.Should().Contain("locked");
        }

        [Test]
        public void Logout_ClearsCurrentUser()
        {
            _auth.Login("ADMIN", "admin");
            _auth.IsLoggedIn.Should().BeTrue();

            _auth.Logout();
            _auth.IsLoggedIn.Should().BeFalse();
            _auth.CurrentUser.Should().BeNull();
        }

        [Test]
        public void HashPassword_ProducesBCryptHash()
        {
            string hash = AuthService.HashPassword("test123");

            hash.Should().StartWith("$2a$10$");
            hash.Length.Should().Be(60);
        }

        [Test]
        public void VerifyPassword_CorrectPassword_ReturnsTrue()
        {
            string hash = AuthService.HashPassword("mypassword");
            AuthService.VerifyPassword("mypassword", hash).Should().BeTrue();
        }

        [Test]
        public void VerifyPassword_WrongPassword_ReturnsFalse()
        {
            string hash = AuthService.HashPassword("mypassword");
            AuthService.VerifyPassword("notmypassword", hash).Should().BeFalse();
        }

        [Test]
        public void VerifyPassword_InvalidHash_ReturnsFalse()
        {
            AuthService.VerifyPassword("password", "not-a-valid-hash").Should().BeFalse();
        }

        [Test]
        public void HashPassword_TakesReasonableTime()
        {
            var sw = Stopwatch.StartNew();
            AuthService.HashPassword("testpassword");
            sw.Stop();

            // BCrypt cost 10 should take >50ms on modern hardware
            sw.ElapsedMilliseconds.Should().BeGreaterThan(10);
        }

        private static void SeedTestUser(SqliteConnection db)
        {
            // Seed roles
            SqlHelper.ExecuteNonQuery(db,
                @"INSERT OR IGNORE INTO roles (id, name, permissions) VALUES
                  (1, 'admin', '{""all"": true}'),
                  (2, 'supervisor', '{""pos"": true}'),
                  (3, 'cashier', '{""pos"": true}')");

            // Seed admin user with known BCrypt hash for "admin"
            string hash = AuthService.HashPassword("admin");
            SqlHelper.ExecuteNonQuery(db,
                @"INSERT OR IGNORE INTO users (id, username, password_hash, password_salt, display_name, alias, role_id, is_active)
                  VALUES (1, 'ADMIN', @hash, '', 'Administrator', 'ADM', 1, 1)",
                SqlHelper.Param("@hash", hash));
        }
    }
}
