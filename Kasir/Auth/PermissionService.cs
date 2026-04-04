using Kasir.Models;

namespace Kasir.Auth
{
    public class PermissionService
    {
        // Phase 1: hard-coded permission map for 3 roles
        // Phase 2: parse JSON from Role.Permissions

        public bool HasPermission(User user, string permissionKey)
        {
            if (user == null) return false;

            // Admin can do everything
            if (user.RoleId == 1) return true;

            switch (user.RoleId)
            {
                case 2: // supervisor
                    return IsSupervisorPermission(permissionKey);
                case 3: // cashier
                    return IsCashierPermission(permissionKey);
                default:
                    return false;
            }
        }

        public bool CanAccessMenu(User user, string menuItem)
        {
            return HasPermission(user, menuItem);
        }

        private static bool IsSupervisorPermission(string key)
        {
            switch (key)
            {
                case "pos":
                case "master":
                case "master.department":
                case "master.supplier":
                case "master.product":
                case "master.credit_card":
                case "master.price_change":
                case "master.stock_opname":
                case "transaction":
                case "transaction.purchase":
                case "transaction.sales":
                case "transaction.return":
                case "transaction.transfer":
                case "transaction.stock_out":
                case "reports":
                case "reports.sales":
                case "reports.purchase":
                case "reports.stock":
                case "reports.master":
                case "inventory":
                case "utility.backup":
                case "utility.printer":
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsCashierPermission(string key)
        {
            switch (key)
            {
                case "pos":
                case "transaction.sales":
                case "reports.sales":
                    return true;
                default:
                    return false;
            }
        }
    }
}
