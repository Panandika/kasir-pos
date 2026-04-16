using System;
using System.Drawing;
using System.Windows.Forms;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Utils;

namespace Kasir.Forms.Reports
{
    public class SalesReportForm : BaseForm
    {
        private TextBox txtDateFrom;
        private TextBox txtDateTo;
        private DataGridView dgvReport;
        private Label lblGrandTotal;
        private SaleRepository _saleRepo;

        public SalesReportForm()
        {
            _saleRepo = new SaleRepository(DbConnection.GetConnection());
            InitializeLayout();
            SetAction("Sales Report — F5: Generate, F7: Export Excel, Esc: Close");

            // Default to today
            string today = DateTime.Now.ToString("yyyy-MM-dd");
            txtDateFrom.Text = today;
            txtDateTo.Text = today;
        }

        private void InitializeLayout()
        {
            // Parameters panel
            var pnlParams = new Panel
            {
                Dock = DockStyle.Top,
                Height = 50,
                BackColor = ThemeConstants.BgFooter
            };

            var lblFrom = new Label { Text = "From:", Location = new Point(10, 15), AutoSize = true, ForeColor = ThemeConstants.FgLabel };
            txtDateFrom = new TextBox
            {
                Location = new Point(60, 12),
                Width = 120,
                BackColor = ThemeConstants.BgInput,
                ForeColor = ThemeConstants.FgPrimary,
                Font = ThemeConstants.FontInputSmall,
                Text = DateTime.Now.ToString("yyyy-MM-dd")
            };

            var lblTo = new Label { Text = "To:", Location = new Point(200, 15), AutoSize = true, ForeColor = ThemeConstants.FgLabel };
            txtDateTo = new TextBox
            {
                Location = new Point(230, 12),
                Width = 120,
                BackColor = ThemeConstants.BgInput,
                ForeColor = ThemeConstants.FgPrimary,
                Font = ThemeConstants.FontInputSmall,
                Text = DateTime.Now.ToString("yyyy-MM-dd")
            };

            var btnGenerate = new Button
            {
                Text = "F5 - Generate",
                Location = new Point(370, 10),
                Size = new Size(140, 30),
                ForeColor = ThemeConstants.FgWhite,
                BackColor = ThemeConstants.BtnPrimary,
                FlatStyle = FlatStyle.Flat
            };
            btnGenerate.Click += (s, e) => GenerateReport();

            var btnExport = new Button
            {
                Text = "F7 - Export",
                Location = new Point(520, 10),
                Size = new Size(120, 30),
                ForeColor = ThemeConstants.FgWhite,
                BackColor = ThemeConstants.BtnSecondary,
                FlatStyle = FlatStyle.Flat
            };
            btnExport.Click += (s, e) => ExportToExcel();

            pnlParams.Controls.AddRange(new Control[] { lblFrom, txtDateFrom, lblTo, txtDateTo, btnGenerate, btnExport });

            // Report grid
            dgvReport = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true };
            ApplyGridTheme(dgvReport);

            dgvReport.Columns.Add("No", "No");
            dgvReport.Columns.Add("JournalNo", "No. Nota");
            dgvReport.Columns.Add("Date", "Tanggal");
            dgvReport.Columns.Add("Cashier", "Kasir");
            dgvReport.Columns.Add("Items", "Items");
            dgvReport.Columns.Add("Gross", "Bruto");
            dgvReport.Columns.Add("Disc", "Diskon");
            dgvReport.Columns.Add("Total", "Total");
            dgvReport.Columns.Add("Cash", "Tunai");
            dgvReport.Columns.Add("Card", "Kartu");
            dgvReport.Columns.Add("Status", "Status");

            dgvReport.Columns["No"].FillWeight = 40;
            dgvReport.Columns["JournalNo"].FillWeight = 160;
            dgvReport.Columns["Date"].FillWeight = 100;
            dgvReport.Columns["Cashier"].FillWeight = 60;
            dgvReport.Columns["Items"].FillWeight = 50;
            dgvReport.Columns["Gross"].FillWeight = 100;
            dgvReport.Columns["Disc"].FillWeight = 80;
            dgvReport.Columns["Total"].FillWeight = 100;
            dgvReport.Columns["Cash"].FillWeight = 100;
            dgvReport.Columns["Card"].FillWeight = 80;
            dgvReport.Columns["Status"].FillWeight = 60;

            // Grand total
            var pnlTotal = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 35,
                BackColor = ThemeConstants.BgFooter
            };

            lblGrandTotal = new Label
            {
                Text = "GRAND TOTAL: Rp 0",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleRight,
                ForeColor = ThemeConstants.FgWhite,
                Font = ThemeConstants.FontHeader,
                Padding = new Padding(0, 0, 20, 0)
            };
            pnlTotal.Controls.Add(lblGrandTotal);

            this.Controls.Add(dgvReport);
            this.Controls.Add(pnlParams);
            this.Controls.Add(pnlTotal);
        }

        private void GenerateReport()
        {
            dgvReport.Rows.Clear();

            string dateFrom = txtDateFrom.Text.Trim();
            string dateTo = txtDateTo.Text.Trim();

            var sales = _saleRepo.GetByDateRange(dateFrom, dateTo);

            long grandTotal = 0;
            int no = 1;

            foreach (var sale in sales)
            {
                var items = _saleRepo.GetItemsByJournalNo(sale.JournalNo);
                string status = sale.Control == 3 ? "VOID" : "OK";

                dgvReport.Rows.Add(
                    no++,
                    sale.JournalNo,
                    Formatting.FormatDate(sale.DocDate),
                    sale.Cashier,
                    items.Count,
                    Formatting.FormatCurrencyShort(sale.GrossAmount),
                    Formatting.FormatCurrencyShort(sale.TotalDisc),
                    Formatting.FormatCurrencyShort(sale.TotalValue),
                    Formatting.FormatCurrencyShort(sale.CashAmount),
                    Formatting.FormatCurrencyShort(sale.NonCash),
                    status);

                if (sale.Control != 3)
                {
                    grandTotal += sale.TotalValue;
                }
            }

            lblGrandTotal.Text = string.Format("GRAND TOTAL: {0}  ({1} transaksi)",
                Formatting.FormatCurrency(grandTotal), sales.Count);

            SetAction(string.Format("Report generated: {0} to {1}, {2} transactions",
                dateFrom, dateTo, sales.Count));
        }

        private void ExportToExcel()
        {
            if (dgvReport.Rows.Count == 0)
            {
                MessageBox.Show("Generate report first.", "Error");
                return;
            }

            string fileName = string.Format("Sales_{0}_{1}.xlsx",
                txtDateFrom.Text.Replace("-", ""),
                txtDateTo.Text.Replace("-", ""));

            ExcelExporter.ExportWithDialog(dgvReport, fileName, "Sales Report");
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.F5:
                    GenerateReport();
                    return true;
                case Keys.F7:
                    ExportToExcel();
                    return true;
                case Keys.Escape:
                    this.Close();
                    return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
