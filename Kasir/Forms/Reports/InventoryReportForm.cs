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
            var pnlParams = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = ThemeConstants.BgFooter };

            pnlParams.Controls.Add(new Label { Text = "Report:", Location = new Point(10, 15), AutoSize = true, ForeColor = ThemeConstants.FgLabel });
            cboReportType = new ComboBox { Location = new Point(70, 12), Width = 200, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = ThemeConstants.BgInput, ForeColor = ThemeConstants.FgPrimary };
            cboReportType.Items.AddRange(new object[] { "Stock Position", "Purchase Register", "Purchase Returns", "Transfers", "Stock Out (Usage/Damage/Loss)", "Stock Opname", "Price History" });
            cboReportType.SelectedIndex = 0;
            ApplyFocusIndicator(cboReportType);
            pnlParams.Controls.Add(cboReportType);

            pnlParams.Controls.Add(new Label { Text = "From:", Location = new Point(290, 15), AutoSize = true, ForeColor = ThemeConstants.FgLabel });
            txtDateFrom = new TextBox { Location = new Point(340, 12), Width = 110, BackColor = ThemeConstants.BgInput, ForeColor = ThemeConstants.FgPrimary, Font = ThemeConstants.FontGrid };
            ApplyFocusIndicator(txtDateFrom);
            pnlParams.Controls.Add(txtDateFrom);

            pnlParams.Controls.Add(new Label { Text = "To:", Location = new Point(460, 15), AutoSize = true, ForeColor = ThemeConstants.FgLabel });
            txtDateTo = new TextBox { Location = new Point(490, 12), Width = 110, BackColor = ThemeConstants.BgInput, ForeColor = ThemeConstants.FgPrimary, Font = ThemeConstants.FontGrid };
            ApplyFocusIndicator(txtDateTo);
            pnlParams.Controls.Add(txtDateTo);

            var btnGen = new Button { Text = "F5", Location = new Point(620, 10), Size = new Size(50, 30), ForeColor = ThemeConstants.FgWhite, BackColor = ThemeConstants.BtnPrimary, FlatStyle = FlatStyle.Flat };
            btnGen.Click += (s, e) => GenerateReport();
            pnlParams.Controls.Add(btnGen);

            var btnExport = new Button { Text = "F7", Location = new Point(680, 10), Size = new Size(50, 30), ForeColor = ThemeConstants.FgWhite, BackColor = ThemeConstants.BtnSecondary, FlatStyle = FlatStyle.Flat };
            btnExport.Click += (s, e) => Export();
            pnlParams.Controls.Add(btnExport);

            dgvReport = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true };
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
                case 6: // Price History
                    GeneratePriceHistory(conn);
                    break;
                default:
                    MessageBox.Show("Report type not yet implemented.");
                    break;
            }
        }

        private void GenerateStockPosition(System.Data.SQLite.SQLiteConnection conn)
        {
            dgvReport.Columns.Add("Code", "Kode"); dgvReport.Columns["Code"].FillWeight = 140;
            dgvReport.Columns.Add("Name", "Nama"); dgvReport.Columns["Name"].FillWeight = 250;
            dgvReport.Columns.Add("Stock", "Stok"); dgvReport.Columns["Stock"].FillWeight = 80;
            dgvReport.Columns.Add("AvgCost", "HPP"); dgvReport.Columns["AvgCost"].FillWeight = 100;
            dgvReport.Columns.Add("Value", "Nilai"); dgvReport.Columns["Value"].FillWeight = 120;

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
            dgvReport.Columns.Add("No", "No. Faktur"); dgvReport.Columns["No"].FillWeight = 160;
            dgvReport.Columns.Add("Date", "Tanggal"); dgvReport.Columns["Date"].FillWeight = 100;
            dgvReport.Columns.Add("Vendor", "Supplier"); dgvReport.Columns["Vendor"].FillWeight = 120;
            dgvReport.Columns.Add("Total", "Total"); dgvReport.Columns["Total"].FillWeight = 120;

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
            dgvReport.Columns.Add("No", "No. Retur"); dgvReport.Columns["No"].FillWeight = 160;
            dgvReport.Columns.Add("Date", "Tanggal"); dgvReport.Columns["Date"].FillWeight = 100;
            dgvReport.Columns.Add("Vendor", "Supplier"); dgvReport.Columns["Vendor"].FillWeight = 120;
            dgvReport.Columns.Add("Total", "Total"); dgvReport.Columns["Total"].FillWeight = 120;

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
            dgvReport.Columns.Add("No", "No. Transfer"); dgvReport.Columns["No"].FillWeight = 160;
            dgvReport.Columns.Add("Date", "Tanggal"); dgvReport.Columns["Date"].FillWeight = 100;
            dgvReport.Columns.Add("From", "Dari"); dgvReport.Columns["From"].FillWeight = 100;
            dgvReport.Columns.Add("To", "Ke"); dgvReport.Columns["To"].FillWeight = 100;

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
            dgvReport.Columns.Add("No", "No. Dokumen"); dgvReport.Columns["No"].FillWeight = 140;
            dgvReport.Columns.Add("Date", "Tanggal"); dgvReport.Columns["Date"].FillWeight = 90;
            dgvReport.Columns.Add("Type", "Jenis"); dgvReport.Columns["Type"].FillWeight = 80;
            dgvReport.Columns.Add("Code", "Kode"); dgvReport.Columns["Code"].FillWeight = 120;
            dgvReport.Columns.Add("Name", "Nama"); dgvReport.Columns["Name"].FillWeight = 200;
            dgvReport.Columns.Add("Qty", "Qty"); dgvReport.Columns["Qty"].FillWeight = 60;
            dgvReport.Columns.Add("Cost", "Harga"); dgvReport.Columns["Cost"].FillWeight = 100;
            dgvReport.Columns.Add("Value", "Nilai"); dgvReport.Columns["Value"].FillWeight = 120;
            dgvReport.Columns.Add("Remark", "Keterangan"); dgvReport.Columns["Remark"].FillWeight = 100;

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
                    dgvReport.Rows[idx].DefaultCellStyle.ForeColor = ThemeConstants.FgError;
                    totalShortage += System.Math.Abs(row.VarianceValue);
                }
                else if (row.Variance > 0)
                {
                    dgvReport.Rows[idx].DefaultCellStyle.ForeColor = ThemeConstants.FgSuccess;
                    totalSurplus += row.VarianceValue;
                }
            }

            lblSummary.Text = string.Format("Kurang: -{0}  Lebih: {1}  Nett: {2}  ({3} items)",
                Formatting.FormatCurrency(totalShortage),
                Formatting.FormatCurrency(totalSurplus),
                Formatting.FormatCurrency(totalSurplus - totalShortage),
                rows.Count);
        }

        private void GeneratePriceHistory(System.Data.SQLite.SQLiteConnection conn)
        {
            dgvReport.Columns.Add("Date", "Tanggal"); dgvReport.Columns["Date"].Width = 100;
            dgvReport.Columns.Add("Code", "Kode"); dgvReport.Columns["Code"].Width = 120;
            dgvReport.Columns.Add("Name", "Nama"); dgvReport.Columns["Name"].Width = 250;
            dgvReport.Columns.Add("OldPrice", "Harga Lama"); dgvReport.Columns["OldPrice"].Width = 100;
            dgvReport.Columns.Add("NewPrice", "Harga Baru"); dgvReport.Columns["NewPrice"].Width = 100;
            dgvReport.Columns.Add("Vendor", "Supplier"); dgvReport.Columns["Vendor"].Width = 100;
            dgvReport.Columns.Add("DocNo", "No. Dokumen"); dgvReport.Columns["DocNo"].Width = 140;

            string sql = @"SELECT ph.doc_date, ph.product_code, COALESCE(p.name, ph.product_name) AS product_name,
                                  ph.old_value, ph.value, ph.sub_code, ph.journal_no
                           FROM price_history ph
                           LEFT JOIN products p ON p.product_code = ph.product_code
                           WHERE ph.doc_date BETWEEN @from AND @to
                           ORDER BY ph.doc_date DESC, ph.product_code";

            var rows = SqlHelper.Query(conn, sql,
                reader => new
                {
                    Date = SqlHelper.GetString(reader, "doc_date"),
                    Code = SqlHelper.GetString(reader, "product_code"),
                    Name = SqlHelper.GetString(reader, "product_name"),
                    OldValue = SqlHelper.GetLong(reader, "old_value"),
                    NewValue = SqlHelper.GetLong(reader, "value"),
                    Vendor = SqlHelper.GetString(reader, "sub_code"),
                    DocNo = SqlHelper.GetString(reader, "journal_no")
                },
                SqlHelper.Param("@from", txtDateFrom.Text),
                SqlHelper.Param("@to", txtDateTo.Text));

            foreach (var r in rows)
            {
                dgvReport.Rows.Add(
                    Formatting.FormatDate(r.Date),
                    r.Code,
                    r.Name ?? "",
                    Formatting.FormatCurrencyShort(r.OldValue),
                    Formatting.FormatCurrencyShort(r.NewValue),
                    r.Vendor ?? "",
                    r.DocNo ?? "");
            }

            lblSummary.Text = string.Format("{0} price changes", rows.Count);
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
