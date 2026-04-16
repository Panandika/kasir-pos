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

namespace Kasir.Forms.Inventory
{
    public class StockOutForm : BaseForm
    {
        private ComboBox cboType;
        private DataGridView dgvItems;
        private StockOpnameService _service;
        private ProductRepository _productRepo;
        private List<StockAdjustmentItem> _items;

        public StockOutForm()
        {
            var conn = DbConnection.GetConnection();
            _service = new StockOpnameService(conn, new ClockImpl());
            _productRepo = new ProductRepository(conn);
            _items = new List<StockAdjustmentItem>();
            InitializeLayout();
            SetAction("Mutasi Barang Keluar — Ins: Add Item, F10: Save, Esc: Close");
        }

        private void InitializeLayout()
        {
            var pnlHeader = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = ThemeConstants.BgPanel };
            pnlHeader.Controls.Add(new Label { Text = "Jenis:", Location = new Point(10, 10), AutoSize = true, ForeColor = ThemeConstants.FgLabel });
            cboType = new ComboBox { Location = new Point(70, 7), Width = 200, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = ThemeConstants.BgInput, ForeColor = ThemeConstants.FgPrimary };
            cboType.Items.AddRange(new object[] { "USAGE - Pemakaian", "DAMAGE - Rusak", "LOSS - Hilang" });
            cboType.SelectedIndex = 0;
            pnlHeader.Controls.Add(cboType);

            dgvItems = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true };
            ApplyGridTheme(dgvItems);
            dgvItems.Columns.Add("Code", "Kode"); dgvItems.Columns["Code"].FillWeight = 140;
            dgvItems.Columns.Add("Name", "Nama"); dgvItems.Columns["Name"].FillWeight = 300;
            dgvItems.Columns.Add("Qty", "Qty"); dgvItems.Columns["Qty"].FillWeight = 80;

            this.Controls.Add(dgvItems);
            this.Controls.Add(pnlHeader);
        }

        private void AddItem()
        {
            using (var dlg = new InputDialog("Add Item", new[] { "Kode Barang", "Qty" }, new[] { "", "1" }))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    var product = _productRepo.GetByCode(dlg.Values[0]);
                    if (product == null) { MessageBox.Show("Tidak ditemukan."); return; }
                    int qty; int.TryParse(dlg.Values[1], out qty);
                    _items.Add(new StockAdjustmentItem { ProductCode = product.ProductCode, ProductName = product.Name, Quantity = qty });
                    RefreshGrid();
                }
            }
        }

        private void RefreshGrid()
        {
            dgvItems.Rows.Clear();
            foreach (var item in _items)
            {
                dgvItems.Rows.Add(item.ProductCode, item.ProductName, item.Quantity);
            }
        }

        private void Save()
        {
            if (_items.Count == 0) { MessageBox.Show("Tambah item."); return; }
            string[] types = { "USAGE", "DAMAGE", "LOSS" };
            string docType = types[cboType.SelectedIndex];
            string jnl = _service.CreateStockOut(docType, "", _items, 1);
            MessageBox.Show("Saved: " + jnl);
            _items.Clear(); RefreshGrid();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Insert: AddItem(); return true;
                case Keys.F10: Save(); return true;
                case Keys.Escape: this.Close(); return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
