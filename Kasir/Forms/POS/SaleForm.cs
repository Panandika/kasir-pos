using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Kasir.Auth;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Forms.Shared;
using Kasir.Hardware;
using Kasir.Models;
using Kasir.Services;
using Kasir.Utils;

namespace Kasir.Forms.POS
{
    public class SaleForm : BaseForm
    {
        private TextBox txtBarcode;
        private DataGridView dgvItems;
        private Label lblSubtotal;
        private Label lblDiscount;
        private Label lblGrandTotal;
        private Label lblFooter;
        private Label lblSyncStatus;
        private SalesService _salesService;
        private ConfigRepository _configRepo;
        private ShiftRepository _shiftRepo;
        private AuthService _auth;
        private PermissionService _perms;
        private int _dailyCount;
        private Shift _currentShift;

        public SaleForm(AuthService auth)
        {
            _auth = auth;
            _perms = new PermissionService();
            var conn = DbConnection.GetConnection();
            _configRepo = new ConfigRepository(conn);
            _shiftRepo = new ShiftRepository(conn);
            _salesService = new SalesService(conn, new ClockImpl());
            _salesService.SetCashier(
                _auth.CurrentUser.Alias,
                _auth.CurrentUser.Id);

            InitializeLayout();
            CheckShift();
            UpdateTotals();
            UpdateFooter();
        }

        private void InitializeLayout()
        {
            SetAction("POS — Scan barcode or type code, Enter to add. F1=Search F5=Payment F10=Drawer Esc=Cancel");

            // Barcode input
            var pnlTop = new Panel
            {
                Dock = DockStyle.Top,
                Height = 40,
                BackColor = Color.FromArgb(0, 30, 0)
            };

            var lblScan = new Label
            {
                Text = "KODE:",
                Location = new Point(5, 10),
                AutoSize = true,
                ForeColor = Color.Gray
            };

            txtBarcode = new TextBox
            {
                Location = new Point(70, 5),
                Width = 300,
                BackColor = Color.FromArgb(20, 20, 20),
                ForeColor = Color.FromArgb(0, 255, 0),
                Font = new Font("Consolas", 16f)
            };
            txtBarcode.KeyDown += TxtBarcode_KeyDown;

            pnlTop.Controls.AddRange(new Control[] { lblScan, txtBarcode });

            // Items DataGridView
            dgvItems = new DataGridView { Dock = DockStyle.Fill };
            ApplyGridTheme(dgvItems);
            dgvItems.ReadOnly = true;

            dgvItems.Columns.Add("No", "NO");
            dgvItems.Columns.Add("Code", "KODE BARANG");
            dgvItems.Columns.Add("Name", "NAMA BARANG");
            dgvItems.Columns.Add("Qty", "QTY");
            dgvItems.Columns.Add("Price", "HARGA");
            dgvItems.Columns.Add("Disc", "DISC%");
            dgvItems.Columns.Add("Total", "TOTAL");

            dgvItems.Columns["No"].Width = 40;
            dgvItems.Columns["Code"].Width = 150;
            dgvItems.Columns["Name"].Width = 250;
            dgvItems.Columns["Qty"].Width = 60;
            dgvItems.Columns["Price"].Width = 100;
            dgvItems.Columns["Disc"].Width = 60;
            dgvItems.Columns["Total"].Width = 120;

            // Totals panel
            var pnlTotals = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 80,
                BackColor = Color.FromArgb(0, 20, 0)
            };

            lblSubtotal = new Label
            {
                Text = "SUBTOTAL: Rp 0",
                Location = new Point(10, 5),
                AutoSize = true,
                ForeColor = Color.FromArgb(0, 200, 0),
                Font = new Font("Consolas", 12f)
            };

            lblDiscount = new Label
            {
                Text = "DISC: Rp 0",
                Location = new Point(10, 25),
                AutoSize = true,
                ForeColor = Color.Yellow,
                Font = new Font("Consolas", 12f)
            };

            lblGrandTotal = new Label
            {
                Text = "TOTAL: Rp 0",
                Location = new Point(10, 50),
                AutoSize = true,
                ForeColor = Color.White,
                Font = new Font("Consolas", 18f, FontStyle.Bold)
            };

            pnlTotals.Controls.AddRange(new Control[] { lblSubtotal, lblDiscount, lblGrandTotal });

