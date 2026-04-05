using System;
using System.Configuration;
using System.Drawing;
using System.Windows.Forms;
using Kasir.Auth;
using Kasir.Data;
using Kasir.Sync;

namespace Kasir.Forms
{
    public class MainMenuForm : BaseForm
    {
        private readonly AuthService _auth;
        private readonly PermissionService _perms;
        private MenuStrip menuStrip;
        private Label lblBranding;

        public MainMenuForm(AuthService auth)
        {
            _auth = auth;
            _perms = new PermissionService(DbConnection.GetConnection());
            InitializeLayout();
            SetAction(string.Format("Logged in as {0} ({1})",
                _auth.CurrentUser.DisplayName,
                _auth.CurrentUser.Alias));
        }

        private void InitializeLayout()
        {
            // Menu strip
            menuStrip = new MenuStrip
            {
                BackColor = Color.FromArgb(0, 40, 0),
                ForeColor = Color.White,
                Font = new Font("Consolas", 12f)
            };

            BuildMenu();
            this.MainMenuStrip = menuStrip;
            this.Controls.Add(menuStrip);

            // Auto-focus menu on load and after returning from child forms
            this.Shown += (s, e) =>
            {
                menuStrip.Select();
                menuStrip.Items[0].Select();
            };

            // Branding footer
            string storeName = ConfigurationManager.AppSettings["StoreName"] ?? "TOKO SINAR MAKMUR";
            string storeBrand = ConfigurationManager.AppSettings["StoreBrand"] ?? "Semoga Berbahagia";
            string storeFooter = ConfigurationManager.AppSettings["StoreFooter"] ?? "Sadhu Sadhu Sadhu";

            lblBranding = new Label
            {
                Text = string.Format("{0} {1} {2} {3} {4}",
                    storeBrand, "\u2022\u2022\u2022", storeName, "\u2022\u2022\u2022", storeFooter),
                Dock = DockStyle.Bottom,
                Height = 30,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.FromArgb(0, 150, 0),
                BackColor = Color.FromArgb(0, 20, 0),
                Font = new Font("Consolas", 10f)
            };
            this.Controls.Add(lblBranding);

            // Welcome label
            var lblWelcome = new Label
            {
                Text = string.Format("Welcome, {0}\n\nUse the menu above or press F-keys:\n\n" +
                    "F2  - Product Search\n" +
                    "F5  - Payment\n" +
                    "F12 - Sync\n" +
                    "Esc - Logout",
                    _auth.CurrentUser.DisplayName),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.FromArgb(0, 180, 0),
                Font = new Font("Consolas", 14f)
            };
            this.Controls.Add(lblWelcome);
        }

