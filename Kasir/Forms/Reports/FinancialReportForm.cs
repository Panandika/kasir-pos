using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Models;
using Kasir.Services;
using Kasir.Utils;

namespace Kasir.Forms.Reports
{
    public class FinancialReportForm : BaseForm
    {
        private ComboBox cboReportType;
        private TextBox txtPeriod;
        private DataGridView dgvReport;
        private Label lblTotal;
        private Button btnExport;
        private AccountRepository _accountRepo;
        private AccountBalanceRepository _balanceRepo;
        private GlDetailRepository _glRepo;
        private PayablesService _payablesService;

        public FinancialReportForm()
        {
            var conn = DbConnection.GetConnection();
            _accountRepo = new AccountRepository(conn);
            _balanceRepo = new AccountBalanceRepository(conn);
            _glRepo = new GlDetailRepository(conn);
            _payablesService = new PayablesService(conn);
            InitializeLayout();
            SetAction("Laporan Keuangan — F5: Cetak, F10: Export Excel, Esc: Keluar");
        }

        private void InitializeLayout()
        {
            var pnlHeader = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = ThemeConstants.BgFooter };

            var lblType = new Label { Text = "Jenis:", Location = new Point(5, 8), AutoSize = true, ForeColor = ThemeConstants.FgLabel };
            cboReportType = new ComboBox
            {
                Location = new Point(60, 5), Width = 200, DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = ThemeConstants.BgInput, ForeColor = ThemeConstants.FgPrimary,
                Font = ThemeConstants.FontGrid
            };
            cboReportType.Items.AddRange(new object[]
            {
                "Neraca Saldo (Trial Balance)",
                "Neraca (Balance Sheet)",
                "Laba/Rugi (Profit & Loss)",
                "Aging Hutang (AP Aging)",
                "Buku Besar (GL Detail)"
            });
            cboReportType.SelectedIndex = 0;

            var lblPeriod = new Label { Text = "Periode:", Location = new Point(280, 8), AutoSize = true, ForeColor = ThemeConstants.FgLabel };
            txtPeriod = new TextBox
            {
                Location = new Point(350, 5), Width = 80, Text = DateTime.Now.ToString("yyyyMM"),
                BackColor = ThemeConstants.BgInput, ForeColor = ThemeConstants.FgPrimary,
                Font = ThemeConstants.FontInputSmall
            };

            btnExport = new Button
            {
                Text = "Export", Location = new Point(450, 4), Width = 80, Height = 28,
                BackColor = ThemeConstants.BtnSecondary, ForeColor = ThemeConstants.FgWhite,
                FlatStyle = FlatStyle.Flat
            };
            btnExport.Click += (s, e) => ExportToExcel();

            pnlHeader.Controls.AddRange(new Control[] { lblType, cboReportType, lblPeriod, txtPeriod, btnExport });

            dgvReport = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true };
            ApplyGridTheme(dgvReport);

            var pnlBottom = new Panel { Dock = DockStyle.Bottom, Height = 30, BackColor = ThemeConstants.BgFooter };
            lblTotal = new Label { Text = "", Dock = DockStyle.Fill, ForeColor = ThemeConstants.FgSuccess, TextAlign = ContentAlignment.MiddleLeft };
            pnlBottom.Controls.Add(lblTotal);

            this.Controls.Add(dgvReport);
            this.Controls.Add(pnlBottom);
            this.Controls.Add(pnlHeader);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.F5:
                    GenerateReport();
                    return true;
                case Keys.F10:
                    ExportToExcel();
                    return true;
                case Keys.Escape:
                    this.Close();
                    return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void GenerateReport()
        {
            dgvReport.Columns.Clear();
            dgvReport.Rows.Clear();

            switch (cboReportType.SelectedIndex)
            {
                case 0: GenerateTrialBalance(); break;
                case 1: GenerateBalanceSheet(); break;
                case 2: GenerateProfitLoss(); break;
                case 3: GenerateApAging(); break;
                case 4: GenerateGlDetail(); break;
            }
        }

