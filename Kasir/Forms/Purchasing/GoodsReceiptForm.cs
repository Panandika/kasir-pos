using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Forms.Shared;
using Kasir.Models;
using Kasir.Services;
using Kasir.Utils;

namespace Kasir.Forms.Purchasing
{
    public class GoodsReceiptForm : BaseForm
    {
        private TextBox txtVendor, txtDate, txtInvoiceNo;
        private DataGridView dgvItems;
        private Label lblTotal;
        private PurchasingService _service;
        private SubsidiaryRepository _vendorRepo;
        private ProductRepository _productRepo;
        private List<PurchaseItem> _items;
        private string _vendorCode;

        public GoodsReceiptForm()
        {
            var conn = DbConnection.GetConnection();
            _service = new PurchasingService(conn, new ClockImpl());
            _vendorRepo = new SubsidiaryRepository(conn);
            _productRepo = new ProductRepository(conn);
            _items = new List<PurchaseItem>();
            InitializeLayout();
            SetAction("Goods Receipt — F2: Vendor, Ins: Add Item, F10: Save, Esc: Close");
            txtDate.Text = DateTime.Now.ToString("yyyy-MM-dd");
        }

        private void InitializeLayout()
        {
            var pnlHeader = new Panel { Dock = DockStyle.Top, Height = 60, BackColor = Color.FromArgb(0, 20, 0) };
            pnlHeader.Controls.Add(new Label { Text = "Supplier:", Location = new Point(10, 8), AutoSize = true, ForeColor = Color.Gray });
            txtVendor = new TextBox { Location = new Point(90, 5), Width = 250, ReadOnly = true, BackColor = Color.FromArgb(30, 30, 30), ForeColor = Color.FromArgb(0, 255, 0), Font = new Font("Consolas", 12f) };
            pnlHeader.Controls.Add(txtVendor);

            pnlHeader.Controls.Add(new Label { Text = "Faktur:", Location = new Point(360, 8), AutoSize = true, ForeColor = Color.Gray });
            txtInvoiceNo = new TextBox { Location = new Point(420, 5), Width = 150, BackColor = Color.FromArgb(30, 30, 30), ForeColor = Color.FromArgb(0, 255, 0), Font = new Font("Consolas", 12f) };
            pnlHeader.Controls.Add(txtInvoiceNo);

            pnlHeader.Controls.Add(new Label { Text = "Tgl:", Location = new Point(10, 35), AutoSize = true, ForeColor = Color.Gray });
            txtDate = new TextBox { Location = new Point(90, 32), Width = 120, BackColor = Color.FromArgb(30, 30, 30), ForeColor = Color.FromArgb(0, 255, 0), Font = new Font("Consolas", 12f) };
            pnlHeader.Controls.Add(txtDate);

            dgvItems = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true };
            ApplyGridTheme(dgvItems);
            dgvItems.Columns.Add("No", "No"); dgvItems.Columns["No"].Width = 40;
            dgvItems.Columns.Add("Code", "Kode"); dgvItems.Columns["Code"].Width = 140;
            dgvItems.Columns.Add("Name", "Nama"); dgvItems.Columns["Name"].Width = 250;
            dgvItems.Columns.Add("Qty", "Qty"); dgvItems.Columns["Qty"].Width = 80;
            dgvItems.Columns.Add("Price", "Harga"); dgvItems.Columns["Price"].Width = 120;
            dgvItems.Columns.Add("Total", "Jumlah"); dgvItems.Columns["Total"].Width = 120;

            var pnlFooter = new Panel { Dock = DockStyle.Bottom, Height = 35, BackColor = Color.FromArgb(0, 30, 0) };
            lblTotal = new Label { Dock = DockStyle.Right, AutoSize = true, ForeColor = Color.White, Font = new Font("Consolas", 14f, FontStyle.Bold), Text = "TOTAL: Rp 0", Padding = new Padding(0, 5, 20, 0) };
            pnlFooter.Controls.Add(lblTotal);

            this.Controls.Add(dgvItems);
            this.Controls.Add(pnlHeader);
            this.Controls.Add(pnlFooter);
        }

        private void SelectVendor()
        {
            string code = InputDialog.ShowSingleInput(this, "Supplier", "Kode Supplier", "");
            if (string.IsNullOrEmpty(code)) return;
            var vendor = _vendorRepo.GetByCode(code);
            if (vendor == null) { MessageBox.Show("Supplier tidak ditemukan."); return; }
            _vendorCode = vendor.SubCode;
            txtVendor.Text = string.Format("{0} — {1}", vendor.SubCode, vendor.Name);
        }

        private void AddItem()
        {
            string code = InputDialog.ShowSingleInput(this, "Add Item", "Kode Barang", "");
            if (string.IsNullOrEmpty(code)) return;
            var product = _productRepo.GetByCode(code);
            if (product == null) { MessageBox.Show("Barang tidak ditemukan."); return; }

            using (var dlg = new InputDialog("Item", new[] { "Qty", "Harga Beli" },
                new[] { "1", (product.BuyingPrice / 100.0).ToString("F0") }))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    int qty; double price;
                    int.TryParse(dlg.Values[0], out qty);
                    double.TryParse(dlg.Values[1], out price);
                    _items.Add(new PurchaseItem { ProductCode = code, ProductName = product.Name, Quantity = qty, UnitPrice = (int)(price * 100), Value = (long)(price * 100) * qty });
                    RefreshGrid();
                }
            }
        }

        private void RefreshGrid()
        {
            dgvItems.Rows.Clear();
            long total = 0; int no = 1;
            foreach (var item in _items)
            {
                dgvItems.Rows.Add(no++, item.ProductCode, item.ProductName, item.Quantity, Formatting.FormatCurrencyShort(item.UnitPrice), Formatting.FormatCurrencyShort(item.Value));
                total += item.Value;
            }
            lblTotal.Text = string.Format("TOTAL: {0}", Formatting.FormatCurrency(total));
        }

        private void Save()
        {
            if (string.IsNullOrEmpty(_vendorCode)) { MessageBox.Show("Pilih supplier."); return; }
            if (_items.Count == 0) { MessageBox.Show("Tambah item."); return; }
            var receipt = new Purchase { SubCode = _vendorCode, DocDate = txtDate.Text.Trim(), RefNo = txtInvoiceNo.Text.Trim() };
            string jnl = _service.CreateGoodsReceipt(receipt, _items, 1);
            MessageBox.Show("Goods Receipt saved: " + jnl + "\nStock updated.");
            _items.Clear(); RefreshGrid();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.F2: SelectVendor(); return true;
                case Keys.Insert: AddItem(); return true;
                case Keys.Delete: if (dgvItems.CurrentRow != null && dgvItems.CurrentRow.Index < _items.Count) { _items.RemoveAt(dgvItems.CurrentRow.Index); RefreshGrid(); } return true;
                case Keys.F10: Save(); return true;
                case Keys.Escape: this.Close(); return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
