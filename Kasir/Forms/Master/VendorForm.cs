using System;
using System.Drawing;
using System.Windows.Forms;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Forms.Shared;
using Kasir.Models;

namespace Kasir.Forms.Master
{
    public class VendorForm : BaseForm
    {
        private DataGridView dgvVendors;
        private TextBox txtSearch;
        private SubsidiaryRepository _vendorRepo;
        private int _currentUserId;

        public VendorForm(int userId = 1)
        {
            _currentUserId = userId;
            _vendorRepo = new SubsidiaryRepository(DbConnection.GetConnection());
            InitializeLayout();
            SetAction("Input Data Supplier — F2: Search, Ins: Tambah, Enter: Ubah, Esc: Keluar");
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
                if (e.KeyCode == Keys.Enter) { SearchVendors(); e.Handled = true; e.SuppressKeyPress = true; }
            };
            ApplyFocusIndicator(txtSearch);
            pnlSearch.Controls.AddRange(new Control[] { lblSearch, txtSearch });

            dgvVendors = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true };
            ApplyGridTheme(dgvVendors);

            dgvVendors.Columns.Add("Code", "Kode");
            dgvVendors.Columns.Add("Name", "Nama Supplier");
            dgvVendors.Columns.Add("Address", "Alamat");
            dgvVendors.Columns.Add("City", "Kota");
            dgvVendors.Columns.Add("Phone", "Telepon");

            dgvVendors.Columns["Code"].FillWeight = 100;
            dgvVendors.Columns["Name"].FillWeight = 250;
            dgvVendors.Columns["Address"].FillWeight = 250;
            dgvVendors.Columns["City"].FillWeight = 120;
            dgvVendors.Columns["Phone"].FillWeight = 120;

            this.Controls.Add(dgvVendors);
            this.Controls.Add(pnlSearch);
        }

        private void LoadData()
        {
            dgvVendors.Rows.Clear();
            var vendors = _vendorRepo.GetAllByGroup("1", 500, 0);
            foreach (var v in vendors)
            {
                dgvVendors.Rows.Add(v.SubCode, v.Name, v.Address ?? "", v.City ?? "", v.Phone ?? "");
                dgvVendors.Rows[dgvVendors.Rows.Count - 1].Tag = v;
            }
        }

        private void SearchVendors()
        {
            string query = txtSearch.Text.Trim();
            if (string.IsNullOrEmpty(query)) { LoadData(); return; }

            dgvVendors.Rows.Clear();
            var results = _vendorRepo.SearchByName(query, "1", 100);
            foreach (var v in results)
            {
                dgvVendors.Rows.Add(v.SubCode, v.Name, v.Address ?? "", v.City ?? "", v.Phone ?? "");
                dgvVendors.Rows[dgvVendors.Rows.Count - 1].Tag = v;
            }
            SetAction(string.Format("Found {0} suppliers", results.Count));
        }

        private void AddVendor()
        {
            using (var dlg = new InputDialog("Tambah Supplier",
                new[] { "Kode", "Nama", "Alamat", "Kota" },
                new[] { "", "", "", "" }))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    var vendor = new Subsidiary
                    {
                        SubCode = dlg.Values[0],
                        Name = dlg.Values[1],
                        Address = dlg.Values[2],
                        City = dlg.Values[3],
                        GroupCode = "1",
                        Status = "A",
                        ChangedBy = _currentUserId
                    };
                    _vendorRepo.Insert(vendor);
                    LoadData();
                }
            }
        }

        private void EditVendor()
        {
            if (dgvVendors.CurrentRow == null) return;
            var vendor = dgvVendors.CurrentRow.Tag as Subsidiary;
            if (vendor == null) return;

            using (var dlg = new InputDialog("Ubah Supplier",
                new[] { "Nama", "Alamat", "Kota", "Telepon" },
                new[] { vendor.Name, vendor.Address ?? "", vendor.City ?? "", vendor.Phone ?? "" }))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    vendor.Name = dlg.Values[0];
                    vendor.Address = dlg.Values[1];
                    vendor.City = dlg.Values[2];
                    vendor.Phone = dlg.Values[3];
                    vendor.ChangedBy = _currentUserId;
                    _vendorRepo.Update(vendor);
                    LoadData();
                }
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.F2: txtSearch.Focus(); return true;
                case Keys.Insert: AddVendor(); return true;
                case Keys.Enter: EditVendor(); return true;
                case Keys.Escape: this.Close(); return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
