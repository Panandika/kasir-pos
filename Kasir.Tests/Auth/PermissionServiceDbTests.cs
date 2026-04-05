using System.Data.SQLite;
using NUnit.Framework;
using FluentAssertions;
using Kasir.Auth;
using Kasir.Data;
using Kasir.Models;
using Kasir.Tests.TestHelpers;

namespace Kasir.Tests.Auth
{
    [TestFixture]
    public class PermissionServiceDbTests
    {
        private SQLiteConnection _db;

        [SetUp]
        public void SetUp()
        {
            _db = TestDb.Create();
        }

        [TearDown]
        public void TearDown()
        {
            _db.Close();
            _db.Dispose();
        }

        private void InsertRole(int id, string name, string permissionsJson)
        {
            SqlHelper.ExecuteNonQuery(_db,
                "INSERT INTO roles (id, name, permissions) VALUES (@id, @name, @perms)",
                SqlHelper.Param("@id", id),
                SqlHelper.Param("@name", name),
                SqlHelper.Param("@perms", permissionsJson));
        }

        [Test]
        public void DbDriven_Admin_Wildcard_AllowsEverything()
        {
            InsertRole(1, "admin", "[\"*\"]");
            var perms = new PermissionService(_db);
            var admin = new User { RoleId = 1 };

            perms.HasPermission(admin, "anything").Should().BeTrue();
            perms.HasPermission(admin, "pos").Should().BeTrue();
            perms.HasPermission(admin, "utility.users").Should().BeTrue();
        }

        [Test]
        public void DbDriven_Supervisor_AllowsConfiguredKeys()
        {
            InsertRole(2, "supervisor", "[\"pos\",\"master\",\"master.department\",\"reports.sales\"]");
            var perms = new PermissionService(_db);
            var supervisor = new User { RoleId = 2 };

            perms.HasPermission(supervisor, "pos").Should().BeTrue();
            perms.HasPermission(supervisor, "master").Should().BeTrue();
            perms.HasPermission(supervisor, "master.department").Should().BeTrue();
            perms.HasPermission(supervisor, "reports.sales").Should().BeTrue();
        }

        [Test]
        public void DbDriven_Supervisor_DeniesUnconfiguredKeys()
        {
            InsertRole(2, "supervisor", "[\"pos\",\"master\"]");
            var perms = new PermissionService(_db);
            var supervisor = new User { RoleId = 2 };

            perms.HasPermission(supervisor, "utility.users").Should().BeFalse();
        }

        [Test]
        public void DbDriven_Cashier_OnlyPosAndSales()
        {
            InsertRole(3, "cashier", "[\"pos\",\"transaction.sales\",\"reports.sales\"]");
            var perms = new PermissionService(_db);
            var cashier = new User { RoleId = 3 };

            perms.HasPermission(cashier, "pos").Should().BeTrue();
            perms.HasPermission(cashier, "transaction.sales").Should().BeTrue();
            perms.HasPermission(cashier, "reports.sales").Should().BeTrue();
            perms.HasPermission(cashier, "master").Should().BeFalse();
            perms.HasPermission(cashier, "accounting").Should().BeFalse();
        }

        [Test]
        public void DbDriven_MalformedJson_DeniesPermission()
        {
            InsertRole(2, "supervisor", "not valid json!!!");
            var perms = new PermissionService(_db);
            var supervisor = new User { RoleId = 2 };

            // Malformed JSON means role has no permissions in DB
            perms.HasPermission(supervisor, "pos").Should().BeFalse();
        }

        [Test]
        public void DbDriven_NullPermissions_DeniesPermission()
        {
            SqlHelper.ExecuteNonQuery(_db,
                "INSERT INTO roles (id, name, permissions) VALUES (3, 'cashier', NULL)");
            var perms = new PermissionService(_db);
            var cashier = new User { RoleId = 3 };

            // NULL permissions means role has no permissions in DB
            perms.HasPermission(cashier, "pos").Should().BeFalse();
        }

        [Test]
        public void DbDriven_OldObjectFormat_StillWorks()
        {
            InsertRole(3, "cashier", "{\"pos\":true,\"transaction.sales\":true}");
            var perms = new PermissionService(_db);
            var cashier = new User { RoleId = 3 };

            perms.HasPermission(cashier, "pos").Should().BeTrue();
            perms.HasPermission(cashier, "transaction.sales").Should().BeTrue();
            perms.HasPermission(cashier, "master").Should().BeFalse();
        }

        [Test]
        public void DbDriven_OldObjectFormat_AllKey_MapsToWildcard()
        {
            InsertRole(1, "admin", "{\"all\":true}");
            var perms = new PermissionService(_db);
            // RoleId=1 is admin, always true via hardcoded fast path
            // Test with roleId 99 to exercise the DB path
            InsertRole(99, "custom", "{\"all\":true}");
            var custom = new User { RoleId = 99 };

            perms.HasPermission(custom, "anything").Should().BeTrue();
        }
    }
}
