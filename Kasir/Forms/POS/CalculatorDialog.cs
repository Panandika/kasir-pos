using System;
using System.Drawing;
using System.Windows.Forms;
using Kasir.Forms;

namespace Kasir.Forms.POS
{
    public class CalculatorDialog : Form
    {
        private TextBox txtA;
        private TextBox txtB;
        private Label lblMultResult;
        private TextBox txtC;
        private TextBox txtD;
        private Label lblAddResult;

        public CalculatorDialog()
        {
            InitializeLayout();
        }

        private void InitializeLayout()
        {
            this.Text = "Calculator";
            this.Size = new Size(400, 250);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.BackColor = ThemeConstants.BgPrimary;
            this.ForeColor = ThemeConstants.FgPrimary;
            this.Font = ThemeConstants.FontMain;
            this.KeyPreview = true;

            int y = 20;
            int lblX = 15;
            int inputW = 100;

            // Row 1: A x B = result
            txtA = CreateInput(lblX, y, inputW);
            var lblTimes = new Label
            {
                Text = "\u00D7",
                Location = new Point(lblX + inputW + 10, y + 3),
                AutoSize = true,
                ForeColor = ThemeConstants.FgWhite
            };
            txtB = CreateInput(lblX + inputW + 35, y, inputW);
            var lblEquals1 = new Label
            {
                Text = "=",
                Location = new Point(lblX + inputW * 2 + 45, y + 3),
                AutoSize = true,
                ForeColor = ThemeConstants.FgWhite
            };
            lblMultResult = new Label
            {
                Text = "0",
                Location = new Point(lblX + inputW * 2 + 70, y + 3),
                AutoSize = true,
                ForeColor = ThemeConstants.FgWarning,
                Font = ThemeConstants.FontHeader
            };

            y += 50;

            // Row 2: C + D = result
            txtC = CreateInput(lblX, y, inputW);
            var lblPlus = new Label
            {
                Text = "+",
                Location = new Point(lblX + inputW + 10, y + 3),
                AutoSize = true,
                ForeColor = ThemeConstants.FgWhite
            };
            txtD = CreateInput(lblX + inputW + 35, y, inputW);
            var lblEquals2 = new Label
            {
                Text = "=",
                Location = new Point(lblX + inputW * 2 + 45, y + 3),
                AutoSize = true,
                ForeColor = ThemeConstants.FgWhite
            };
            lblAddResult = new Label
            {
                Text = "0",
                Location = new Point(lblX + inputW * 2 + 70, y + 3),
                AutoSize = true,
                ForeColor = ThemeConstants.FgWarning,
                Font = ThemeConstants.FontHeader
            };

            y += 60;

            // OK / Cancel buttons
            var btnOk = new Button
            {
                Text = "OK",
                Location = new Point(80, y),
                Size = new Size(100, 35),
                BackColor = ThemeConstants.BtnSecondary,
                ForeColor = ThemeConstants.FgWhite,
                FlatStyle = FlatStyle.Flat,
                DialogResult = DialogResult.OK
            };

            var btnCancel = new Button
            {
                Text = "Tutup",
                Location = new Point(200, y),
                Size = new Size(100, 35),
                BackColor = ThemeConstants.BtnDanger,
                ForeColor = ThemeConstants.FgWhite,
                FlatStyle = FlatStyle.Flat,
                DialogResult = DialogResult.Cancel
            };

            this.Controls.AddRange(new Control[] {
                txtA, lblTimes, txtB, lblEquals1, lblMultResult,
                txtC, lblPlus, txtD, lblEquals2, lblAddResult,
                btnOk, btnCancel
            });

            this.AcceptButton = btnOk;
            this.CancelButton = btnCancel;

            // Wire up live calculation
            txtA.TextChanged += (s, e) => UpdateCalc();
            txtB.TextChanged += (s, e) => UpdateCalc();
            txtC.TextChanged += (s, e) => UpdateCalc();
            txtD.TextChanged += (s, e) => UpdateCalc();
        }

        private TextBox CreateInput(int x, int y, int w)
        {
            var txt = new TextBox
            {
                Location = new Point(x, y),
                Width = w,
                BackColor = ThemeConstants.BgInput,
                ForeColor = ThemeConstants.FgPrimary,
                Font = ThemeConstants.FontMain,
                TextAlign = HorizontalAlignment.Right,
                Text = "0"
            };
            BaseForm.ApplyFocusIndicator(txt);
            return txt;
        }

        private void UpdateCalc()
        {
            long a = ParseNum(txtA.Text);
            long b = ParseNum(txtB.Text);
            try
            {
                lblMultResult.Text = checked(a * b).ToString("N0");
            }
            catch (OverflowException)
            {
                lblMultResult.Text = "OVERFLOW";
            }

            long c = ParseNum(txtC.Text);
            long d = ParseNum(txtD.Text);
            lblAddResult.Text = (c + d).ToString("N0");
        }

        private static long ParseNum(string text)
        {
            long val;
            if (long.TryParse(text.Replace(",", "").Replace(".", ""), out val))
            {
                return val;
            }
            return 0;
        }
    }
}
