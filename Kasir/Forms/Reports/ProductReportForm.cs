using System;
using System.Drawing;
using System.Windows.Forms;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Utils;

namespace Kasir.Forms.Reports
{
    public class ProductReportForm : BaseForm
    {
        private TextBox txtSearch;
        private DataGridView dgvReport;
        private Label lblSummary;

        public ProductReportForm()
        {
            InitializeLayout();
            SetAction("Cetak Master Barang — F5: Refresh, F7: Export Excel, Esc: Keluar");
            GenerateReport();
        }

        private void InitializeLayout()
        {
            var pnlParams = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = ThemeConstants.BgFooter };

            pnlParams.Controls.Add(new Label { Text = "Cari:", Location = new Point(10, 15), AutoSize = true, ForeColor = ThemeConstants.FgLabel });
            txtSearch = new TextBox
            {
                Location = new Point(60, 12),
                Width = 300,
                BackColor = ThemeConstants.BgInput, ForeColor = ThemeConstants.FgPrimary,
                Font = ThemeConstants.FontGrid
            };
            txtSearch.TextChanged += (s, e) => FilterGrid();
            pnlParams.Controls.Add(txtSearch);

            var btnRefresh = new Button { Text = "F5", Location = new Point(380, 10), Size = new Size(50, 30), ForeColor = ThemeConstants.FgWhite, BackColor = ThemeConstants.BtnPrimary, FlatStyle = FlatStyle.Flat };
            btnRefresh.Click += (s, e) => GenerateReport();
            pnlParams.Controls.Add(btnRefresh);

            var btnExport = new Button { Text = "F7", Location = new Point(440, 10), Size = new Size(50, 30), ForeColor = ThemeConstants.FgWhite, BackColor = ThemeConstants.BtnSecondary, FlatStyle = FlatStyle.Flat };
            btnExport.Click += (s, e) => Export();
            pnlParams.Controls.Add(btnExport);

            dgvReport = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false };
            ApplyGridTheme(dgvReport);

            var pnlFooter = new Panel { Dock = DockStyle.Bottom, Height = 30, BackColor = ThemeConstants.BgFooter };
            lblSummary = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, ForeColor = ThemeConstants.FgWhite, Font = ThemeConstants.FontGrid, Padding = new Padding(0, 0, 20, 0) };
            pnlFooter.Controls.Add(lblSummary);

            this.Controls.Add(dgvReport);
            this.Controls.Add(pnlParams);
            this.Controls.Add(pnlFooter);
        }

        private void GenerateReport()
        {
            dgvReport.Columns.Clear();
            dgvReport.Rows.Clear();

            dgvReport.Columns.Add("Code", "Kode"); dgvReport.Columns["Code"].FillWeight = 140;
            dgvReport.Columns.Add("Barcode", "Barcode"); dgvReport.Columns["Barcode"].FillWeight = 140;
            dgvReport.Columns.Add("Name", "Nama"); dgvReport.Columns["Name"].FillWeight = 250;
            dgvReport.Columns.Add("Unit", "Satuan"); dgvReport.Columns["Unit"].FillWeight = 60;
            dgvReport.Columns.Add("Dept", "Dept"); dgvReport.Columns["Dept"].FillWeight = 60;
            dgvReport.Columns.Add("Price", "Harga Jual"); dgvReport.Columns["Price"].FillWeight = 100;
            dgvReport.Columns.Add("Buying", "Harga Beli"); dgvReport.Columns["Buying"].FillWeight = 100;
            dgvReport.Columns.Add("Cost", "HPP"); dgvReport.Columns["Cost"].FillWeight = 100;
            dgvReport.Columns.Add("Status", "Status"); dgvReport.Columns["Status"].FillWeight = 50;

            var conn = DbConnection.GetConnection();
            var productRepo = new ProductRepository(conn);
            var products = productRepo.GetAllActive();

            foreach (var p in products)
            {
                dgvReport.Rows.Add(
                    p.ProductCode,
                    p.Barcode ?? "",
                    p.Name,
                    p.Unit ?? "",
                    p.DeptCode ?? "",
                    Formatting.FormatCurrencyShort(p.Price),
                    Formatting.FormatCurrencyShort(p.BuyingPrice),
                    Formatting.FormatCurrencyShort(p.CostPrice),
                    p.Status);
            }

            lblSummary.Text = string.Format("{0} products", products.Count);
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
            string fileName = string.Format("MasterBarang_{0}.xlsx", DateTime.Now.ToString("yyyyMMdd"));
            ExcelExporter.ExportWithDialog(dgvReport, fileName, "Master Barang");
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
