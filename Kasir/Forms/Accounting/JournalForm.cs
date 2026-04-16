using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Models;
using Kasir.Services;
using Kasir.Utils;

namespace Kasir.Forms.Accounting
{
    public class JournalForm : BaseForm
    {
        private TextBox txtDate;
        private TextBox txtRemark;
        private DataGridView dgvLines;
        private Label lblDebitTotal;
        private Label lblCreditTotal;
        private Label lblDifference;
        private AccountingService _accountingService;
        private AccountRepository _accountRepo;
        private bool _readOnly;
        private int _currentUserId;

        public JournalForm(bool readOnly = false, int userId = 1)
        {
            _readOnly = readOnly;
            _currentUserId = userId;
            var conn = DbConnection.GetConnection();
            _accountingService = new AccountingService(conn);
            _accountRepo = new AccountRepository(conn);
            InitializeLayout();
            string action = _readOnly
                ? "Informasi Jurnal — Esc: Keluar"
                : "Jurnal Memorial — F5: Simpan, Ins: Tambah Baris, Del: Hapus Baris, Esc: Keluar";
            SetAction(action);
        }

        private void InitializeLayout()
        {
            var pnlHeader = new Panel { Dock = DockStyle.Top, Height = 70, BackColor = ThemeConstants.BgFooter };

            var lblDate = new Label { Text = "Tanggal:", Location = new Point(5, 8), AutoSize = true, ForeColor = ThemeConstants.FgLabel };
            txtDate = new TextBox
            {
                Location = new Point(80, 5), Width = 120, Text = DateTime.Now.ToString("yyyy-MM-dd"),
                BackColor = ThemeConstants.BgInput, ForeColor = ThemeConstants.FgPrimary,
                Font = ThemeConstants.FontInputSmall, ReadOnly = _readOnly
            };

            var lblRemark = new Label { Text = "Ket:", Location = new Point(220, 8), AutoSize = true, ForeColor = ThemeConstants.FgLabel };
            txtRemark = new TextBox
            {
                Location = new Point(260, 5), Width = 500,
                BackColor = ThemeConstants.BgInput, ForeColor = ThemeConstants.FgPrimary,
                Font = ThemeConstants.FontInputSmall, ReadOnly = _readOnly
            };

            lblDebitTotal = new Label { Text = "Debit: 0", Location = new Point(5, 40), Width = 250, ForeColor = ThemeConstants.FgSuccess };
            lblCreditTotal = new Label { Text = "Credit: 0", Location = new Point(260, 40), Width = 250, ForeColor = ThemeConstants.FgSuccess };
            lblDifference = new Label { Text = "Selisih: 0", Location = new Point(520, 40), Width = 250, ForeColor = ThemeConstants.FgWarning };

            pnlHeader.Controls.AddRange(new Control[] { lblDate, txtDate, lblRemark, txtRemark,
                lblDebitTotal, lblCreditTotal, lblDifference });

            dgvLines = new DataGridView { Dock = DockStyle.Fill, ReadOnly = _readOnly };
            ApplyGridTheme(dgvLines);

            dgvLines.Columns.Add("AccCode", "Kode Akun");
            dgvLines.Columns.Add("AccName", "Nama Akun");
            dgvLines.Columns.Add("Remark", "Keterangan");
            dgvLines.Columns.Add("Debit", "Debit");
            dgvLines.Columns.Add("Credit", "Kredit");

            dgvLines.Columns["AccCode"].FillWeight = 120;
            dgvLines.Columns["AccName"].FillWeight = 250;
            dgvLines.Columns["Remark"].FillWeight = 200;
            dgvLines.Columns["Debit"].FillWeight = 150;
            dgvLines.Columns["Credit"].FillWeight = 150;

            if (!_readOnly)
            {
                dgvLines.ReadOnly = false;
                dgvLines.CellEndEdit += DgvLines_CellEndEdit;
            }

            this.Controls.Add(dgvLines);
            this.Controls.Add(pnlHeader);
        }

        private void DgvLines_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            if (e.ColumnIndex == 0) // Account code entered
            {
                string code = dgvLines.Rows[e.RowIndex].Cells["AccCode"].Value?.ToString();
                if (!string.IsNullOrEmpty(code))
                {
                    var account = _accountRepo.GetByCode(code);
                    if (account != null)
                    {
                        dgvLines.Rows[e.RowIndex].Cells["AccName"].Value = account.AccountName;
                    }
                }
            }
            UpdateTotals();
        }

        private void UpdateTotals()
        {
            long totalDebit = 0, totalCredit = 0;
            foreach (DataGridViewRow row in dgvLines.Rows)
            {
                if (row.IsNewRow) continue;
                long debit, credit;
                long.TryParse(row.Cells["Debit"].Value?.ToString() ?? "0", out debit);
                long.TryParse(row.Cells["Credit"].Value?.ToString() ?? "0", out credit);
                totalDebit += debit;
                totalCredit += credit;
            }

            lblDebitTotal.Text = "Debit: " + Formatting.FormatMoney(totalDebit);
            lblCreditTotal.Text = "Credit: " + Formatting.FormatMoney(totalCredit);
            long diff = totalDebit - totalCredit;
            lblDifference.Text = "Selisih: " + Formatting.FormatMoney(Math.Abs(diff));
            lblDifference.ForeColor = diff == 0 ? ThemeConstants.FgPrimary : ThemeConstants.FgError;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.F5:
                    if (!_readOnly) SaveJournal();
                    return true;

                case Keys.Insert:
                    if (!_readOnly) dgvLines.Rows.Add();
                    return true;

                case Keys.Delete:
                    if (!_readOnly && dgvLines.CurrentRow != null && !dgvLines.CurrentRow.IsNewRow)
                        dgvLines.Rows.Remove(dgvLines.CurrentRow);
                    return true;

                case Keys.Escape:
                    this.Close();
                    return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void SaveJournal()
        {
            var entry = new JournalEntry
            {
                DocDate = txtDate.Text.Trim(),
                Remark = txtRemark.Text.Trim(),
                PeriodCode = txtDate.Text.Substring(0, 4) + txtDate.Text.Substring(5, 2),
                ChangedBy = _currentUserId,
                Lines = new List<JournalLine>()
            };

            foreach (DataGridViewRow row in dgvLines.Rows)
            {
                if (row.IsNewRow) continue;
                string accCode = row.Cells["AccCode"].Value?.ToString();
                if (string.IsNullOrEmpty(accCode)) continue;

                long debit, credit;
                long.TryParse(row.Cells["Debit"].Value?.ToString() ?? "0", out debit);
                long.TryParse(row.Cells["Credit"].Value?.ToString() ?? "0", out credit);

                entry.Lines.Add(new JournalLine
                {
                    AccountCode = accCode,
                    Remark = row.Cells["Remark"].Value?.ToString() ?? "",
                    Debit = debit,
                    Credit = credit
                });
            }

            try
            {
                string jnl = _accountingService.CreateJournalEntry(entry);
                MessageBox.Show("Jurnal " + jnl + " tersimpan.", "Sukses",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                dgvLines.Rows.Clear();
                txtRemark.Text = "";
                UpdateTotals();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Gagal: " + ex.Message, "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
