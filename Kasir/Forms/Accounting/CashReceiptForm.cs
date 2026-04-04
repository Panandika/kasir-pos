using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Models;
using Kasir.Utils;

namespace Kasir.Forms.Accounting
{
    public class CashReceiptForm : BaseForm
    {
        private TextBox txtDate;
        private TextBox txtRemark;
        private DataGridView dgvLines;
        private Label lblTotal;
        private CashTransactionRepository _cashTxnRepo;
        private CounterRepository _counterRepo;
        private AccountRepository _accountRepo;
        private bool _isBankMode;

        public CashReceiptForm(bool bankMode = false)
        {
            _isBankMode = bankMode;
            var conn = DbConnection.GetConnection();
            _cashTxnRepo = new CashTransactionRepository(conn);
            _counterRepo = new CounterRepository(conn);
            _accountRepo = new AccountRepository(conn);
            InitializeLayout();
            string title = _isBankMode ? "Penerimaan Bank" : "Penerimaan Kas";
            SetAction(title + " — F5: Simpan, Ins: Tambah Baris, Esc: Keluar");
        }

        private void InitializeLayout()
        {
            var pnlHeader = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = Color.FromArgb(0, 30, 0) };
            var lblDate = new Label { Text = "Tanggal:", Location = new Point(5, 8), AutoSize = true, ForeColor = Color.Gray };
            txtDate = new TextBox
            {
                Location = new Point(80, 5), Width = 120, Text = DateTime.Now.ToString("yyyy-MM-dd"),
                BackColor = Color.FromArgb(20, 20, 20), ForeColor = Color.FromArgb(0, 255, 0),
                Font = new Font("Consolas", 12f)
            };
            var lblRemark = new Label { Text = "Ket:", Location = new Point(220, 8), AutoSize = true, ForeColor = Color.Gray };
            txtRemark = new TextBox
            {
                Location = new Point(260, 5), Width = 400,
                BackColor = Color.FromArgb(20, 20, 20), ForeColor = Color.FromArgb(0, 255, 0),
                Font = new Font("Consolas", 12f)
            };
            pnlHeader.Controls.AddRange(new Control[] { lblDate, txtDate, lblRemark, txtRemark });

            dgvLines = new DataGridView { Dock = DockStyle.Fill };
            ApplyGridTheme(dgvLines);
            dgvLines.ReadOnly = false;

            dgvLines.Columns.Add("AccCode", "Kode Akun");
            dgvLines.Columns.Add("AccName", "Nama Akun");
            dgvLines.Columns.Add("Amount", "Jumlah");
            dgvLines.Columns.Add("Remark", "Keterangan");

            dgvLines.Columns["AccCode"].Width = 120;
            dgvLines.Columns["AccName"].Width = 250;
            dgvLines.Columns["Amount"].Width = 150;
            dgvLines.Columns["Remark"].Width = 250;

            dgvLines.CellEndEdit += (s, e) =>
            {
                if (e.ColumnIndex == 0)
                {
                    string code = dgvLines.Rows[e.RowIndex].Cells["AccCode"].Value?.ToString();
                    if (!string.IsNullOrEmpty(code))
                    {
                        var acc = _accountRepo.GetByCode(code);
                        if (acc != null)
                            dgvLines.Rows[e.RowIndex].Cells["AccName"].Value = acc.AccountName;
                    }
                }
                UpdateTotal();
            };

            var pnlBottom = new Panel { Dock = DockStyle.Bottom, Height = 35, BackColor = Color.FromArgb(0, 30, 0) };
            lblTotal = new Label { Text = "Total: 0", Location = new Point(5, 8), Width = 300, ForeColor = Color.Cyan };
            pnlBottom.Controls.Add(lblTotal);

            this.Controls.Add(dgvLines);
            this.Controls.Add(pnlBottom);
            this.Controls.Add(pnlHeader);
        }

        private void UpdateTotal()
        {
            long total = 0;
            foreach (DataGridViewRow row in dgvLines.Rows)
            {
                if (row.IsNewRow) continue;
                long val;
                long.TryParse(row.Cells["Amount"].Value?.ToString() ?? "0", out val);
                total += val;
            }
            lblTotal.Text = "Total: " + Formatting.FormatMoney(total);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.F5:
                    SaveTransaction();
                    return true;
                case Keys.Insert:
                    dgvLines.Rows.Add();
                    return true;
                case Keys.Escape:
                    this.Close();
                    return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void SaveTransaction()
        {
            var lines = new List<CashTransactionLine>();
            long total = 0;

            foreach (DataGridViewRow row in dgvLines.Rows)
            {
                if (row.IsNewRow) continue;
                string accCode = row.Cells["AccCode"].Value?.ToString();
                if (string.IsNullOrEmpty(accCode)) continue;

                long val;
                long.TryParse(row.Cells["Amount"].Value?.ToString() ?? "0", out val);
                if (val <= 0) continue;

                lines.Add(new CashTransactionLine
                {
                    AccountCode = accCode,
                    Direction = "K",
                    Value = val,
                    Remark = row.Cells["Remark"].Value?.ToString() ?? ""
                });
                total += val;
            }

            if (lines.Count == 0) return;

            try
            {
                string docType = _isBankMode ? "BANK_IN" : "CASH_IN";
                string prefix = _isBankMode ? "BMS" : "KMS";
                string jnl = _counterRepo.GetNext(prefix, "01");
                string periodCode = txtDate.Text.Substring(0, 4) + txtDate.Text.Substring(5, 2);

                _cashTxnRepo.Insert(new CashTransaction
                {
                    DocType = docType,
                    JournalNo = jnl,
                    DocDate = txtDate.Text.Trim(),
                    Remark = txtRemark.Text.Trim(),
                    TotalValue = total,
                    Control = 1,
                    PeriodCode = periodCode,
                    RegisterId = "01",
                    ChangedBy = 1
                }, lines);

                MessageBox.Show("Transaksi " + jnl + " tersimpan.", "Sukses",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                dgvLines.Rows.Clear();
                txtRemark.Text = "";
                UpdateTotal();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Gagal: " + ex.Message, "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