        private void GenerateTrialBalance()
        {
            dgvReport.Columns.Add("Code", "Kode");
            dgvReport.Columns.Add("Name", "Nama Perkiraan");
            dgvReport.Columns.Add("Debit", "Debit");
            dgvReport.Columns.Add("Credit", "Kredit");

            dgvReport.Columns["Code"].FillWeight = 120;
            dgvReport.Columns["Name"].FillWeight = 300;
            dgvReport.Columns["Debit"].FillWeight = 150;
            dgvReport.Columns["Credit"].FillWeight = 150;

            string period = txtPeriod.Text.Trim();
            var accounts = _accountRepo.GetDetailAccounts();
            long totalDebit = 0, totalCredit = 0;

            foreach (var acc in accounts)
            {
                var bal = _balanceRepo.GetBalance(acc.AccountCode, period);
                if (bal == null) continue;

                long net = bal.OpeningBalance + bal.DebitTotal - bal.CreditTotal;
                long debit = 0, credit = 0;

                if (acc.NormalBalance == "D")
                {
                    if (net >= 0) debit = net; else credit = -net;
                }
                else
                {
                    if (net <= 0) credit = -net; else debit = net;
                }

                if (debit == 0 && credit == 0) continue;

                dgvReport.Rows.Add(acc.AccountCode, acc.AccountName,
                    debit > 0 ? Formatting.FormatMoney(debit) : "",
                    credit > 0 ? Formatting.FormatMoney(credit) : "");

                totalDebit += debit;
                totalCredit += credit;
            }

            lblTotal.Text = string.Format("Total — Debit: {0}  Kredit: {1}  {2}",
                Formatting.FormatMoney(totalDebit), Formatting.FormatMoney(totalCredit),
                totalDebit == totalCredit ? "BALANCE" : "NOT BALANCED!");
            lblTotal.ForeColor = totalDebit == totalCredit ? ThemeConstants.FgPrimary : ThemeConstants.FgError;
        }

        private void GenerateBalanceSheet()
        {
            dgvReport.Columns.Add("Code", "Kode");
            dgvReport.Columns.Add("Name", "Nama Perkiraan");
            dgvReport.Columns.Add("Amount", "Jumlah");

            dgvReport.Columns["Code"].FillWeight = 120;
            dgvReport.Columns["Name"].FillWeight = 350;
            dgvReport.Columns["Amount"].FillWeight = 180;

            string period = txtPeriod.Text.Trim();
            long totalAssets = 0, totalLiabEquity = 0;

            // Assets (group 1)
            dgvReport.Rows.Add("", "=== AKTIVA ===", "");
            totalAssets = AddGroupRows(1, period);
            dgvReport.Rows.Add("", "TOTAL AKTIVA", Formatting.FormatMoney(totalAssets));

            dgvReport.Rows.Add("", "", "");

            // Liabilities (group 2)
            dgvReport.Rows.Add("", "=== KEWAJIBAN ===", "");
            long totalLiab = AddGroupRows(2, period);
            totalLiabEquity += totalLiab;
            dgvReport.Rows.Add("", "TOTAL KEWAJIBAN", Formatting.FormatMoney(totalLiab));

            // Equity (group 3)
            dgvReport.Rows.Add("", "=== MODAL ===", "");
            long totalEquity = AddGroupRows(3, period);
            totalLiabEquity += totalEquity;
            dgvReport.Rows.Add("", "TOTAL MODAL", Formatting.FormatMoney(totalEquity));

            dgvReport.Rows.Add("", "TOTAL KEWAJIBAN + MODAL", Formatting.FormatMoney(totalLiabEquity));

            lblTotal.Text = string.Format("Aktiva: {0}  Pasiva: {1}  {2}",
                Formatting.FormatMoney(totalAssets), Formatting.FormatMoney(totalLiabEquity),
                totalAssets == totalLiabEquity ? "BALANCE" : "NOT BALANCED!");
            lblTotal.ForeColor = totalAssets == totalLiabEquity ? ThemeConstants.FgPrimary : ThemeConstants.FgError;
        }

        private void GenerateProfitLoss()
        {
            dgvReport.Columns.Add("Code", "Kode");
            dgvReport.Columns.Add("Name", "Nama Perkiraan");
            dgvReport.Columns.Add("Amount", "Jumlah");

            dgvReport.Columns["Code"].FillWeight = 120;
            dgvReport.Columns["Name"].FillWeight = 350;
            dgvReport.Columns["Amount"].FillWeight = 180;

            string period = txtPeriod.Text.Trim();

            // Revenue (group 4)
            dgvReport.Rows.Add("", "=== PENDAPATAN ===", "");
            long totalRevenue = AddGroupRows(4, period);
            dgvReport.Rows.Add("", "TOTAL PENDAPATAN", Formatting.FormatMoney(totalRevenue));

            dgvReport.Rows.Add("", "", "");

            // Expenses (group 5)
            dgvReport.Rows.Add("", "=== BIAYA ===", "");
            long totalExpense = AddGroupRows(5, period);
            dgvReport.Rows.Add("", "TOTAL BIAYA", Formatting.FormatMoney(totalExpense));

            dgvReport.Rows.Add("", "", "");
            long netIncome = totalRevenue - totalExpense;
            dgvReport.Rows.Add("", "LABA/RUGI BERSIH", Formatting.FormatMoney(netIncome));

            lblTotal.Text = string.Format("Pendapatan: {0}  Biaya: {1}  Laba: {2}",
                Formatting.FormatMoney(totalRevenue), Formatting.FormatMoney(totalExpense),
                Formatting.FormatMoney(netIncome));
        }

