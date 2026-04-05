using System;
using System.Drawing;
using System.Windows.Forms;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Services;
using Kasir.Utils;

namespace Kasir.Forms.Reports
{
    public class InventoryReportForm : BaseForm
    {
        private ComboBox cboReportType;
        private TextBox txtDateFrom, txtDateTo;
        private DataGridView dgvReport;
        private Label lblSummary;

        public InventoryReportForm(int preSelectedIndex = 0)
        {
            InitializeLayout();
            SetAction("Inventory Reports — F5: Generate, F7: Export, Esc: Close");
            txtDateFrom.Text = DateTime.Now.ToString("yyyy-MM-dd");
            txtDateTo.Text = DateTime.Now.ToString("yyyy-MM-dd");
            if (preSelectedIndex >= 0 && preSelectedIndex < cboReportType.Items.Count)
            {
                cboReportType.SelectedIndex = preSelectedIndex;
            }
        }

        private void InitializeLayout()
        {
            var pnlParams = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = Color.FromArgb(0, 30, 0) };

            pnlParams.Controls.Add(new Label { Text = "Report:", Location = new Point(10, 15), AutoSize = true, ForeColor = Color.Gray });
            cboReportType = new ComboBox { Location = new Point(70, 12), Width = 200, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Color.FromArgb(30, 30, 30), ForeColor = Color.FromArgb(0, 255, 0) };
            cboReportType.Items.AddRange(new object[] { "Stock Position", "Purchase Register", "Purchase Returns", "Transfers", "Stock Out (Usage/Damage/Loss)", "Stock Opname", "Price History" });
            cboReportType.SelectedIndex = 0;
            pnlParams.Controls.Add(cboReportType);

            pnlParams.Controls.Add(new Label { Text = "From:", Location = new Point(290, 15), AutoSize = true, ForeColor = Color.Gray });
            txtDateFrom = new TextBox { Location = new Point(340, 12), Width = 110, BackColor = Color.FromArgb(30, 30, 30), ForeColor = Color.FromArgb(0, 255, 0), Font = new Font("Consolas", 11f) };
            pnlParams.Controls.Add(txtDateFrom);

            pnlParams.Controls.Add(new Label { Text = "To:", Location = new Point(460, 15), AutoSize = true, ForeColor = Color.Gray });
            txtDateTo = new TextBox { Location = new Point(490, 12), Width = 110, BackColor = Color.FromArgb(30, 30, 30), ForeColor = Color.FromArgb(0, 255, 0), Font = new Font("Consolas", 11f) };
            pnlParams.Controls.Add(txtDateTo);

            var btnGen = new Button { Text = "F5", Location = new Point(620, 10), Size = new Size(50, 30), ForeColor = Color.White, BackColor = Color.FromArgb(0, 80, 0), FlatStyle = FlatStyle.Flat };
            btnGen.Click += (s, e) => GenerateReport();
            pnlParams.Controls.Add(btnGen);

            var btnExport = new Button { Text = "F7", Location = new Point(680, 10), Size = new Size(50, 30), ForeColor = Color.White, BackColor = Color.FromArgb(0, 60, 0), FlatStyle = FlatStyle.Flat };
            btnExport.Click += (s, e) => Export();
            pnlParams.Controls.Add(btnExport);

            dgvReport = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true };
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
            var conn = DbConnection.GetConnection();

