using System;
using System.Drawing;
using System.Windows.Forms;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Hardware;

namespace Kasir.Forms.Admin
{
    public class PrinterConfigForm : BaseForm
    {
        private TextBox txtPrinterName;
        private ConfigRepository _configRepo;

        public PrinterConfigForm()
        {
            _configRepo = new ConfigRepository(DbConnection.GetConnection());
            InitializeLayout();
            SetAction("Printer Configuration — Test and save printer settings");
        }

        private void InitializeLayout()
        {
            var pnl = new Panel
            {
                Size = new Size(500, 300),
                BackColor = Color.FromArgb(10, 10, 10)
            };

            var lblTitle = new Label
            {
                Text = "Printer Configuration",
                Font = new Font("Consolas", 16f, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 255, 0),
                Location = new Point(15, 15),
                AutoSize = true
            };

            var lblName = new Label
            {
                Text = "Printer Name (from Devices and Printers):",
                ForeColor = Color.Gray,
                Location = new Point(15, 55),
                AutoSize = true
            };

            txtPrinterName = new TextBox
            {
                Location = new Point(15, 80),
                Width = 460,
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.FromArgb(0, 255, 0),
                Font = new Font("Consolas", 14f),
                Text = _configRepo.Get("printer_name") ?? ""
            };

            var btnTestPrint = CreateActionButton("Test Print", new Point(15, 130));
            btnTestPrint.Click += BtnTestPrint_Click;

            var btnTestDrawer = CreateActionButton("Test Drawer", new Point(180, 130));
            btnTestDrawer.Click += BtnTestDrawer_Click;

            var btnSave = CreateActionButton("Save", new Point(345, 130));
            btnSave.BackColor = Color.FromArgb(0, 100, 0);
            btnSave.Click += BtnSave_Click;

            pnl.Controls.AddRange(new Control[] {
                lblTitle, lblName, txtPrinterName, btnTestPrint, btnTestDrawer, btnSave
            });

            this.Load += (s, e) =>
            {
                pnl.Location = new Point(
                    (this.ClientSize.Width - pnl.Width) / 2,
                    (this.ClientSize.Height - pnl.Height) / 2);
            };

            this.Controls.Add(pnl);
        }

        private Button CreateActionButton(string text, Point location)
        {
            return new Button
            {
                Text = text,
                Location = location,
                Size = new Size(150, 35),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(0, 60, 0),
                FlatStyle = FlatStyle.Flat
            };
        }

        private void BtnTestPrint_Click(object sender, EventArgs e)
        {
            string name = txtPrinterName.Text.Trim();
            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show("Enter a printer name first.", "Error");
                return;
            }

            var printer = new ReceiptPrinter(name);
            string storeName = _configRepo.Get("store_name") ?? "TEST STORE";
            bool success = printer.PrintTestReceipt(storeName);

            MessageBox.Show(
                success ? "Test print sent successfully!" : "Print failed. Check printer name.",
                success ? "Success" : "Error");
        }

        private void BtnTestDrawer_Click(object sender, EventArgs e)
        {
            string name = txtPrinterName.Text.Trim();
            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show("Enter a printer name first.", "Error");
                return;
            }

            var drawer = new CashDrawer(name);
            bool success = drawer.Open();

            MessageBox.Show(
                success ? "Cash drawer command sent!" : "Drawer command failed.",
                success ? "Success" : "Error");
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            _configRepo.Set("printer_name", txtPrinterName.Text.Trim());
            MessageBox.Show("Printer name saved.", "Success");
        }
    }
}
