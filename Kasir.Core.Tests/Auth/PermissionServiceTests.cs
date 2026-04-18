using NUnit.Framework;
using FluentAssertions;
using Kasir.Auth;
using Kasir.Models;

namespace Kasir.Tests.Auth
{
    [TestFixture]
    public class PermissionServiceTests
    {
        private PermissionService _perms;

        [SetUp]
        public void SetUp()
        {
            _perms = new PermissionService();
        }

        [Test]
        public void Admin_HasAllPermissions()
        {
            var admin = new User { RoleId = 1 };

            _perms.HasPermission(admin, "pos").Should().BeTrue();
            _perms.HasPermission(admin, "master").Should().BeTrue();
            _perms.HasPermission(admin, "reports").Should().BeTrue();
            _perms.HasPermission(admin, "utility.backup").Should().BeTrue();
            _perms.HasPermission(admin, "anything").Should().BeTrue();
        }

        [Test]
        public void Supervisor_HasMasterAndPosAndReports()
        {
            var supervisor = new User { RoleId = 2 };

            _perms.HasPermission(supervisor, "pos").Should().BeTrue();
            _perms.HasPermission(supervisor, "master").Should().BeTrue();
            _perms.HasPermission(supervisor, "master.department").Should().BeTrue();
            _perms.HasPermission(supervisor, "master.product").Should().BeTrue();
            _perms.HasPermission(supervisor, "reports").Should().BeTrue();
            _perms.HasPermission(supervisor, "reports.sales").Should().BeTrue();
            _perms.HasPermission(supervisor, "utility.backup").Should().BeTrue();
        }

        [Test]
        public void Supervisor_CannotAccessUserManagement()
        {
            var supervisor = new User { RoleId = 2 };

            _perms.HasPermission(supervisor, "utility.users").Should().BeFalse();
        }

        [Test]
        public void Cashier_CanOnlyAccessPosAndSalesReports()
        {
            var cashier = new User { RoleId = 3 };

            _perms.HasPermission(cashier, "pos").Should().BeTrue();
            _perms.HasPermission(cashier, "transaction.sales").Should().BeTrue();
            _perms.HasPermission(cashier, "reports.sales").Should().BeTrue();
        }

        [Test]
        public void Cashier_CannotAccessMasterOrAdmin()
        {
            var cashier = new User { RoleId = 3 };

            _perms.HasPermission(cashier, "master").Should().BeFalse();
            _perms.HasPermission(cashier, "master.department").Should().BeFalse();
            _perms.HasPermission(cashier, "utility.backup").Should().BeFalse();
            _perms.HasPermission(cashier, "reports.purchase").Should().BeFalse();
        }

        [Test]
        public void NullUser_DeniesAllPermissions()
        {
            _perms.HasPermission(null, "pos").Should().BeFalse();
        }

        [Test]
        public void UnknownRole_DeniesAllPermissions()
        {
            var unknown = new User { RoleId = 99 };

            _perms.HasPermission(unknown, "pos").Should().BeFalse();
        }
    }
}