            switch (cboReportType.SelectedIndex)
            {
                case 0: // Stock Position
                    GenerateStockPosition(conn);
                    break;
                case 1: // Purchase Register
                    GeneratePurchaseRegister(conn);
                    break;
                case 2: // Purchase Returns
                    GeneratePurchaseReturns(conn);
                    break;
                case 3: // Transfers
                    GenerateTransfers(conn);
                    break;
                case 4: // Stock Out
                    GenerateStockOut(conn);
                    break;
                case 5: // Stock Opname
                    GenerateStockOpname(conn);
                    break;
                default:
                    MessageBox.Show("Report type not yet implemented.");
                    break;
            }
        }

        private void GenerateStockPosition(System.Data.SQLite.SQLiteConnection conn)
        {
            dgvReport.Columns.Add("Code", "Kode"); dgvReport.Columns["Code"].Width = 140;
            dgvReport.Columns.Add("Name", "Nama"); dgvReport.Columns["Name"].Width = 250;
            dgvReport.Columns.Add("Stock", "Stok"); dgvReport.Columns["Stock"].Width = 80;
            dgvReport.Columns.Add("AvgCost", "HPP"); dgvReport.Columns["AvgCost"].Width = 100;
            dgvReport.Columns.Add("Value", "Nilai"); dgvReport.Columns["Value"].Width = 120;

            var productRepo = new ProductRepository(conn);
            var invService = new InventoryService(conn);
            var products = productRepo.GetAllActive();
            long totalValue = 0;

            foreach (var p in products)
            {
                int stock = invService.GetStockOnHand(p.ProductCode);
                int avgCost = invService.CalculateAverageCost(p.ProductCode);
                long value = (long)stock * avgCost;
                totalValue += value;

                dgvReport.Rows.Add(p.ProductCode, p.Name, stock, Formatting.FormatCurrencyShort(avgCost), Formatting.FormatCurrencyShort(value));
            }

            lblSummary.Text = string.Format("Total Value: {0}  ({1} products)", Formatting.FormatCurrency(totalValue), products.Count);
        }

        private void GeneratePurchaseRegister(System.Data.SQLite.SQLiteConnection conn)
        {
            dgvReport.Columns.Add("No", "No. Faktur"); dgvReport.Columns["No"].Width = 160;
            dgvReport.Columns.Add("Date", "Tanggal"); dgvReport.Columns["Date"].Width = 100;
            dgvReport.Columns.Add("Vendor", "Supplier"); dgvReport.Columns["Vendor"].Width = 120;
            dgvReport.Columns.Add("Total", "Total"); dgvReport.Columns["Total"].Width = 120;

            var purchaseRepo = new PurchaseRepository(conn);
            var purchases = purchaseRepo.GetByDateRange(txtDateFrom.Text, txtDateTo.Text, "PURCHASE");
            long total = 0;
            foreach (var p in purchases)
            {
                dgvReport.Rows.Add(p.JournalNo, Formatting.FormatDate(p.DocDate), p.SubCode, Formatting.FormatCurrencyShort(p.TotalValue));
                total += p.TotalValue;
            }
            lblSummary.Text = string.Format("Total: {0}  ({1} invoices)", Formatting.FormatCurrency(total), purchases.Count);
        }

        private void GeneratePurchaseReturns(System.Data.SQLite.SQLiteConnection conn)
        {
            dgvReport.Columns.Add("No", "No. Retur"); dgvReport.Columns["No"].Width = 160;
            dgvReport.Columns.Add("Date", "Tanggal"); dgvReport.Columns["Date"].Width = 100;
            dgvReport.Columns.Add("Vendor", "Supplier"); dgvReport.Columns["Vendor"].Width = 120;
            dgvReport.Columns.Add("Total", "Total"); dgvReport.Columns["Total"].Width = 120;

            var purchaseRepo = new PurchaseRepository(conn);
            var returns = purchaseRepo.GetByDateRange(txtDateFrom.Text, txtDateTo.Text, "PURCHASE_RETURN");
            long total = 0;
            foreach (var r in returns)
            {
                dgvReport.Rows.Add(r.JournalNo, Formatting.FormatDate(r.DocDate), r.SubCode, Formatting.FormatCurrencyShort(r.TotalValue));
                total += r.TotalValue;
            }
            lblSummary.Text = string.Format("Total Returns: {0}  ({1} documents)", Formatting.FormatCurrency(total), returns.Count);
        }

        private void GenerateTransfers(System.Data.SQLite.SQLiteConnection conn)
        {
            dgvReport.Columns.Add("No", "No. Transfer"); dgvReport.Columns["No"].Width = 160;
            dgvReport.Columns.Add("Date", "Tanggal"); dgvReport.Columns["Date"].Width = 100;
            dgvReport.Columns.Add("From", "Dari"); dgvReport.Columns["From"].Width = 100;
            dgvReport.Columns.Add("To", "Ke"); dgvReport.Columns["To"].Width = 100;

            var transferRepo = new StockTransferRepository(conn);
            var transfers = transferRepo.GetByDateRange(txtDateFrom.Text, txtDateTo.Text);
            foreach (var t in transfers)
            {
                dgvReport.Rows.Add(t.JournalNo, Formatting.FormatDate(t.DocDate), t.FromLocation, t.ToLocation);
            }
            lblSummary.Text = string.Format("{0} transfers", transfers.Count);
        }

        private void GenerateStockOut(System.Data.SQLite.SQLiteConnection conn)
        {
            dgvReport.Columns.Add("No", "No. Dokumen"); dgvReport.Columns["No"].Width = 140;
            dgvReport.Columns.Add("Date", "Tanggal"); dgvReport.Columns["Date"].Width = 90;
            dgvReport.Columns.Add("Type", "Jenis"); dgvReport.Columns["Type"].Width = 80;
            dgvReport.Columns.Add("Code", "Kode"); dgvReport.Columns["Code"].Width = 120;
            dgvReport.Columns.Add("Name", "Nama"); dgvReport.Columns["Name"].Width = 200;
            dgvReport.Columns.Add("Qty", "Qty"); dgvReport.Columns["Qty"].Width = 60;
            dgvReport.Columns.Add("Cost", "Harga"); dgvReport.Columns["Cost"].Width = 100;
            dgvReport.Columns.Add("Value", "Nilai"); dgvReport.Columns["Value"].Width = 120;
            dgvReport.Columns.Add("Remark", "Keterangan"); dgvReport.Columns["Remark"].Width = 100;

            var adjRepo = new StockAdjustmentRepository(conn);
            var items = adjRepo.GetAllItemsByDateRange(txtDateFrom.Text, txtDateTo.Text);
            long totalValue = 0;
            foreach (var item in items)
            {
                dgvReport.Rows.Add(
                    item.JournalNo,
                    Formatting.FormatDate(item.DocDate),
                    item.DocType,
                    item.ProductCode,
                    item.ProductName ?? "",
                    item.Quantity,
                    Formatting.FormatCurrencyShort(item.CostPrice),
                    Formatting.FormatCurrencyShort(item.Value),
                    item.Reason ?? "");
                totalValue += item.Value;
            }
            lblSummary.Text = string.Format("Total: {0}  ({1} items)", Formatting.FormatCurrency(totalValue), items.Count);
        }

        private void GenerateStockOpname(System.Data.SQLite.SQLiteConnection conn)
        {
            dgvReport.Columns.Add("Code", "Kode"); dgvReport.Columns["Code"].Width = 120;
            dgvReport.Columns.Add("Name", "Nama"); dgvReport.Columns["Name"].Width = 250;
            dgvReport.Columns.Add("QtySystem", "Stok Sistem"); dgvReport.Columns["QtySystem"].Width = 80;
            dgvReport.Columns.Add("QtyActual", "Stok Fisik"); dgvReport.Columns["QtyActual"].Width = 80;
            dgvReport.Columns.Add("Variance", "Selisih"); dgvReport.Columns["Variance"].Width = 80;
            dgvReport.Columns.Add("Cost", "HPP"); dgvReport.Columns["Cost"].Width = 100;
            dgvReport.Columns.Add("VarValue", "Nilai Selisih"); dgvReport.Columns["VarValue"].Width = 120;

            var adjRepo = new StockAdjustmentRepository(conn);
            var rows = adjRepo.GetOpnameByDateRange(txtDateFrom.Text, txtDateTo.Text);
            long totalShortage = 0;
            long totalSurplus = 0;

            foreach (var row in rows)
            {
                int idx = dgvReport.Rows.Add(
                    row.ProductCode,
                    row.ProductName ?? "",
                    row.QtySystem,
                    row.QtyActual,
                    row.Variance,
                    Formatting.FormatCurrencyShort(row.CostPrice),
                    Formatting.FormatCurrencyShort(row.VarianceValue));

                if (row.Variance < 0)
                {
                    dgvReport.Rows[idx].DefaultCellStyle.ForeColor = Color.FromArgb(255, 80, 80);
                    totalShortage += row.VarianceValue;
                }
                else if (row.Variance > 0)
                {
                    dgvReport.Rows[idx].DefaultCellStyle.ForeColor = Color.LimeGreen;
                    totalSurplus += row.VarianceValue;
                }
            }

            lblSummary.Text = string.Format("Kurang: {0}  Lebih: {1}  Nett: {2}  ({3} items)",
                Formatting.FormatCurrency(totalShortage),
                Formatting.FormatCurrency(totalSurplus),
                Formatting.FormatCurrency(totalShortage + totalSurplus),
                rows.Count);
        }

        private void Export()
        {
            if (dgvReport.Rows.Count == 0) { MessageBox.Show("Generate report first."); return; }
            string fileName = string.Format("Inventory_{0}_{1}.xlsx", cboReportType.Text.Replace(" ", ""), DateTime.Now.ToString("yyyyMMdd"));
            ExcelExporter.ExportWithDialog(dgvReport, fileName, cboReportType.Text);
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
