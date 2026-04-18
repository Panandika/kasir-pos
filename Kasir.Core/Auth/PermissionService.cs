using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Kasir.Data;
using Kasir.Models;
using Newtonsoft.Json;

namespace Kasir.Auth
{
    public class PermissionService
    {
        private Dictionary<int, HashSet<string>> _rolePermissions;

        public PermissionService()
        {
            // Fallback: hardcoded permissions (backward compat for tests)
        }

        public PermissionService(SqliteConnection db)
        {
            LoadPermissionsFromDb(db);
        }

        private void LoadPermissionsFromDb(SqliteConnection db)
        {
            try
            {
                _rolePermissions = new Dictionary<int, HashSet<string>>();
                var roles = SqlHelper.Query(db,
                    "SELECT id, permissions FROM roles WHERE permissions IS NOT NULL",
                    r => new { Id = SqlHelper.GetInt(r, "id"), Perms = SqlHelper.GetString(r, "permissions") });

                foreach (var role in roles)
                {
                    if (string.IsNullOrWhiteSpace(role.Perms))
                    {
                        continue;
                    }

                    var perms = ParsePermissions(role.Perms);
                    if (perms != null)
                    {
                        _rolePermissions[role.Id] = perms;
                    }
                }
            }
            catch
            {
                _rolePermissions = null; // fallback to hardcoded
            }
        }

        private static HashSet<string> ParsePermissions(string json)
        {
            json = json.Trim();

            // Array format: ["pos","master","master.department"]
            if (json.StartsWith("["))
            {
                try
                {
                    var list = JsonConvert.DeserializeObject<List<string>>(json);
                    return list != null ? new HashSet<string>(list) : null;
                }
                catch
                {
                    return null;
                }
            }

            // Object format (legacy): {"pos":true,"master":true}
            if (json.StartsWith("{"))
            {
                try
                {
                    var dict = JsonConvert.DeserializeObject<Dictionary<string, bool>>(json);
                    if (dict == null) return null;
                    var set = new HashSet<string>();
                    foreach (var kv in dict)
                    {
                        if (kv.Value)
                        {
                            set.Add(kv.Key);
                        }
                    }
                    // Legacy "all" key maps to wildcard
                    if (set.Contains("all"))
                    {
                        set.Remove("all");
                        set.Add("*");
                    }
                    return set;
                }
                catch
                {
                    return null;
                }
            }

            return null;
        }

        public bool HasPermission(User user, string permissionKey)
        {
            if (user == null) return false;

            // Admin can do everything (hardcoded fast path)
            if (user.RoleId == 1) return true;

            // DB-driven permissions
            if (_rolePermissions != null)
            {
                HashSet<string> perms;
                if (_rolePermissions.TryGetValue(user.RoleId, out perms))
                {
                    return perms.Contains("*") || perms.Contains(permissionKey);
                }
                return false;
            }

            // Fallback: hardcoded
            switch (user.RoleId)
            {
                case 2:
                    return IsSupervisorPermission(permissionKey);
                case 3:
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
                case "accounting":
                case "bank":
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