        private void BuildMenu()
        {
            var user = _auth.CurrentUser;

            // Master menu (M)
            var masterMenu = new ToolStripMenuItem("&Master");
            AddMenuItem(masterMenu, "&Departemen", "master.department", OnDepartmentClick);
            AddMenuItem(masterMenu, "&Supplier", "master.supplier", OnVendorClick);
            AddMenuItem(masterMenu, "&Barang", "master.product", OnProductClick);
            AddMenuItem(masterMenu, "&Credit Card", "master.credit_card", OnCreditCardClick);
            AddMenuItem(masterMenu, "&Ganti Harga Jual", "master.price_change", OnPriceChangeClick);
            AddMenuItem(masterMenu, "Stok &Opname", "master.stock_opname", (s, e) => ShowChildForm(new Inventory.OpnameForm()));

            // Transaksi menu (T)
            var transMenu = new ToolStripMenuItem("&Transaksi");
            AddMenuItem(transMenu, "Pemesanan/&Order", "transaction.purchase", (s, e) => ShowChildForm(new Purchasing.PurchaseOrderForm()));
            AddMenuItem(transMenu, "P&enerimaan Barang", "transaction.purchase", (s, e) => ShowChildForm(new Purchasing.GoodsReceiptForm()));
            AddMenuItem(transMenu, "&Nota Pembelian", "transaction.purchase", (s, e) => ShowChildForm(new Purchasing.PurchaseInvoiceForm()));
            AddMenuItem(transMenu, "&Hutang", "transaction.purchase", (s, e) => ShowChildForm(new Accounting.PayablesForm()));
            transMenu.DropDownItems.Add(new ToolStripSeparator());
            AddMenuItem(transMenu, "&Retur Pembelian", "transaction.return", (s, e) => ShowChildForm(new Purchasing.ReturnForm()));
            AddMenuItem(transMenu, "Pema&kaian/Rusak/Hilang", "transaction.stock_out", (s, e) => ShowChildForm(new Inventory.StockOutForm()));
            transMenu.DropDownItems.Add(new ToolStripSeparator());
            AddMenuItem(transMenu, "&Penjualan", "transaction.sales", OnPenjualanClick);
            AddMenuItem(transMenu, "&Transfer", "transaction.transfer", (s, e) => ShowChildForm(new Inventory.TransferForm()));

            // Laporan menu (L)
            var reportMenu = new ToolStripMenuItem("&Laporan");
            AddMenuItem(reportMenu, "Cetak Master (&Barang)", "reports.master", (s, e) => ShowChildForm(new Reports.ProductReportForm()));
            AddMenuItem(reportMenu, "Cetak Master (&Supplier)", "reports.master", (s, e) => ShowChildForm(new Reports.ProductReportForm()));
            AddMenuItem(reportMenu, "&Pembelian/Stok", "reports.purchase", (s, e) => ShowChildForm(new Reports.InventoryReportForm()));
            AddMenuItem(reportMenu, "&Hutang", "reports.purchase", (s, e) => ShowChildForm(new Reports.FinancialReportForm()));
            AddMenuItem(reportMenu, "Pen&jualan", "reports.sales", OnSalesReportClick);
            AddMenuItem(reportMenu, "&Laba", "reports.sales", (s, e) => ShowChildForm(new Reports.FinancialReportForm()));
            AddMenuItem(reportMenu, "&Transfer/Stok", "reports.stock", (s, e) => ShowChildForm(new Reports.InventoryReportForm()));
            AddMenuItem(reportMenu, "Pema&kaian/Rusak/Hilang", "reports.stock", (s, e) => ShowChildForm(new Reports.InventoryReportForm(4)));
            AddMenuItem(reportMenu, "St&ok Barang", "reports.stock", (s, e) => ShowChildForm(new Reports.InventoryReportForm(0)));
            AddMenuItem(reportMenu, "Stok Op&name", "reports.stock", (s, e) => ShowChildForm(new Reports.InventoryReportForm(5)));

            // Utility menu (U)
            var utilMenu = new ToolStripMenuItem("&Utility");
            AddMenuItem(utilMenu, "&User Management", "utility.users", OnUserManagementClick);
            AddMenuItem(utilMenu, "&Printer Config", "utility.printer", OnPrinterConfigClick);
            AddMenuItem(utilMenu, "&Backup", "utility.backup", OnBackupClick);
            AddMenuItem(utilMenu, "&Shift Management", "pos", OnShiftClick);

            // Akuntansi menu (K)
            var accMenu = new ToolStripMenuItem("A&kuntansi");
            AddMenuItem(accMenu, "&Daftar Perkiraan", "accounting", (s, e) => ShowChildForm(new Accounting.AccountsForm()));
            AddMenuItem(accMenu, "&Jurnal Memorial", "accounting", (s, e) => ShowChildForm(new Accounting.JournalForm(false, _auth.CurrentUser.Id)));
            AddMenuItem(accMenu, "Penerimaan &Kas", "accounting", (s, e) => ShowChildForm(new Accounting.CashReceiptForm(false, _auth.CurrentUser.Id)));
            AddMenuItem(accMenu, "&Pengeluaran Kas", "accounting", (s, e) => ShowChildForm(new Accounting.CashDisbursementForm(false, _auth.CurrentUser.Id)));
            AddMenuItem(accMenu, "Penerimaan &Bank", "accounting", (s, e) => ShowChildForm(new Accounting.CashReceiptForm(true, _auth.CurrentUser.Id)));
            AddMenuItem(accMenu, "P&engeluaran Bank", "accounting", (s, e) => ShowChildForm(new Accounting.CashDisbursementForm(true, _auth.CurrentUser.Id)));
            accMenu.DropDownItems.Add(new ToolStripSeparator());
            AddMenuItem(accMenu, "Pr&oses Posting", "accounting", (s, e) => ShowChildForm(new Accounting.PostingProgressForm()));

            // Informasi menu (I)
            var infoMenu = new ToolStripMenuItem("&Informasi");
            AddMenuItem(infoMenu, "Info &Perkiraan", "accounting", (s, e) => ShowChildForm(new Accounting.AccountsForm(true)));
            AddMenuItem(infoMenu, "Info &Jurnal", "accounting", (s, e) => ShowChildForm(new Accounting.JournalForm(true, _auth.CurrentUser.Id)));
            AddMenuItem(infoMenu, "Info &Supplier", "master.supplier", (s, e) => ShowChildForm(new Master.VendorForm(_auth.CurrentUser.Id)));
            AddMenuItem(infoMenu, "Info &Giro", "bank", (s, e) => ShowChildForm(new Bank.BankGiroForm(true)));
            AddMenuItem(infoMenu, "Info &Barang", "master.product", (s, e) => ShowChildForm(new Master.ProductForm(_auth.CurrentUser.Id)));

            // Bank menu (B)
            var bankMenu = new ToolStripMenuItem("&Bank");
            AddMenuItem(bankMenu, "&Input Tabel Bank", "bank", (s, e) => ShowChildForm(new Bank.BankForm()));
            AddMenuItem(bankMenu, "&Giro Tolakan/Cair", "bank", (s, e) => ShowChildForm(new Bank.BankGiroForm()));
            AddMenuItem(bankMenu, "&Laporan Buku Bank", "bank", (s, e) => ShowChildForm(new Reports.FinancialReportForm()));

            // Keluar (E) — changed from &K to avoid conflict with A&kuntansi
            var exitMenu = new ToolStripMenuItem("K&eluar");
            exitMenu.Click += (s, e) => this.Close();

            menuStrip.Items.AddRange(new ToolStripItem[] {
                masterMenu, transMenu, accMenu, reportMenu, infoMenu, utilMenu, bankMenu, exitMenu
            });

            // Style all items
            foreach (ToolStripMenuItem item in menuStrip.Items)
            {
                item.ForeColor = Color.White;
                foreach (ToolStripItem sub in item.DropDownItems)
                {
                    if (sub is ToolStripMenuItem menuItem)
                    {
                        menuItem.BackColor = Color.FromArgb(0, 30, 0);
                        menuItem.ForeColor = menuItem.Enabled ? Color.White : Color.Gray;
                    }
                }
            }
        }

