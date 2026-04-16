using System;
using System.Drawing;
using System.Windows.Forms;
using Kasir.Auth;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Forms.Shared;
using Kasir.Models;

namespace Kasir.Forms.Admin
{
    public class UserForm : BaseForm
    {
        private DataGridView dgvUsers;
        private UserRepository _userRepo;
        private RoleRepository _roleRepo;

        public UserForm()
        {
            var conn = DbConnection.GetConnection();
            _userRepo = new UserRepository(conn);
            _roleRepo = new RoleRepository(conn);
            InitializeLayout();
            SetAction("User Management — Ins: Add, Enter: Edit, Del: Delete, P: Password");
            LoadData();
        }

        private void InitializeLayout()
        {
            dgvUsers = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true
            };
            ApplyGridTheme(dgvUsers);

            dgvUsers.Columns.Add("Username", "Username");
            dgvUsers.Columns.Add("DisplayName", "Display Name");
            dgvUsers.Columns.Add("Alias", "Alias");
            dgvUsers.Columns.Add("Role", "Role");
            dgvUsers.Columns.Add("Active", "Active");

            dgvUsers.Columns["Username"].FillWeight = 150;
            dgvUsers.Columns["DisplayName"].FillWeight = 250;
            dgvUsers.Columns["Alias"].FillWeight = 60;
            dgvUsers.Columns["Role"].FillWeight = 120;
            dgvUsers.Columns["Active"].FillWeight = 60;

            this.Controls.Add(dgvUsers);
        }

        private void LoadData()
        {
            dgvUsers.Rows.Clear();
            var users = _userRepo.GetAll();
            var roles = _roleRepo.GetAll();

            foreach (var user in users)
            {
                string roleName = "Unknown";
                foreach (var role in roles)
                {
                    if (role.Id == user.RoleId)
                    {
                        roleName = role.Name;
                        break;
                    }
                }

                dgvUsers.Rows.Add(
                    user.Username,
                    user.DisplayName,
                    user.Alias,
                    roleName,
                    user.IsActive == 1 ? "Yes" : "No");
                dgvUsers.Rows[dgvUsers.Rows.Count - 1].Tag = user;
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Insert:
                    AddUser();
                    return true;
                case Keys.Enter:
                    EditUser();
                    return true;
                case Keys.Delete:
                    DeleteUser();
                    return true;
                case Keys.P:
                    ChangePassword();
                    return true;
                case Keys.Escape:
                    this.Close();
                    return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void AddUser()
        {
            using (var dlg = new InputDialog("Add User",
                new[] { "Username", "Display Name", "Alias (3 chars)", "Password", "Role ID (1=admin, 2=supervisor, 3=cashier)" },
                new[] { "", "", "", "", "3" }))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    var user = new User
                    {
                        Username = dlg.Values[0].ToUpper(),
                        DisplayName = dlg.Values[1],
                        Alias = dlg.Values[2],
                        PasswordHash = AuthService.HashPassword(dlg.Values[3]),
                        PasswordSalt = "",
                        RoleId = int.Parse(dlg.Values[4]),
                        IsActive = 1
                    };

                    _userRepo.Insert(user);
                    LoadData();
                }
            }
        }

        private void EditUser()
        {
            if (dgvUsers.CurrentRow == null) return;
            var user = dgvUsers.CurrentRow.Tag as User;
            if (user == null) return;

            using (var dlg = new InputDialog("Edit User",
                new[] { "Display Name", "Alias (3 chars)", "Role ID (1=admin, 2=supervisor, 3=cashier)", "Active (1=yes, 0=no)" },
                new[] { user.DisplayName, user.Alias, user.RoleId.ToString(), user.IsActive.ToString() }))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    user.DisplayName = dlg.Values[0];
                    user.Alias = dlg.Values[1];
                    user.RoleId = int.Parse(dlg.Values[2]);
                    user.IsActive = int.Parse(dlg.Values[3]);

                    _userRepo.Update(user);
                    LoadData();
                }
            }
        }

        private void DeleteUser()
        {
            if (dgvUsers.CurrentRow == null) return;
            var user = dgvUsers.CurrentRow.Tag as User;
            if (user == null) return;

            if (user.Id == 1)
            {
                MessageBox.Show("Cannot delete the main admin user.", "Error");
                return;
            }

            if (MessageBox.Show(
                string.Format("Delete user {0}?", user.Username),
                "Confirm", MessageBoxButtons.YesNo) == DialogResult.Yes)
            {
                _userRepo.Delete(user.Id);
                LoadData();
            }
        }

        private void ChangePassword()
        {
            if (dgvUsers.CurrentRow == null) return;
            var user = dgvUsers.CurrentRow.Tag as User;
            if (user == null) return;

            string newPassword = InputDialog.ShowSingleInput(
                this, "Change Password", "New password for " + user.Username, "");

            if (!string.IsNullOrEmpty(newPassword))
            {
                string hash = AuthService.HashPassword(newPassword);
                _userRepo.UpdatePassword(user.Id, hash, "");
                MessageBox.Show("Password changed.", "Success");
            }
        }
    }
}
