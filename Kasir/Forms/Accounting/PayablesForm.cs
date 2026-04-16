using System;
using System.Drawing;
using System.Windows.Forms;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Models;
using Kasir.Services;
using Kasir.Utils;

namespace Kasir.Forms.Accounting
{
    public class PayablesForm : BaseForm
    {
        private TextBox txtVendor;
        private DataGridView dgvPayables;
        private Label lblTotal;
        private TextBox txtPayment;
        private PayablesService _payablesService;
        private SubsidiaryRepository _vendorRepo;
        private string _selectedVendor;

        public PayablesForm()
        {
            var conn = DbConnection.GetConnection();
            _payablesService = new PayablesService(conn);
            _vendorRepo = new SubsidiaryRepository(conn);
            InitializeLayout();
            SetAction("Pembayaran Hutang — F2: Cari Supplier, F5: Bayar, Esc: Keluar");
        }

        private void InitializeLayout()
        {
            var pnlHeader = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = ThemeConstants.BgFooter };
            var lblVendor = new Label { Text = "Supplier:", Location = new Point(5, 8), AutoSize = true, ForeColor = ThemeConstants.FgLabel };
            txtVendor = new TextBox
            {
                Location = new Point(80, 5), Width = 300,
                BackColor = ThemeConstants.BgInput, ForeColor = ThemeConstants.FgPrimary,
                Font = ThemeConstants.FontInputSmall
            };
            txtVendor.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { LoadVendorPayables(); e.Handled = true; e.SuppressKeyPress = true; }
            };
            ApplyFocusIndicator(txtVendor);
            pnlHeader.Controls.AddRange(new Control[] { lblVendor, txtVendor });

            dgvPayables = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true };
            ApplyGridTheme(dgvPayables);

            dgvPayables.Columns.Add("JournalNo", "No. Faktur");
            dgvPayables.Columns.Add("DocDate", "Tanggal");
            dgvPayables.Columns.Add("DueDate", "Jatuh Tempo");
            dgvPayables.Columns.Add("Amount", "Jumlah");
            dgvPayables.Columns.Add("Paid", "Dibayar");
            dgvPayables.Columns.Add("Remaining", "Sisa");

            dgvPayables.Columns["JournalNo"].FillWeight = 150;
            dgvPayables.Columns["DocDate"].FillWeight = 120;
            dgvPayables.Columns["DueDate"].FillWeight = 120;
            dgvPayables.Columns["Amount"].FillWeight = 130;
            dgvPayables.Columns["Paid"].FillWeight = 130;
            dgvPayables.Columns["Remaining"].FillWeight = 130;

            var pnlBottom = new Panel { Dock = DockStyle.Bottom, Height = 45, BackColor = ThemeConstants.BgFooter };
            lblTotal = new Label { Text = "Total Hutang: 0", Location = new Point(5, 8), Width = 300, ForeColor = ThemeConstants.FgSuccess };
            var lblPay = new Label { Text = "Bayar:", Location = new Point(320, 8), AutoSize = true, ForeColor = ThemeConstants.FgLabel };
            txtPayment = new TextBox
            {
                Location = new Point(380, 5), Width = 200,
                BackColor = ThemeConstants.BgInput, ForeColor = ThemeConstants.FgPrimary,
                Font = ThemeConstants.FontInputSmall
            };
            ApplyFocusIndicator(txtPayment);
            pnlBottom.Controls.AddRange(new Control[] { lblTotal, lblPay, txtPayment });

            this.Controls.Add(dgvPayables);
            this.Controls.Add(pnlBottom);
            this.Controls.Add(pnlHeader);
        }

        private void LoadVendorPayables()
        {
            _selectedVendor = txtVendor.Text.Trim();
            if (string.IsNullOrEmpty(_selectedVendor)) return;

            dgvPayables.Rows.Clear();
            var outstanding = _payablesService.GetOutstanding(_selectedVendor);
            long total = 0;

            foreach (var p in outstanding)
            {
                long remaining = p.Amount - p.PaymentAmount;
                dgvPayables.Rows.Add(p.JournalNo, p.DocDate, p.DueDate,
                    Formatting.FormatMoney(p.Amount),
                    Formatting.FormatMoney(p.PaymentAmount),
                    Formatting.FormatMoney(remaining));
                total += remaining;
            }

            lblTotal.Text = "Total Hutang: " + Formatting.FormatMoney(total);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.F2:
                    txtVendor.Focus();
                    return true;

                case Keys.F5:
                    ProcessPayment();
                    return true;

                case Keys.Escape:
                    this.Close();
                    return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void ProcessPayment()
        {
            if (string.IsNullOrEmpty(_selectedVendor)) return;

            long amount;
            if (!long.TryParse(txtPayment.Text, out amount) || amount <= 0)
            {
                MessageBox.Show("Masukkan jumlah pembayaran yang valid.", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                string periodCode = DateTime.Now.ToString("yyyyMM");
                var result = _payablesService.AllocatePayment(_selectedVendor, amount, "1100",
                    DateTime.Now.ToString("yyyy-MM-dd"), periodCode, 1, null);

                MessageBox.Show(
                    string.Format("Pembayaran: {0}\nFaktur lunas: {1}\nFaktur sebagian: {2}\nSisa: {3}",
                        Formatting.FormatMoney(result.AmountAllocated),
                        result.InvoicesPaid,
                        result.InvoicesPartiallyPaid,
                        Formatting.FormatMoney(result.AmountRemaining)),
                    "Sukses", MessageBoxButtons.OK, MessageBoxIcon.Information);

                txtPayment.Text = "";
                LoadVendorPayables();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Gagal: " + ex.Message, "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