            // Footer panel
            var pnlFooter = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 25,
                BackColor = Color.FromArgb(0, 30, 0)
            };

            lblFooter = new Label
            {
                Dock = DockStyle.Left,
                AutoSize = true,
                ForeColor = Color.FromArgb(0, 150, 0),
                Font = new Font("Consolas", 9f)
            };

            lblSyncStatus = new Label
            {
                Dock = DockStyle.Right,
                AutoSize = true,
                ForeColor = Color.FromArgb(0, 150, 0),
                Font = new Font("Consolas", 9f),
                Text = "SYNC: --"
            };

            pnlFooter.Controls.AddRange(new Control[] { lblFooter, lblSyncStatus });

            // Add controls (order matters for Dock)
            this.Controls.Add(dgvItems);
            this.Controls.Add(pnlTop);
            this.Controls.Add(pnlTotals);
            this.Controls.Add(pnlFooter);

            txtBarcode.Focus();
        }

        private void CheckShift()
        {
            string registerId = _configRepo.Get("register_id") ?? "01";
            _currentShift = _shiftRepo.GetOpenShift(registerId);

            if (_currentShift != null)
            {
                _salesService.SetShift(_currentShift.ShiftNumber);
            }
        }

        private void TxtBarcode_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                string code = txtBarcode.Text.Trim();
                if (!string.IsNullOrEmpty(code))
                {
                    AddItemByCode(code);
                }
                txtBarcode.Text = "";
                txtBarcode.Focus();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        private void AddItemByCode(string code)
        {
            if (_currentShift == null)
            {
                MessageBox.Show("No shift open. Open a shift first (Utility > Shift).", "Error");
                return;
            }

            var item = _salesService.AddItem(code, 1);
            if (item == null)
            {
                SetAction("Barang tidak ditemukan: " + code);
                return;
            }

            RefreshGrid();
            UpdateTotals();
            SetAction(string.Format("Added: {0} — {1}", item.ProductCode, item.ProductName));
        }

        private void RefreshGrid()
        {
            dgvItems.Rows.Clear();
            int no = 1;
            foreach (var item in _salesService.CurrentItems)
            {
                dgvItems.Rows.Add(
                    no++,
                    item.ProductCode,
                    item.ProductName ?? "",
                    item.Quantity,
                    Formatting.FormatCurrencyShort(item.UnitPrice),
                    item.DiscPct > 0 ? (item.DiscPct / 100.0).ToString("F1") + "%" : "",
                    Formatting.FormatCurrencyShort(item.Value));
            }
        }

        private void UpdateTotals()
        {
            var totals = _salesService.GetTotals();
            lblSubtotal.Text = string.Format("SUBTOTAL: {0}", Formatting.FormatCurrency(totals.GrossAmount));
            lblDiscount.Text = string.Format("DISC: {0}", Formatting.FormatCurrency(totals.TotalDiscount));
            lblGrandTotal.Text = string.Format("TOTAL: {0}", Formatting.FormatCurrency(totals.NetAmount));
        }

        private void UpdateFooter()
        {
            string registerId = _configRepo.Get("register_id") ?? "01";
            string today = DateTime.Now.ToString("yyyy-MM-dd");
            _dailyCount = new SaleRepository(DbConnection.GetConnection()).GetDailyCount(today);

            lblFooter.Text = string.Format("MESIN#{0}  ID#{1}  JMLH: {2}  Shift: {3}",
                registerId,
                _auth.CurrentUser.Id,
                _dailyCount,
                _currentShift != null ? _currentShift.ShiftNumber : "-");
        }

        private void OpenPayment()
        {
            if (_salesService.CurrentItems.Count == 0)
            {
                SetAction("No items to pay.");
                return;
            }

            var totals = _salesService.GetTotals();

            using (var payForm = new PaymentForm(totals.NetAmount))
            {
                if (payForm.ShowDialog(this) == DialogResult.OK)
                {
                    try
                    {
                        var sale = _salesService.CompleteSale(
                            payForm.CashAmount,
                            payForm.CardAmount,
                            payForm.VoucherAmount,
                            payForm.CardCode,
                            payForm.CardType,
                            ""); // memberCode — TODO: add member lookup

                        SetAction(string.Format("SALE COMPLETE: {0} — Change: {1}",
                            sale.JournalNo,
                            Formatting.FormatCurrency(sale.ChangeAmount)));

                        // Print receipt async
                        PrintReceiptAsync(sale);

                        // Open cash drawer
                        if (payForm.CashAmount > 0)
                        {
                            OpenCashDrawer();
                        }

                        // Clear for next customer
                        _salesService.ClearCurrentSale();
                        RefreshGrid();
                        UpdateTotals();
                        UpdateFooter();
                        txtBarcode.Focus();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Payment error: " + ex.Message, "Error");
                    }
                }
            }
        }

        private async void PrintReceiptAsync(Sale sale)
        {
            try
            {
                string printerName = _configRepo.Get("printer_name");
                if (string.IsNullOrEmpty(printerName)) return;

                var printer = new ReceiptPrinter(printerName);
                var items = new SaleRepository(DbConnection.GetConnection())
                    .GetItemsByJournalNo(sale.JournalNo);

                byte[] receiptData = BuildReceiptBytes(sale, items);
                bool success = await Task.Run(() => printer.Print(receiptData));

                if (!success)
                {
                    SetAction("PRINTER ERROR — receipt not printed");
                }
            }
            catch (Exception ex)
            {
                SetAction("Print error: " + ex.Message);
            }
        }

        private byte[] BuildReceiptBytes(Sale sale, List<SaleItem> items)
        {
            string storeName = _configRepo.Get("store_name") ?? "TOKO";
            var receipt = new List<byte[]>();

            receipt.Add(EscPosCommands.Init);
            receipt.Add(EscPosCommands.CenterAlign);
            receipt.Add(EscPosCommands.BoldOn);
            receipt.Add(EscPosCommands.Text(storeName + "\n"));
            receipt.Add(EscPosCommands.BoldOff);
            receipt.Add(EscPosCommands.LeftAlign);
            receipt.Add(EscPosCommands.Text(string.Format("Date: {0}\n", sale.DocDate)));
            receipt.Add(EscPosCommands.Text(string.Format("No: {0}  Cashier: {1}\n", sale.JournalNo, sale.Cashier)));
            receipt.Add(EscPosCommands.Text("================================\n"));

            foreach (var item in items)
            {
                string name = (item.ProductName ?? item.ProductCode);
                if (name.Length > 24) name = name.Substring(0, 24);
                string total = Formatting.FormatCurrencyShort(item.Value);
                receipt.Add(EscPosCommands.Text(string.Format("{0,-24}{1,8}\n", name, total)));
            }

            receipt.Add(EscPosCommands.Text("================================\n"));
            receipt.Add(EscPosCommands.BoldOn);
            receipt.Add(EscPosCommands.Text(string.Format("TOTAL: {0,26}\n",
                Formatting.FormatCurrency(sale.TotalValue))));
            receipt.Add(EscPosCommands.BoldOff);

            if (sale.CashAmount > 0)
            {
                receipt.Add(EscPosCommands.Text(string.Format("TUNAI: {0,26}\n",
                    Formatting.FormatCurrency(sale.CashAmount))));
            }
            if (sale.NonCash > 0)
            {
                receipt.Add(EscPosCommands.Text(string.Format("KARTU: {0,26}\n",
                    Formatting.FormatCurrency(sale.NonCash))));
            }
            if (sale.ChangeAmount > 0)
            {
                receipt.Add(EscPosCommands.Text(string.Format("KEMBALI: {0,24}\n",
                    Formatting.FormatCurrency(sale.ChangeAmount))));
            }

            receipt.Add(EscPosCommands.Text("================================\n"));
            receipt.Add(EscPosCommands.CenterAlign);
            receipt.Add(EscPosCommands.Text("Terima kasih!\n"));
            receipt.Add(EscPosCommands.Text("\n\n\n"));
            receipt.Add(EscPosCommands.PartialCut);

            return receipt.SelectMany(b => b).ToArray();
        }

        private void OpenCashDrawer()
        {
            try
            {
                string printerName = _configRepo.Get("printer_name");
                if (!string.IsNullOrEmpty(printerName))
                {
                    var drawer = new CashDrawer(printerName);
                    drawer.Open();
                }
            }
            catch
            {
                // Drawer failure is non-critical
            }
        }

        private void VoidCurrentItem()
        {
            if (dgvItems.CurrentRow == null) return;
            int index = dgvItems.CurrentRow.Index;

            if (MessageBox.Show("Void item?", "Confirm", MessageBoxButtons.YesNo) == DialogResult.Yes)
            {
                _salesService.RemoveItem(index);
                RefreshGrid();
                UpdateTotals();
            }
        }

        private void ChangeQty()
        {
            if (dgvItems.CurrentRow == null) return;
            int index = dgvItems.CurrentRow.Index;
            var item = _salesService.CurrentItems[index];

            string qtyStr = InputDialog.ShowSingleInput(this,
                "Change Qty", "New quantity for " + item.ProductCode,
                item.Quantity.ToString());

            if (qtyStr == null) return;

            int newQty;
            if (!int.TryParse(qtyStr, out newQty) || newQty <= 0)
            {
                MessageBox.Show("Invalid quantity.", "Error");
                return;
            }

            _salesService.UpdateItemQty(index, newQty);
            RefreshGrid();
            UpdateTotals();
        }

        private void SearchProduct()
        {
            string query = InputDialog.ShowSingleInput(this,
                "Product Search", "Search by name or code", "");

            if (string.IsNullOrEmpty(query)) return;

            var results = new ProductRepository(DbConnection.GetConnection())
                .SearchByText(query, 20);

            if (results.Count == 0)
            {
                MessageBox.Show("No products found.", "Search");
                return;
            }

            if (results.Count == 1)
            {
                AddItemByCode(results[0].ProductCode);
                return;
            }

            // Show selection list
            using (var selectForm = new Form())
            {
                selectForm.Text = "Select Product";
                selectForm.Size = new Size(600, 400);
                selectForm.StartPosition = FormStartPosition.CenterParent;
                selectForm.BackColor = Color.Black;

                var listBox = new ListBox
                {
                    Dock = DockStyle.Fill,
                    BackColor = Color.Black,
                    ForeColor = Color.FromArgb(0, 255, 0),
                    Font = new Font("Consolas", 11f)
                };

                foreach (var p in results)
                {
                    listBox.Items.Add(string.Format("{0} | {1} | {2}",
                        p.ProductCode, p.Name, Formatting.FormatCurrency(p.Price)));
                }

                listBox.DoubleClick += (s, e) =>
                {
                    if (listBox.SelectedIndex >= 0)
                    {
                        selectForm.Tag = results[listBox.SelectedIndex].ProductCode;
                        selectForm.DialogResult = DialogResult.OK;
                    }
                };

                listBox.KeyDown += (s, e) =>
                {
                    if (e.KeyCode == Keys.Enter && listBox.SelectedIndex >= 0)
                    {
                        selectForm.Tag = results[listBox.SelectedIndex].ProductCode;
                        selectForm.DialogResult = DialogResult.OK;
                    }
                    else if (e.KeyCode == Keys.Escape)
                    {
                        selectForm.DialogResult = DialogResult.Cancel;
                    }
                };

                selectForm.Controls.Add(listBox);

                if (selectForm.ShowDialog(this) == DialogResult.OK && selectForm.Tag != null)
                {
                    AddItemByCode(selectForm.Tag.ToString());
                }
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.F1:
                    SearchProduct();
                    return true;
                case Keys.F2:
                    OpenCashDrawer();
                    SetAction("Cash drawer opened");
                    return true;
                case Keys.F3:
                    ChangeQty();
                    return true;
                case Keys.F5:
                    OpenPayment();
                    return true;
                case Keys.F8:
                    VoidCurrentItem();
                    return true;
                case Keys.F10:
                    if (_salesService.CurrentItems.Count > 0)
                    {
                        if (MessageBox.Show("Void entire sale?", "Confirm",
                            MessageBoxButtons.YesNo) == DialogResult.Yes)
                        {
                            _salesService.ClearCurrentSale();
                            RefreshGrid();
                            UpdateTotals();
                            SetAction("Sale voided");
                        }
                    }
                    return true;
                case Keys.Escape:
                    if (_salesService.CurrentItems.Count > 0)
                    {
                        if (MessageBox.Show("Abandon current sale?", "Confirm",
                            MessageBoxButtons.YesNo) != DialogResult.Yes)
                        {
                            return true;
                        }
                    }
                    this.Close();
                    return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
