using System;
using System.Drawing;
using System.Windows.Forms;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Forms.Shared;
using Kasir.Models;
using Kasir.Utils;

namespace Kasir.Forms.POS
{
    public class ShiftForm : BaseForm
    {
        private ShiftRepository _shiftRepo;
        private ConfigRepository _configRepo;
        private SaleRepository _saleRepo;
        private Label lblStatus;
        private Label lblInfo;
        private int _cashierId;
        private Shift _currentShift;

        public Shift CurrentShift
        {
            get { return _currentShift; }
        }

        public ShiftForm(int cashierId)
        {
            _cashierId = cashierId;
            var conn = DbConnection.GetConnection();
            _shiftRepo = new ShiftRepository(conn);
            _configRepo = new ConfigRepository(conn);
            _saleRepo = new SaleRepository(conn);
            InitializeLayout();
            RefreshStatus();
        }

        private void InitializeLayout()
        {
            SetAction("Shift Management — F1: Open Shift, F2: Close Shift, Esc: Back");

            var pnl = new Panel
            {
                Size = new Size(500, 300),
                BackColor = Color.FromArgb(10, 10, 10)
            };

            var lblTitle = new Label
            {
                Text = "SHIFT MANAGEMENT",
                Font = new Font("Consolas", 18f, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 255, 0),
                Location = new Point(15, 15),
                AutoSize = true
            };

            lblStatus = new Label
            {
                Location = new Point(15, 60),
                Size = new Size(470, 30),
                Font = new Font("Consolas", 14f),
                ForeColor = Color.Yellow
            };

            lblInfo = new Label
            {
                Location = new Point(15, 100),
                Size = new Size(470, 120),
                ForeColor = Color.Gray,
                Font = new Font("Consolas", 11f)
            };

            var btnOpen = new Button
            {
                Text = "F1 - Open Shift",
                Location = new Point(15, 230),
                Size = new Size(220, 40),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(0, 100, 0),
                FlatStyle = FlatStyle.Flat
            };
            btnOpen.Click += (s, e) => OpenShift();

            var btnClose = new Button
            {
                Text = "F2 - Close Shift",
                Location = new Point(250, 230),
                Size = new Size(220, 40),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(100, 0, 0),
                FlatStyle = FlatStyle.Flat
            };
            btnClose.Click += (s, e) => CloseShift();

            pnl.Controls.AddRange(new Control[] { lblTitle, lblStatus, lblInfo, btnOpen, btnClose });

            this.Load += (s, e) =>
            {
                pnl.Location = new Point(
                    (this.ClientSize.Width - pnl.Width) / 2,
                    (this.ClientSize.Height - pnl.Height) / 2);
            };

            this.Controls.Add(pnl);
        }

        private void RefreshStatus()
        {
            string registerId = _configRepo.Get("register_id") ?? "01";
            _currentShift = _shiftRepo.GetOpenShift(registerId);

            if (_currentShift != null)
            {
                lblStatus.Text = string.Format("Shift {0} — OPEN", _currentShift.ShiftNumber);
                lblStatus.ForeColor = Color.FromArgb(0, 255, 0);
                lblInfo.Text = string.Format(
                    "Opened: {0}\nCashier ID: {1}\nOpening Cash: {2}",
                    _currentShift.OpenedAt,
                    _currentShift.CashierId,
                    Formatting.FormatCurrency(_currentShift.OpeningCash));
            }
            else
            {
                lblStatus.Text = "No shift open";
                lblStatus.ForeColor = Color.Red;
                lblInfo.Text = "Press F1 to open a new shift.";
            }
        }

        private void OpenShift()
        {
            string registerId = _configRepo.Get("register_id") ?? "01";

            if (_shiftRepo.GetOpenShift(registerId) != null)
            {
                MessageBox.Show("A shift is already open. Close it first.", "Error");
                return;
            }

            string cashStr = InputDialog.ShowSingleInput(this,
                "Open Shift", "Opening cash amount (Rp, no decimals)", "0");

            if (cashStr == null) return;

            long openingCash;
            if (!long.TryParse(cashStr, out openingCash))
            {
                MessageBox.Show("Invalid amount.", "Error");
                return;
            }

            var shift = new Shift
            {
                RegisterId = registerId,
                ShiftNumber = "1",
                CashierId = _cashierId,
                OpenedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                OpeningCash = openingCash * 100 // Convert to cents
            };

            _shiftRepo.OpenShift(shift);
            RefreshStatus();
            MessageBox.Show("Shift opened.", "Success");
        }

        private void CloseShift()
        {
            if (_currentShift == null)
            {
                MessageBox.Show("No shift is open.", "Error");
                return;
            }

            // Calculate expected cash
            string today = DateTime.Now.ToString("yyyy-MM-dd");
            long dailyCashSales = _saleRepo.GetDailyTotal(today);
            long expectedCash = _currentShift.OpeningCash + dailyCashSales;

            string cashStr = InputDialog.ShowSingleInput(this,
                "Close Shift",
                string.Format("Count cash in drawer (expected: {0})",
                    Formatting.FormatCurrency(expectedCash)),
                (expectedCash / 100).ToString());

            if (cashStr == null) return;

            long closingCash;
            if (!long.TryParse(cashStr, out closingCash))
            {
                MessageBox.Show("Invalid amount.", "Error");
                return;
            }

            closingCash = closingCash * 100; // Convert to cents

            _shiftRepo.CloseShift(_currentShift.Id, closingCash, expectedCash);

            long variance = closingCash - expectedCash;
            string varianceText = variance == 0
                ? "No variance."
                : string.Format("Variance: {0}{1}",
                    variance > 0 ? "+" : "",
                    Formatting.FormatCurrency(variance));

            MessageBox.Show(
                string.Format("Shift closed.\n\nExpected: {0}\nCounted: {1}\n{2}",
                    Formatting.FormatCurrency(expectedCash),
                    Formatting.FormatCurrency(closingCash),
                    varianceText),
                "Shift Closed");

            RefreshStatus();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.F1:
                    OpenShift();
                    return true;
                case Keys.F2:
                    CloseShift();
                    return true;
                case Keys.Escape:
                    this.Close();
                    return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
