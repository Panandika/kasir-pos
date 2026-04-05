using System;
using System.Drawing;
using System.Windows.Forms;

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
            this.BackColor = Color.Black;
            this.ForeColor = Color.FromArgb(0, 255, 0);
            this.Font = new Font("Consolas", 14f);
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
                ForeColor = Color.White
            };
            txtB = CreateInput(lblX + inputW + 35, y, inputW);
            var lblEquals1 = new Label
            {
                Text = "=",
                Location = new Point(lblX + inputW * 2 + 45, y + 3),
                AutoSize = true,
                ForeColor = Color.White
            };
            lblMultResult = new Label
            {
                Text = "0",
                Location = new Point(lblX + inputW * 2 + 70, y + 3),
                AutoSize = true,
                ForeColor = Color.Yellow,
                Font = new Font("Consolas", 14f, FontStyle.Bold)
            };

            y += 50;

            // Row 2: C + D = result
            txtC = CreateInput(lblX, y, inputW);
            var lblPlus = new Label
            {
                Text = "+",
                Location = new Point(lblX + inputW + 10, y + 3),
                AutoSize = true,
                ForeColor = Color.White
            };
            txtD = CreateInput(lblX + inputW + 35, y, inputW);
            var lblEquals2 = new Label
            {
                Text = "=",
                Location = new Point(lblX + inputW * 2 + 45, y + 3),
                AutoSize = true,
                ForeColor = Color.White
            };
            lblAddResult = new Label
            {
                Text = "0",
                Location = new Point(lblX + inputW * 2 + 70, y + 3),
                AutoSize = true,
                ForeColor = Color.Yellow,
                Font = new Font("Consolas", 14f, FontStyle.Bold)
            };

            y += 60;

            // OK / Cancel buttons
            var btnOk = new Button
            {
                Text = "OK",
                Location = new Point(80, y),
                Size = new Size(100, 35),
                BackColor = Color.FromArgb(0, 60, 0),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                DialogResult = DialogResult.OK
            };

            var btnCancel = new Button
            {
                Text = "Tutup",
                Location = new Point(200, y),
                Size = new Size(100, 35),
                BackColor = Color.FromArgb(60, 0, 0),
                ForeColor = Color.White,
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
                BackColor = Color.FromArgb(20, 20, 20),
                ForeColor = Color.FromArgb(0, 255, 0),
                Font = new Font("Consolas", 14f),
                TextAlign = HorizontalAlignment.Right,
                Text = "0"
            };
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
