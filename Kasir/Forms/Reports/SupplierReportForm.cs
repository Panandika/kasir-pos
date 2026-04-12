using System;
using System.Drawing;
using System.Windows.Forms;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Utils;

namespace Kasir.Forms.Reports
{
    public class SupplierReportForm : BaseForm
    {
        private TextBox txtSearch;
        private DataGridView dgvReport;
        private Label lblSummary;

        public SupplierReportForm()
        {
            InitializeLayout();
            SetAction("Cetak Master Supplier — F5: Refresh, F7: Export Excel, Esc: Keluar");
            GenerateReport();
        }

        private void InitializeLayout()
        {
            var pnlParams = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = Color.FromArgb(0, 30, 0) };

            pnlParams.Controls.Add(new Label { Text = "Cari:", Location = new Point(10, 15), AutoSize = true, ForeColor = Color.Gray });
            txtSearch = new TextBox
            {
                Location = new Point(60, 12),
                Width = 300,
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.FromArgb(0, 255, 0),
                Font = new Font("Consolas", 11f)
            };
            txtSearch.TextChanged += (s, e) => FilterGrid();
            pnlParams.Controls.Add(txtSearch);

            var btnRefresh = new Button { Text = "F5", Location = new Point(380, 10), Size = new Size(50, 30), ForeColor = Color.White, BackColor = Color.FromArgb(0, 80, 0), FlatStyle = FlatStyle.Flat };
            btnRefresh.Click += (s, e) => GenerateReport();
            pnlParams.Controls.Add(btnRefresh);

            var btnExport = new Button { Text = "F7", Location = new Point(440, 10), Size = new Size(50, 30), ForeColor = Color.White, BackColor = Color.FromArgb(0, 60, 0), FlatStyle = FlatStyle.Flat };
            btnExport.Click += (s, e) => Export();
            pnlParams.Controls.Add(btnExport);

            dgvReport = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false };
            ApplyGridTheme(dgvReport);

            var pnlFooter = new Panel { Dock = DockStyle.Bottom, Height = 30, BackColor = Color.FromArgb(0, 30, 0) };
            lblSummary = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, ForeColor = Color.White, Font = new Font("Consolas", 11f), Padding = new Padding(0, 0, 20, 0) };
            pnlFooter.Controls.Add(lblSummary);

            this.Controls.Add(dgvReport);
            this.Controls.Add(pnlParams);
            this.Controls.Add(pnlFooter);
        }

        private void GenerateReport()
        {
            dgvReport.Columns.Clear();
            dgvReport.Rows.Clear();

            dgvReport.Columns.Add("Code", "Kode"); dgvReport.Columns["Code"].Width = 120;
            dgvReport.Columns.Add("Name", "Nama"); dgvReport.Columns["Name"].Width = 250;
            dgvReport.Columns.Add("Address", "Alamat"); dgvReport.Columns["Address"].Width = 300;
            dgvReport.Columns.Add("Phone", "Telepon"); dgvReport.Columns["Phone"].Width = 120;
            dgvReport.Columns.Add("Contact", "Contact Person"); dgvReport.Columns["Contact"].Width = 150;

            var conn = DbConnection.GetConnection();
            var subRepo = new SubsidiaryRepository(conn);
            var vendors = subRepo.GetAllByGroup("1", 10000, 0);

            foreach (var v in vendors)
            {
                dgvReport.Rows.Add(
                    v.SubCode,
                    v.Name,
                    v.Address ?? "",
                    v.Phone ?? "",
                    v.ContactPerson ?? "");
            }

            lblSummary.Text = string.Format("{0} suppliers", vendors.Count);
        }

        private void FilterGrid()
        {
            string query = txtSearch.Text.Trim().ToUpperInvariant();

            foreach (DataGridViewRow row in dgvReport.Rows)
            {
                if (string.IsNullOrEmpty(query))
                {
                    row.Visible = true;
                    continue;
                }

                string code = (row.Cells["Code"].Value ?? "").ToString().ToUpperInvariant();
                string name = (row.Cells["Name"].Value ?? "").ToString().ToUpperInvariant();
                row.Visible = code.Contains(query) || name.Contains(query);
            }
        }

        private void Export()
        {
            if (dgvReport.Rows.Count == 0)
            {
                MessageBox.Show("No data to export.");
                return;
            }
            string fileName = string.Format("MasterSupplier_{0}.xlsx", DateTime.Now.ToString("yyyyMMdd"));
            ExcelExporter.ExportWithDialog(dgvReport, fileName, "Master Supplier");
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.F5: GenerateReport(); return true;
                case Keys.F7: Export(); return true;
                case Keys.Escape: this.Close(); return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
