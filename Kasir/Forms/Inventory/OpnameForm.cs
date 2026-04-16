using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Kasir.Data;
using Kasir.Services;
using Kasir.Utils;

namespace Kasir.Forms.Inventory
{
    public class OpnameForm : BaseForm
    {
        private DataGridView dgvOpname;
        private StockOpnameService _service;
        private List<OpnameLine> _lines;

        public OpnameForm()
        {
            var conn = DbConnection.GetConnection();
            _service = new StockOpnameService(conn, new ClockImpl());
            _lines = new List<OpnameLine>();
            InitializeLayout();
            SetAction("Stok Opname — F3: Load Sheet, F10: Save Adjustments, Esc: Close");
        }

        private void InitializeLayout()
        {
            dgvOpname = new DataGridView { Dock = DockStyle.Fill };
            ApplyGridTheme(dgvOpname);
            dgvOpname.ReadOnly = false;

            dgvOpname.Columns.Add(new DataGridViewTextBoxColumn { Name = "Code", HeaderText = "Kode", ReadOnly = true, FillWeight = 140 });
            dgvOpname.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "Nama Barang", ReadOnly = true, FillWeight = 250 });
            dgvOpname.Columns.Add(new DataGridViewTextBoxColumn { Name = "System", HeaderText = "Stok Sistem", ReadOnly = true, FillWeight = 100 });
            dgvOpname.Columns.Add(new DataGridViewTextBoxColumn { Name = "Physical", HeaderText = "Stok Fisik", FillWeight = 100 });
            dgvOpname.Columns.Add(new DataGridViewTextBoxColumn { Name = "Variance", HeaderText = "Selisih", ReadOnly = true, FillWeight = 80 });

            dgvOpname.Columns["Physical"].DefaultCellStyle.BackColor = ThemeConstants.BgHeader;
            dgvOpname.Columns["Physical"].DefaultCellStyle.ForeColor = ThemeConstants.FgWarning;

            dgvOpname.CellEndEdit += DgvOpname_CellEndEdit;

            this.Controls.Add(dgvOpname);
        }

        private void LoadSheet()
        {
            _lines = _service.GetOpnameSheet(500);
            dgvOpname.Rows.Clear();

            foreach (var line in _lines)
            {
                int rowIdx = dgvOpname.Rows.Add(
                    line.ProductCode,
                    line.ProductName,
                    line.SystemQty.ToString(),
                    line.SystemQty.ToString(), // Default physical = system
                    "0");
                dgvOpname.Rows[rowIdx].Tag = line;
            }

            SetAction(string.Format("Loaded {0} products. Edit 'Stok Fisik' column.", _lines.Count));
        }

        private void DgvOpname_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            if (dgvOpname.Columns[e.ColumnIndex].Name != "Physical") return;
            if (e.RowIndex < 0 || e.RowIndex >= _lines.Count) return;

            int physical;
            if (!int.TryParse(dgvOpname.Rows[e.RowIndex].Cells["Physical"].Value?.ToString(), out physical))
            {
                physical = _lines[e.RowIndex].SystemQty;
            }

            _lines[e.RowIndex].PhysicalQty = physical;
            int variance = physical - _lines[e.RowIndex].SystemQty;
            dgvOpname.Rows[e.RowIndex].Cells["Variance"].Value = variance.ToString();

            // Color variance
            if (variance < 0)
                dgvOpname.Rows[e.RowIndex].Cells["Variance"].Style.ForeColor = ThemeConstants.FgError;
            else if (variance > 0)
                dgvOpname.Rows[e.RowIndex].Cells["Variance"].Style.ForeColor = ThemeConstants.FgSuccess;
            else
                dgvOpname.Rows[e.RowIndex].Cells["Variance"].Style.ForeColor = ThemeConstants.FgLabel;
        }

        private void SaveAdjustments()
        {
            if (_lines.Count == 0) { MessageBox.Show("Load sheet dulu (F3)."); return; }

            // Check if any variance
            int variances = 0;
            foreach (var line in _lines)
            {
                if (line.PhysicalQty != line.SystemQty) variances++;
            }

            if (variances == 0) { MessageBox.Show("Tidak ada selisih."); return; }

            if (MessageBox.Show(string.Format("Simpan {0} penyesuaian stok?", variances),
                "Konfirmasi", MessageBoxButtons.YesNo) == DialogResult.Yes)
            {
                string jnl = _service.CreateOpnameAdjustment(_lines, 1);
                MessageBox.Show("Opname saved: " + jnl + "\nStock adjusted.");
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.F3: LoadSheet(); return true;
                case Keys.F10: SaveAdjustments(); return true;
                case Keys.Escape: this.Close(); return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