        private void GenerateApAging()
        {
            dgvReport.Columns.Add("Vendor", "Supplier");
            dgvReport.Columns.Add("Name", "Nama");
            dgvReport.Columns.Add("Current", "Lancar");
            dgvReport.Columns.Add("D30", "30 Hari");
            dgvReport.Columns.Add("D60", "60 Hari");
            dgvReport.Columns.Add("D90", "90 Hari");
            dgvReport.Columns.Add("D120", "120+ Hari");
            dgvReport.Columns.Add("Total", "Total");

            foreach (DataGridViewColumn col in dgvReport.Columns)
                col.FillWeight = col.Name == "Name" ? 200 : 120;

            string asOfDate = DateTime.Now.ToString("yyyy-MM-dd");
            var aging = _payablesService.GetAgingReport(asOfDate);

            long grandTotal = 0;
            foreach (var b in aging)
            {
                dgvReport.Rows.Add(b.VendorCode, b.VendorName,
                    Formatting.FormatMoney(b.Current),
                    Formatting.FormatMoney(b.Days30),
                    Formatting.FormatMoney(b.Days60),
                    Formatting.FormatMoney(b.Days90),
                    Formatting.FormatMoney(b.Days120Plus),
                    Formatting.FormatMoney(b.Total));
                grandTotal += b.Total;
            }

            lblTotal.Text = "Total Hutang: " + Formatting.FormatMoney(grandTotal);
        }

        private void GenerateGlDetail()
        {
            dgvReport.Columns.Add("Date", "Tanggal");
            dgvReport.Columns.Add("JournalNo", "No. Jurnal");
            dgvReport.Columns.Add("Account", "Akun");
            dgvReport.Columns.Add("Remark", "Keterangan");
            dgvReport.Columns.Add("Debit", "Debit");
            dgvReport.Columns.Add("Credit", "Kredit");

            dgvReport.Columns["Date"].FillWeight = 100;
            dgvReport.Columns["JournalNo"].FillWeight = 150;
            dgvReport.Columns["Account"].FillWeight = 100;
            dgvReport.Columns["Remark"].FillWeight = 200;
            dgvReport.Columns["Debit"].FillWeight = 130;
            dgvReport.Columns["Credit"].FillWeight = 130;

            string period = txtPeriod.Text.Trim();
            var details = _glRepo.GetByPeriod(period);

            long totalDebit = 0, totalCredit = 0;
            foreach (var d in details)
            {
                dgvReport.Rows.Add(d.DocDate, d.JournalNo, d.AccountCode, d.Remark,
                    d.Debit > 0 ? Formatting.FormatMoney(d.Debit) : "",
                    d.Credit > 0 ? Formatting.FormatMoney(d.Credit) : "");
                totalDebit += d.Debit;
                totalCredit += d.Credit;
            }

            lblTotal.Text = string.Format("{0} entries — Debit: {1}  Kredit: {2}",
                details.Count, Formatting.FormatMoney(totalDebit), Formatting.FormatMoney(totalCredit));
        }

        private long AddGroupRows(int accountGroup, string period)
        {
            var accounts = _accountRepo.GetByGroup(accountGroup);
            long total = 0;

            foreach (var acc in accounts)
            {
                if (acc.IsDetail != 1) continue;
                var bal = _balanceRepo.GetBalance(acc.AccountCode, period);
                if (bal == null) continue;

                long net = bal.OpeningBalance + bal.DebitTotal - bal.CreditTotal;
                if (acc.NormalBalance == "K") net = -net; // credit-normal → positive when credit > debit

                if (net == 0) continue;

                dgvReport.Rows.Add(acc.AccountCode, acc.AccountName, Formatting.FormatMoney(net));
                total += net;
            }

            return total;
        }

        private void ExportToExcel()
        {
            if (dgvReport.Rows.Count == 0)
            {
                MessageBox.Show("Generate report first (F5).", "Info");
                return;
            }

            try
            {
                string reportName = cboReportType.SelectedItem.ToString().Split('(')[0].Trim();
                string fileName = string.Format("{0}_{1}.xlsx", reportName.Replace(' ', '_'), txtPeriod.Text);
                string path = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Desktop), fileName);

                ExcelExporter.ExportDataGridView(dgvReport, path, reportName);
                MessageBox.Show("Exported to: " + path, "Export Success",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Export failed: " + ex.Message, "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
