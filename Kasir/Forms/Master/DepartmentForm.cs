using System;
using System.Drawing;
using System.Windows.Forms;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Forms.Shared;
using Kasir.Models;
using Kasir.Utils;

namespace Kasir.Forms.Master
{
    public class DepartmentForm : BaseForm
    {
        private DataGridView dgvDepts;
        private DepartmentRepository _deptRepo;

        public DepartmentForm()
        {
            _deptRepo = new DepartmentRepository(DbConnection.GetConnection());
            InitializeLayout();
            SetAction("Input Label Departemen — Ins: Tambah, Enter: Ubah, Del: Hapus, Esc: Keluar");
            LoadData();
        }

        private void InitializeLayout()
        {
            dgvDepts = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true
            };
            ApplyGridTheme(dgvDepts);

            dgvDepts.Columns.Add("DeptCode", "Kode Dept");
            dgvDepts.Columns.Add("Name", "Nama Dept");
            dgvDepts.Columns.Add("ChangedAt", "Diubah");

            dgvDepts.Columns["DeptCode"].Width = 120;
            dgvDepts.Columns["Name"].Width = 400;
            dgvDepts.Columns["ChangedAt"].Width = 200;

            this.Controls.Add(dgvDepts);
        }

        private void LoadData()
        {
            dgvDepts.Rows.Clear();
            var depts = _deptRepo.GetAll();

            foreach (var dept in depts)
            {
                dgvDepts.Rows.Add(
                    dept.DeptCode,
                    dept.Name,
                    Formatting.FormatDate(dept.ChangedAt));
                dgvDepts.Rows[dgvDepts.Rows.Count - 1].Tag = dept;
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Insert:
                    AddDepartment();
                    return true;
                case Keys.Enter:
                    EditDepartment();
                    return true;
                case Keys.Delete:
                    DeleteDepartment();
                    return true;
                case Keys.Escape:
                    this.Close();
                    return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void AddDepartment()
        {
            using (var dlg = new InputDialog("Tambah Departemen",
                new[] { "Kode Dept (max 6 chars)", "Nama Dept" },
                new[] { "", "" }))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    string code = dlg.Values[0];
                    string name = dlg.Values[1];

                    if (!Validators.IsValidDeptCode(code))
                    {
                        MessageBox.Show("Kode dept harus 1-6 karakter.", "Error");
                        return;
                    }

                    if (string.IsNullOrWhiteSpace(name))
                    {
                        MessageBox.Show("Nama dept tidak boleh kosong.", "Error");
                        return;
                    }

                    // Check duplicate
                    if (_deptRepo.GetByCode(code) != null)
                    {
                        MessageBox.Show("Kode dept sudah ada.", "Error");
                        return;
                    }

                    var dept = new Department
                    {
                        DeptCode = code,
                        Name = name,
                        ChangedBy = 1 // TODO: get from current user
                    };

                    _deptRepo.Insert(dept);
                    LoadData();
                }
            }
        }

        private void EditDepartment()
        {
            if (dgvDepts.CurrentRow == null) return;
            var dept = dgvDepts.CurrentRow.Tag as Department;
            if (dept == null) return;

            using (var dlg = new InputDialog("Ubah Departemen",
                new[] { "Kode Dept", "Nama Dept" },
                new[] { dept.DeptCode, dept.Name }))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    string name = dlg.Values[1];

                    if (string.IsNullOrWhiteSpace(name))
                    {
                        MessageBox.Show("Nama dept tidak boleh kosong.", "Error");
                        return;
                    }

                    dept.Name = name;
                    dept.ChangedBy = 1; // TODO: get from current user

                    _deptRepo.Update(dept);
                    LoadData();
                }
            }
        }

        private void DeleteDepartment()
        {
            if (dgvDepts.CurrentRow == null) return;
            var dept = dgvDepts.CurrentRow.Tag as Department;
            if (dept == null) return;

            if (MessageBox.Show(
                string.Format("Hapus departemen {0} - {1}?", dept.DeptCode, dept.Name),
                "Konfirmasi", MessageBoxButtons.YesNo) == DialogResult.Yes)
            {
                _deptRepo.Delete(dept.Id);
                LoadData();
            }
        }
    }
}
