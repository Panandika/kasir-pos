using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Kasir.Auth;
using Kasir.Data;
using Kasir.Forms;
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
        private Label lblSubtotalHeader;
        private Label lblSubtotalValue;
        private Label lblTotalRow;
        private Label lblTotalCount;
        private Label lblFooter;
        private Label lblFooterClock;
        private SalesService _salesService;
        private SaleRepository _saleRepo;
        private ConfigRepository _configRepo;
        private ShiftRepository _shiftRepo;
        private AuthService _auth;
        private PermissionService _perms;
        private IClock _clock;
        private int _dailyCount;
        private Shift _currentShift;
        private System.Windows.Forms.Timer _posClock;
        private ProductSearchPanel _searchPanel;
        private System.Windows.Forms.Timer _highlightTimer;

        public SaleForm(AuthService auth)
        {
            _auth = auth;
            _perms = new PermissionService();
            _clock = new ClockImpl();
            var conn = DbConnection.GetConnection();
            _configRepo = new ConfigRepository(conn);
            _shiftRepo = new ShiftRepository(conn);
            _saleRepo = new SaleRepository(conn);
            _salesService = new SalesService(conn, _clock);
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
            SetAction("F1=Kode F2=Nama F3=Qty F5=Bayar F8=Void F9=Calc F10=Batal F11=Drawer +=Pas Esc=Keluar");

            // === 1. SUBTOTAL header panel ===
            var pnlHeader = new Panel
            {
                Dock = DockStyle.Top,
                Height = 70,
                BackColor = ThemeConstants.BgPanel
            };

            lblSubtotalHeader = new Label
            {
                Text = "SUBTOTAL",
                Location = new Point(10, 10),
                AutoSize = true,
                ForeColor = ThemeConstants.FgWhite,
                Font = ThemeConstants.FontSubtotalLabel
            };

            lblSubtotalValue = new Label
            {
                Text = "0",
                Dock = DockStyle.Right,
                AutoSize = false,
                Width = 400,
                TextAlign = ContentAlignment.MiddleRight,
                ForeColor = ThemeConstants.FgWhite,
                Font = ThemeConstants.FontSubtotal,
                Padding = new Padding(0, 0, 20, 0)
            };

            pnlHeader.Controls.Add(lblSubtotalValue);
            pnlHeader.Controls.Add(lblSubtotalHeader);

            // === 2. Items DataGridView ===
            dgvItems = new DataGridView { Dock = DockStyle.Fill };
            ApplyGridTheme(dgvItems);
            dgvItems.ReadOnly = true;

            dgvItems.Columns.Add("No", "NO");
            dgvItems.Columns.Add("Code", "KODE BARANG");
            dgvItems.Columns.Add("Name", "NAMA BARANG");
            dgvItems.Columns.Add("Qty", "QTY");
            dgvItems.Columns.Add("Price", "HARGA");
            dgvItems.Columns.Add("Total", "TOTAL");
            dgvItems.Columns.Add("Disc", "DISC");

            dgvItems.Columns["No"].Width = 40;
            dgvItems.Columns["Code"].Width = 150;
            dgvItems.Columns["Name"].Width = 250;
            dgvItems.Columns["Qty"].Width = 60;
            dgvItems.Columns["Price"].Width = 100;
            dgvItems.Columns["Total"].Width = 120;
            dgvItems.Columns["Disc"].Width = 80;

            // === 3. Barcode input panel (below grid) ===
            var pnlInput = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 35,
                BackColor = ThemeConstants.BgPrimary
            };

            var lblCursor = new Label
            {
                Text = ">",
                Location = new Point(5, 5),
                AutoSize = true,
                ForeColor = ThemeConstants.FgPrimary,
                Font = ThemeConstants.FontInput
            };

            txtBarcode = new TextBox
            {
                Location = new Point(25, 3),
                Width = 300,
                BackColor = ThemeConstants.BgPrimary,
                ForeColor = ThemeConstants.FgPrimary,
                Font = ThemeConstants.FontInput,
                BorderStyle = BorderStyle.None
            };
            txtBarcode.KeyDown += TxtBarcode_KeyDown;

            pnlInput.Controls.AddRange(new Control[] { lblCursor, txtBarcode });

            // === 4. TOTAL row ===
            var pnlTotalRow = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 30,
                BackColor = ThemeConstants.BgPanel
            };

            lblTotalRow = new Label
            {
                Text = "TOTAL\u2192  0.00",
                Location = new Point(10, 5),
                AutoSize = true,
                ForeColor = ThemeConstants.FgWhite,
                Font = ThemeConstants.FontHeader
            };

            lblTotalCount = new Label
            {
                Dock = DockStyle.Right,
                AutoSize = true,
                ForeColor = ThemeConstants.FgWhite,
                Font = ThemeConstants.FontMenu,
                Text = "0",
                Padding = new Padding(0, 5, 20, 0)
            };

            pnlTotalRow.Controls.AddRange(new Control[] { lblTotalRow, lblTotalCount });

            // === 5. Footer bar (JAM + MESIN + JRNL) ===
            var pnlFooter = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 25,
                BackColor = ThemeConstants.BgFooter
            };

            lblFooterClock = new Label
            {
                Text = string.Format("JAM\u2192 {0}", _clock.Now.ToString("HH:mm:ss")),
                Dock = DockStyle.Left,
                AutoSize = true,
                ForeColor = ThemeConstants.FgWarning,
                Font = ThemeConstants.FontSmall
            };

            lblFooter = new Label
            {
                Dock = DockStyle.Right,
                AutoSize = true,
                ForeColor = ThemeConstants.FgMuted,
                Font = ThemeConstants.FontSmall
            };

            pnlFooter.Controls.AddRange(new Control[] { lblFooterClock, lblFooter });

            // === Clock timer for footer ===
            _posClock = new System.Windows.Forms.Timer { Interval = 1000 };
            _posClock.Tick += (s, e) =>
            {
                lblFooterClock.Text = string.Format("JAM\u2192 {0}", _clock.Now.ToString("HH:mm:ss"));
            };
            _posClock.Start();

            // === Search panel (inline, hidden by default) ===
            _searchPanel = new ProductSearchPanel();
            _searchPanel.ProductSelected += OnSearchProductSelected;
            _searchPanel.SearchCancelled += (s2, e2) => txtBarcode.Focus();

            // === Highlight timer for last-added item ===
            _highlightTimer = new System.Windows.Forms.Timer { Interval = 500 };
            _highlightTimer.Tick += OnHighlightTimerTick;

            // === Add controls (order matters for Dock: Fill must be added first) ===
            this.Controls.Add(dgvItems);
            this.Controls.Add(_searchPanel);
            this.Controls.Add(pnlHeader);
            this.Controls.Add(pnlInput);
            this.Controls.Add(pnlTotalRow);
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
                // Smart display: if input is numeric, show it in SUBTOTAL area temporarily
                long numVal;
                if (long.TryParse(code, out numVal))
                {
                    lblSubtotalValue.Text = numVal.ToString("N0");
                }
                SetAction("Barang tidak ditemukan: " + code);
                return;
            }

            RefreshGrid();
            UpdateTotals();
            HighlightLastRow();
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
                    Formatting.FormatCurrencyShort(item.Value),
                    item.DiscValue > 0 ? Formatting.FormatCurrencyShort(item.DiscValue) : "");
            }
        }

        private void UpdateTotals()
        {
            var totals = _salesService.GetTotals();
            lblSubtotalValue.Text = totals.NetAmount.ToString("N0");
            lblTotalRow.Text = string.Format("TOTAL\u2192  {0}", Formatting.FormatCurrency(totals.NetAmount));
            lblTotalCount.Text = _salesService.CurrentItems.Count.ToString();
        }

        private void UpdateFooter()
        {
            string registerId = _configRepo.Get("register_id") ?? "01";
            string today = _clock.Now.ToString("yyyy-MM-dd");
            _dailyCount = _saleRepo.GetDailyCount(today);

            lblFooter.Text = string.Format("MESIN#{0}  ID#{1}  JRNL#{2}",
                registerId,
                _auth.CurrentUser.Id,
                _dailyCount.ToString("D5"));
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
                            "");

                        FinalizeSale(sale, payForm.CashAmount > 0);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Payment error: " + ex.Message, "Error");
                    }
                }
            }
        }

        private void DoExactPayment()
        {
            if (_salesService.CurrentItems.Count == 0)
            {
                SetAction("No items to pay.");
                return;
            }

            try
            {
                var totals = _salesService.GetTotals();
                var sale = _salesService.CompleteSale(
                    totals.NetAmount, 0, 0, "", "", "");

                FinalizeSale(sale, true);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Payment error: " + ex.Message, "Error");
            }
        }

        private void FinalizeSale(Sale sale, bool openDrawer)
        {
            SetAction(string.Format("SALE COMPLETE: {0} — Change: {1}",
                sale.JournalNo,
                Formatting.FormatCurrency(sale.ChangeAmount)));

            PrintReceiptAsync(sale);

            if (openDrawer)
            {
                OpenCashDrawer();
            }

            _salesService.ClearCurrentSale();
            RefreshGrid();
            UpdateTotals();
            UpdateFooter();
            txtBarcode.Focus();
        }

        private async void PrintReceiptAsync(Sale sale)
        {
            try
            {
                string printerName = _configRepo.Get("printer_name");
                if (string.IsNullOrEmpty(printerName)) return;

                var printer = new ReceiptPrinter(printerName);
                // Use separate connection for background thread
                var items = _saleRepo.GetItemsByJournalNo(sale.JournalNo);

                byte[] receiptData = BuildReceiptBytes(sale, items);
                bool success = await Task.Run(() => printer.Print(receiptData)).ConfigureAwait(true);

                if (!success)
                {
                    MessageBox.Show("Receipt not printed. Use Utility > Reprint to try again.",
                        "Printer Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Print error: " + ex.Message + "\nUse Utility > Reprint to try again.",
                    "Printer Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private byte[] BuildReceiptBytes(Sale sale, List<SaleItem> items)
        {
            string storeName = _configRepo.Get("store_name") ?? "TOKO";
            string storeAddress = _configRepo.Get("store_address");
            string storeTagline = _configRepo.Get("store_tagline");
            var receipt = new List<byte[]>();

            receipt.Add(EscPosCommands.Init);
            receipt.Add(EscPosCommands.CenterAlign);
            receipt.Add(EscPosCommands.BoldOn);
            receipt.Add(EscPosCommands.Text(storeName + "\n"));
            receipt.Add(EscPosCommands.BoldOff);
            if (!string.IsNullOrEmpty(storeAddress))
            {
                receipt.Add(EscPosCommands.Text(storeAddress + "\n"));
            }
            if (!string.IsNullOrEmpty(storeTagline))
            {
                receipt.Add(EscPosCommands.Text(storeTagline + "\n"));
            }
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

        private void SearchByCode()
        {
            _searchPanel.ShowSearch(true);
        }

        private void SearchByName()
        {
            _searchPanel.ShowSearch(false);
        }

        private void OnSearchProductSelected(object sender, Product product)
        {
            AddItemByCode(product.ProductCode);
            txtBarcode.Focus();
        }

        private void HighlightLastRow()
        {
            if (dgvItems.Rows.Count == 0) return;
            int lastIndex = dgvItems.Rows.Count - 1;
            dgvItems.Rows[lastIndex].DefaultCellStyle.BackColor = ThemeConstants.BgSelection;
            dgvItems.Rows[lastIndex].DefaultCellStyle.ForeColor = ThemeConstants.FgWhite;
            _highlightTimer.Stop();
            _highlightTimer.Start();
        }

        private void OnHighlightTimerTick(object sender, EventArgs e)
        {
            _highlightTimer.Stop();
            // Reset all rows to default style (alternating handled by grid theme)
            foreach (DataGridViewRow row in dgvItems.Rows)
            {
                row.DefaultCellStyle.BackColor = Color.Empty;
                row.DefaultCellStyle.ForeColor = Color.Empty;
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.F1:
                    SearchByCode();
                    return true;
                case Keys.F2:
                    SearchByName();
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
                case Keys.F9:
                    using (var calc = new CalculatorDialog())
                    {
                        calc.ShowDialog(this);
                    }
                    txtBarcode.Focus();
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
                case Keys.F11:
                    OpenCashDrawer();
                    SetAction("Cash drawer opened");
                    return true;
                case Keys.Add:
                    DoExactPayment();
                    return true;
                case Keys.Down:
                    if (txtBarcode.Focused)
                    {
                        if (_salesService.CurrentItems.Count > 0)
                        {
                            if (MessageBox.Show("Batalkan transaksi?", "Konfirmasi",
                                MessageBoxButtons.YesNo) == DialogResult.Yes)
                            {
                                _salesService.ClearCurrentSale();
                                RefreshGrid();
                                UpdateTotals();
                                lblSubtotalValue.Text = "0";
                                SetAction("Transaksi dibatalkan");
                            }
                        }
                        txtBarcode.Focus();
                        return true;
                    }
                    break;
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

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (_posClock != null)
            {
                _posClock.Stop();
                _posClock.Dispose();
            }
            if (_highlightTimer != null)
            {
                _highlightTimer.Stop();
                _highlightTimer.Dispose();
            }
            base.OnFormClosed(e);
        }
    }
}
