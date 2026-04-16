using System;
using System.Drawing;
using System.Windows.Forms;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Forms.Shared;
using Kasir.Models;

namespace Kasir.Forms.Accounting
{
    public class AccountsForm : BaseForm
    {
        private DataGridView dgvAccounts;
        private TextBox txtSearch;
        private AccountRepository _accountRepo;
        private bool _readOnly;

        public AccountsForm(bool readOnly = false)
        {
            _readOnly = readOnly;
            _accountRepo = new AccountRepository(DbConnection.GetConnection());
            InitializeLayout();
            string action = _readOnly
                ? "Informasi Perkiraan — F2: Search, Esc: Keluar"
                : "Daftar Perkiraan — F2: Search, Ins: Tambah, Enter: Ubah, Esc: Keluar";
            SetAction(action);
            LoadData();
        }

        private void InitializeLayout()
        {
            var pnlSearch = new Panel { Dock = DockStyle.Top, Height = 35, BackColor = ThemeConstants.BgFooter };
            var lblSearch = new Label { Text = "Search:", Location = new Point(5, 8), AutoSize = true, ForeColor = ThemeConstants.FgLabel };
            txtSearch = new TextBox
            {
                Location = new Point(80, 5),
                Width = 300,
                BackColor = ThemeConstants.BgInput,
                ForeColor = ThemeConstants.FgPrimary,
                Font = ThemeConstants.FontInputSmall
            };
            txtSearch.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { SearchAccounts(); e.Handled = true; e.SuppressKeyPress = true; }
            };
            ApplyFocusIndicator(txtSearch);
            pnlSearch.Controls.AddRange(new Control[] { lblSearch, txtSearch });

            dgvAccounts = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true };
            ApplyGridTheme(dgvAccounts);

            dgvAccounts.Columns.Add("Code", "Kode");
            dgvAccounts.Columns.Add("Name", "Nama Perkiraan");
            dgvAccounts.Columns.Add("Group", "Grup");
            dgvAccounts.Columns.Add("NormalBal", "D/K");
            dgvAccounts.Columns.Add("Detail", "Detail");

            dgvAccounts.Columns["Code"].FillWeight = 120;
            dgvAccounts.Columns["Name"].FillWeight = 300;
            dgvAccounts.Columns["Group"].FillWeight = 100;
            dgvAccounts.Columns["NormalBal"].FillWeight = 60;
            dgvAccounts.Columns["Detail"].FillWeight = 60;

            this.Controls.Add(dgvAccounts);
            this.Controls.Add(pnlSearch);
        }

        private void LoadData()
        {
            dgvAccounts.Rows.Clear();
            var accounts = _accountRepo.GetAll();
            foreach (var a in accounts)
            {
                string group = GetGroupName(a.AccountGroup);
                string indent = new string(' ', a.Level * 2);
                dgvAccounts.Rows.Add(a.AccountCode, indent + a.AccountName, group,
                    a.NormalBalance, a.IsDetail == 1 ? "Ya" : "");
                dgvAccounts.Rows[dgvAccounts.Rows.Count - 1].Tag = a;
            }
        }

        private void SearchAccounts()
        {
            string query = txtSearch.Text.Trim();
            if (string.IsNullOrEmpty(query)) { LoadData(); return; }

            dgvAccounts.Rows.Clear();
            var results = _accountRepo.Search(query);
            foreach (var a in results)
            {
                string group = GetGroupName(a.AccountGroup);
                dgvAccounts.Rows.Add(a.AccountCode, a.AccountName, group,
                    a.NormalBalance, a.IsDetail == 1 ? "Ya" : "");
                dgvAccounts.Rows[dgvAccounts.Rows.Count - 1].Tag = a;
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.F2:
                    txtSearch.Focus();
                    return true;

                case Keys.Insert:
                    if (!_readOnly) ShowAddDialog();
                    return true;

                case Keys.Enter:
                    if (!_readOnly && dgvAccounts.CurrentRow != null)
                        ShowEditDialog(dgvAccounts.CurrentRow.Tag as Account);
                    return true;

                case Keys.Escape:
                    this.Close();
                    return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void ShowAddDialog()
        {
            var dialog = new InputDialog("Tambah Perkiraan", new[]
            {
                "Kode Perkiraan", "Nama", "Induk (kosong=root)", "Grup (1-5)", "D/K"
            }, new[] { "", "", "", "", "D" });

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                var values = dialog.Values;
                int group;
                int.TryParse(values[3], out group);

                _accountRepo.Insert(new Account
                {
                    AccountCode = values[0],
                    AccountName = values[1],
                    ParentCode = values[2],
                    AccountGroup = group,
                    NormalBalance = string.IsNullOrEmpty(values[4]) ? "D" : values[4].ToUpper(),
                    IsDetail = 1
                });
                LoadData();
            }
        }

        private void ShowEditDialog(Account account)
        {
            if (account == null) return;

            var dialog = new InputDialog("Ubah Perkiraan", new[]
            {
                "Nama", "Induk", "Grup (1-5)", "D/K"
            }, new[]
            {
                account.AccountName, account.ParentCode ?? "",
                account.AccountGroup.ToString(), account.NormalBalance
            });

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                var values = dialog.Values;
                int group;
                int.TryParse(values[2], out group);

                account.AccountName = values[0];
                account.ParentCode = values[1];
                account.AccountGroup = group;
                account.NormalBalance = string.IsNullOrEmpty(values[3]) ? "D" : values[3].ToUpper();
                _accountRepo.Update(account);
                LoadData();
            }
        }

        private static string GetGroupName(int group)
        {
            switch (group)
            {
                case 1: return "Aktiva";
                case 2: return "Kewajiban";
                case 3: return "Modal";
                case 4: return "Pendapatan";
                case 5: return "Biaya";
                default: return "";
            }
        }
    }
}