        private void AddMenuItem(ToolStripMenuItem parent, string text, string permission,
            EventHandler handler)
        {
            var item = new ToolStripMenuItem(text);
            item.Enabled = _perms.HasPermission(_auth.CurrentUser, permission);
            if (item.Enabled)
            {
                item.Click += handler;
            }
            parent.DropDownItems.Add(item);
        }

        private void OnDepartmentClick(object sender, EventArgs e)
        {
            ShowChildForm(new Master.DepartmentForm(_auth.CurrentUser.Id));
        }

        private void OnUserManagementClick(object sender, EventArgs e)
        {
            ShowChildForm(new Admin.UserForm());
        }

        private void OnPrinterConfigClick(object sender, EventArgs e)
        {
            ShowChildForm(new Admin.PrinterConfigForm());
        }

        private void OnBackupClick(object sender, EventArgs e)
        {
            ShowChildForm(new Admin.BackupForm());
        }

        private void ShowChildForm(Form childForm)
        {
            this.Hide();
            childForm.FormClosed += (s, ev) =>
            {
                this.Show();
                menuStrip.Select();
                menuStrip.Items[0].Select();
            };
            childForm.Show();
        }

        private void OnVendorClick(object sender, EventArgs e)
        {
            ShowChildForm(new Master.VendorForm(_auth.CurrentUser.Id));
        }

        private void OnPriceChangeClick(object sender, EventArgs e)
        {
            ShowChildForm(new Master.PriceChangeForm());
        }

        private void OnProductClick(object sender, EventArgs e)
        {
            ShowChildForm(new Master.ProductForm(_auth.CurrentUser.Id));
        }

        private void OnCreditCardClick(object sender, EventArgs e)
        {
            ShowChildForm(new Master.CreditCardForm(_auth.CurrentUser.Id));
        }

        private void OnSalesReportClick(object sender, EventArgs e)
        {
            ShowChildForm(new Reports.SalesReportForm());
        }

        private void OnPenjualanClick(object sender, EventArgs e)
        {
            ShowChildForm(new POS.SaleForm(_auth));
        }

        private void OnShiftClick(object sender, EventArgs e)
        {
            ShowChildForm(new POS.ShiftForm(_auth.CurrentUser.Id));
        }

        private void RunSync()
        {
            SetAction("Syncing...");
            try
            {
                var engine = new SyncEngine(DbConnection.GetConnection());
                var result = engine.RunOnce();

                string msg2 = string.Format("Push: {0} events. Pull: {1} events.{2}",
                    result.PushEventCount,
                    result.PullAppliedCount,
                    result.Success ? "" : " (errors occurred)");

                SetAction(string.Format("Last sync: {0} — {1}",
                    engine.LastSyncTime.ToString("HH:mm:ss"), msg2));

                if (!result.Success)
                {
                    MessageBox.Show(
                        string.Format("Sync completed with issues:\n\nPush: {0}\nPull: {1}",
                            engine.LastPushResult, engine.LastPullResult),
                        "Sync Warning",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                SetAction("Sync failed: " + ex.Message);
                MessageBox.Show("Sync error: " + ex.Message, "Error");
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.F12:
                    RunSync();
                    return true;
                case Keys.Escape:
                    this.Close();
                    return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
