using System;
using System.Drawing;
using System.Windows.Forms;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Forms;
using Kasir.Models;
using Kasir.Services;
using Kasir.Utils;

namespace Kasir.Forms.POS
{
    public class PaymentForm : Form
    {
        private long _totalDue;
        private TextBox txtCash;
        private TextBox txtCard;
        private TextBox txtVoucher;
        private ComboBox cboCardType;
        private Label lblChange;
        private Label lblTotal;
        private Button btnOk;
        private PaymentCalculator _paymentCalc;
        private CreditCardRepository _cardRepo;

        public long CashAmount { get; private set; }
        public long CardAmount { get; private set; }
        public long VoucherAmount { get; private set; }
        public string CardCode { get; private set; }
        public string CardType { get; private set; }
        public long Change { get; private set; }

        public PaymentForm(long totalDue)
        {
            _totalDue = totalDue;
            _paymentCalc = new PaymentCalculator();
            _cardRepo = new CreditCardRepository(DbConnection.GetConnection());
            InitializeLayout();
        }

        private void InitializeLayout()
        {
            this.Text = "Payment";
            this.Size = new Size(450, 400);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.BackColor = ThemeConstants.BgDialog;
            this.ForeColor = ThemeConstants.FgPrimary;
            this.Font = ThemeConstants.FontMenu;

            lblTotal = new Label
            {
                Text = string.Format("TOTAL: {0}", Formatting.FormatCurrency(_totalDue)),
                Font = ThemeConstants.FontHeader,
                ForeColor = ThemeConstants.FgWhite,
                Location = new Point(15, 15),
                AutoSize = true
            };

            var lblCash = new Label { Text = "Cash (Rp):", Location = new Point(15, 60), AutoSize = true, ForeColor = ThemeConstants.FgLabel };
            txtCash = CreateInput(new Point(15, 85));
            txtCash.TextChanged += RecalculateChange;
            txtCash.Text = (_totalDue / 100).ToString();

            var lblCard = new Label { Text = "Card (Rp):", Location = new Point(15, 125), AutoSize = true, ForeColor = ThemeConstants.FgLabel };
            txtCard = CreateInput(new Point(15, 150));
            txtCard.TextChanged += RecalculateChange;
            txtCard.Text = "0";

            var lblCardType = new Label { Text = "Card Type:", Location = new Point(230, 125), AutoSize = true, ForeColor = ThemeConstants.FgLabel };
            cboCardType = new ComboBox
            {
                Location = new Point(230, 150),
                Width = 180,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = ThemeConstants.BgInput,
                ForeColor = ThemeConstants.FgPrimary
            };

            var cards = _cardRepo.GetAll();
            cboCardType.Items.Add("(none)");
            foreach (var card in cards)
            {
                cboCardType.Items.Add(string.Format("{0} ({1}%)", card.Name, (card.FeePct / 100.0).ToString("F1")));
            }
            cboCardType.SelectedIndex = 0;

            var lblVoucher = new Label { Text = "Voucher (Rp):", Location = new Point(15, 190), AutoSize = true, ForeColor = ThemeConstants.FgLabel };
            txtVoucher = CreateInput(new Point(15, 215));
            txtVoucher.TextChanged += RecalculateChange;
            txtVoucher.Text = "0";

            lblChange = new Label
            {
                Text = "CHANGE: Rp 0",
                Font = ThemeConstants.FontHeader,
                ForeColor = ThemeConstants.FgWarning,
                Location = new Point(15, 260),
                AutoSize = true
            };

            btnOk = new Button
            {
                Text = "CONFIRM PAYMENT (Enter)",
                Location = new Point(15, 310),
                Size = new Size(400, 40),
                ForeColor = ThemeConstants.FgWhite,
                BackColor = ThemeConstants.BtnPrimary,
                FlatStyle = FlatStyle.Flat,
                Font = ThemeConstants.FontInputSmall
            };
            btnOk.Click += BtnOk_Click;

            this.AcceptButton = btnOk;

            var btnCancel = new Button
            {
                Text = "Cancel (Esc)",
                DialogResult = DialogResult.Cancel,
                Location = new Point(320, 15),
                Size = new Size(100, 30),
                ForeColor = ThemeConstants.FgWhite,
                BackColor = ThemeConstants.BtnDanger,
                FlatStyle = FlatStyle.Flat
            };
            this.CancelButton = btnCancel;

            this.Controls.AddRange(new Control[] {
                lblTotal, lblCash, txtCash, lblCard, txtCard, lblCardType, cboCardType,
                lblVoucher, txtVoucher, lblChange, btnOk, btnCancel
            });

            RecalculateChange(null, null);
        }

        private TextBox CreateInput(Point location)
        {
            return new TextBox
            {
                Location = location,
                Width = 200,
                BackColor = ThemeConstants.BgInput,
                ForeColor = ThemeConstants.FgPrimary,
                Font = ThemeConstants.FontMain,
                TextAlign = HorizontalAlignment.Right
            };
        }

        private void RecalculateChange(object sender, EventArgs e)
        {
            long cash = ParseAmount(txtCash.Text);
            long card = ParseAmount(txtCard.Text);
            long voucher = ParseAmount(txtVoucher.Text);

            var result = _paymentCalc.ValidatePayment(_totalDue, cash, card, voucher);

            if (result.IsValid)
            {
                lblChange.Text = string.Format("CHANGE: {0}", Formatting.FormatCurrency(result.Change));
                lblChange.ForeColor = ThemeConstants.FgWarning;
                btnOk.Enabled = true;
            }
            else
            {
                lblChange.Text = string.Format("SHORT: {0}", Formatting.FormatCurrency(result.Shortfall));
                lblChange.ForeColor = ThemeConstants.FgError;
                btnOk.Enabled = false;
            }
        }

        private void BtnOk_Click(object sender, EventArgs e)
        {
            CashAmount = ParseAmount(txtCash.Text);
            CardAmount = ParseAmount(txtCard.Text);
            VoucherAmount = ParseAmount(txtVoucher.Text);

            var result = _paymentCalc.ValidatePayment(_totalDue, CashAmount, CardAmount, VoucherAmount);
            if (!result.IsValid)
            {
                MessageBox.Show("Insufficient payment.", "Error");
                return;
            }

            Change = result.Change;

            if (cboCardType.SelectedIndex > 0)
            {
                var cards = _cardRepo.GetAll();
                var selectedCard = cards[cboCardType.SelectedIndex - 1];
                CardCode = selectedCard.CardCode;
                CardType = "C";
            }
            else
            {
                CardCode = "";
                CardType = "";
            }

            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        // Indonesian convention: period is thousand separator (e.g., 100.000 = Rp 100,000).
        // Both period and comma are stripped as separators; input is treated as whole Rupiah.
        private static long ParseAmount(string text)
        {
            long value;
            if (long.TryParse(text.Replace(".", "").Replace(",", ""), out value))
            {
                return value * 100; // Convert to cents
            }
            return 0;
        }
    }
}
