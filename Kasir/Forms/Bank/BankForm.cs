using System;
using System.Drawing;
using System.Windows.Forms;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Forms.Shared;
using Kasir.Models;

namespace Kasir.Forms.Bank
{
    public class BankForm : BaseForm
    {
        private DataGridView dgvBanks;
        private SubsidiaryRepository _bankRepo;

        public BankForm()
        {
            _bankRepo = new SubsidiaryRepository(DbConnection.GetConnection());
            InitializeLayout();
            SetAction("Data Bank — Ins: Tambah, Enter: Ubah, Esc: Keluar");
            LoadData();
        }

        private void InitializeLayout()
        {
            dgvBanks = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true };
            ApplyGridTheme(dgvBanks);

            dgvBanks.Columns.Add("Code", "Kode");
            dgvBanks.Columns.Add("Name", "Nama Bank");
            dgvBanks.Columns.Add("AccNo", "No. Rekening");
            dgvBanks.Columns.Add("Branch", "Cabang");
            dgvBanks.Columns.Add("Holder", "Atas Nama");

            dgvBanks.Columns["Code"].Width = 100;
            dgvBanks.Columns["Name"].Width = 200;
            dgvBanks.Columns["AccNo"].Width = 180;
            dgvBanks.Columns["Branch"].Width = 150;
            dgvBanks.Columns["Holder"].Width = 180;

            this.Controls.Add(dgvBanks);
        }

        private void LoadData()
        {
            dgvBanks.Rows.Clear();
            var banks = _bankRepo.GetAllByGroup("3", 500, 0); // group '3' = banks
            foreach (var b in banks)
            {
                dgvBanks.Rows.Add(b.SubCode, b.BankName ?? b.Name,
                    b.BankAccountNo ?? "", b.BankBranch ?? "", b.BankHolder ?? "");
                dgvBanks.Rows[dgvBanks.Rows.Count - 1].Tag = b;
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Insert:
                    ShowAddDialog();
                    return true;
                case Keys.Enter:
                    if (dgvBanks.CurrentRow != null)
                        ShowEditDialog(dgvBanks.CurrentRow.Tag as Subsidiary);
                    return true;
                case Keys.Escape:
                    this.Close();
                    return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void ShowAddDialog()
        {
            var dialog = new InputDialog("Tambah Bank", new[]
            {
                "Kode Bank", "Nama Bank", "No. Rekening", "Cabang", "Atas Nama"
            });

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                var v = dialog.Values;
                _bankRepo.Insert(new Subsidiary
                {
                    SubCode = v[0],
                    Name = v[1],
                    BankName = v[1],
                    BankAccountNo = v[2],
                    BankBranch = v[3],
                    BankHolder = v[4],
                    GroupCode = "3",
                    Status = "A"
                });
                LoadData();
            }
        }

        private void ShowEditDialog(Subsidiary bank)
        {
            if (bank == null) return;

            var dialog = new InputDialog("Ubah Bank", new[]
            {
                "Nama Bank", "No. Rekening", "Cabang", "Atas Nama"
            }, new[]
            {
                bank.BankName ?? bank.Name, bank.BankAccountNo ?? "",
                bank.BankBranch ?? "", bank.BankHolder ?? ""
            });

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                var v = dialog.Values;
                bank.Name = v[0];
                bank.BankName = v[0];
                bank.BankAccountNo = v[1];
                bank.BankBranch = v[2];
                bank.BankHolder = v[3];
                _bankRepo.Update(bank);
                LoadData();
            }
        }
    }
}
