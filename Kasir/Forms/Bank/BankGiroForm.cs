using System;
using System.Drawing;
using System.Windows.Forms;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Models;
using Kasir.Utils;

namespace Kasir.Forms.Bank
{
    public class BankGiroForm : BaseForm
    {
        private DataGridView dgvGiros;
        private TextBox txtVendor;
        private GiroRepository _giroRepo;
        private bool _readOnly;

        public BankGiroForm(bool readOnly = false)
        {
            _readOnly = readOnly;
            _giroRepo = new GiroRepository(DbConnection.GetConnection());
            InitializeLayout();
            string action = _readOnly
                ? "Informasi Giro — F2: Cari Supplier, Esc: Keluar"
                : "Giro / Cek — F2: Cari Supplier, F5: Cairkan, F8: Tolak, Ins: Tambah, Esc: Keluar";
            SetAction(action);
        }

        private void InitializeLayout()
        {
            var pnlHeader = new Panel { Dock = DockStyle.Top, Height = 35, BackColor = ThemeConstants.BgFooter };
            var lblVendor = new Label { Text = "Supplier:", Location = new Point(5, 8), AutoSize = true, ForeColor = ThemeConstants.FgLabel };
            txtVendor = new TextBox
            {
                Location = new Point(80, 5), Width = 200,
                BackColor = ThemeConstants.BgInput, ForeColor = ThemeConstants.FgPrimary,
                Font = ThemeConstants.FontInputSmall
            };
            txtVendor.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { LoadGiros(); e.Handled = true; e.SuppressKeyPress = true; }
            };
            pnlHeader.Controls.AddRange(new Control[] { lblVendor, txtVendor });

            dgvGiros = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true };
            ApplyGridTheme(dgvGiros);

            dgvGiros.Columns.Add("GiroNo", "No. Giro");
            dgvGiros.Columns.Add("GiroDate", "Tgl Giro");
            dgvGiros.Columns.Add("DocDate", "Tgl Dok");
            dgvGiros.Columns.Add("Value", "Nilai");
            dgvGiros.Columns.Add("Status", "Status");
            dgvGiros.Columns.Add("Remark", "Keterangan");

            dgvGiros.Columns["GiroNo"].FillWeight = 150;
            dgvGiros.Columns["GiroDate"].FillWeight = 120;
            dgvGiros.Columns["DocDate"].FillWeight = 120;
            dgvGiros.Columns["Value"].FillWeight = 130;
            dgvGiros.Columns["Status"].FillWeight = 80;
            dgvGiros.Columns["Remark"].FillWeight = 200;

            this.Controls.Add(dgvGiros);
            this.Controls.Add(pnlHeader);
        }

        private void LoadGiros()
        {
            string vendor = txtVendor.Text.Trim();
            if (string.IsNullOrEmpty(vendor)) return;

            dgvGiros.Rows.Clear();
            var giros = _giroRepo.GetOpenByVendor(vendor);
            foreach (var g in giros)
            {
                dgvGiros.Rows.Add(g.GiroNo, g.GiroDate, g.DocDate,
                    Formatting.FormatMoney(g.Value),
                    g.Status == "O" ? "Open" : "Cair",
                    g.Remark ?? "");
                dgvGiros.Rows[dgvGiros.Rows.Count - 1].Tag = g;
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.F2:
                    txtVendor.Focus();
                    return true;

                case Keys.F5:
                    if (!_readOnly) ClearSelectedGiro();
                    return true;

                case Keys.F8:
                    if (!_readOnly) RejectSelectedGiro();
                    return true;

                case Keys.Escape:
                    this.Close();
                    return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void ClearSelectedGiro()
        {
            if (dgvGiros.CurrentRow == null) return;
            var giro = dgvGiros.CurrentRow.Tag as GiroEntry;
            if (giro == null) return;

            var confirm = MessageBox.Show("Cairkan giro " + giro.GiroNo + "?",
                "Konfirmasi", MessageBoxButtons.YesNo);
            if (confirm != DialogResult.Yes) return;

            _giroRepo.ClearGiro(giro.Id, 1);
            LoadGiros();
        }

        private void RejectSelectedGiro()
        {
            if (dgvGiros.CurrentRow == null) return;
            var giro = dgvGiros.CurrentRow.Tag as GiroEntry;
            if (giro == null) return;

            var confirm = MessageBox.Show("Tolak giro " + giro.GiroNo + "?",
                "Konfirmasi", MessageBoxButtons.YesNo);
            if (confirm != DialogResult.Yes) return;

            _giroRepo.RejectGiro(giro.Id);
            LoadGiros();
        }
    }
}
