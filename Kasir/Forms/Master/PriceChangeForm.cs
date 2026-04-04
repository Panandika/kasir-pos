using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Services;
using Kasir.Utils;

namespace Kasir.Forms.Master
{
    public class PriceChangeForm : BaseForm
    {
        private DataGridView dgvPrices;
        private ProductRepository _productRepo;
        private PriceChangeService _priceService;

        public PriceChangeForm()
        {
            var conn = DbConnection.GetConnection();
            _productRepo = new ProductRepository(conn);
            _priceService = new PriceChangeService(conn);
            InitializeLayout();
            SetAction("Ganti Harga Jual — F5: Load Products, F10: Save Changes, Esc: Keluar");
        }

        private void InitializeLayout()
        {
            dgvPrices = new DataGridView { Dock = DockStyle.Fill };
            ApplyGridTheme(dgvPrices);
            dgvPrices.ReadOnly = false;

            dgvPrices.Columns.Add(new DataGridViewTextBoxColumn { Name = "Code", HeaderText = "Kode", ReadOnly = true, Width = 120 });
            dgvPrices.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "Nama Barang", ReadOnly = true, Width = 250 });
            dgvPrices.Columns.Add(new DataGridViewTextBoxColumn { Name = "OldPrice", HeaderText = "Jual Lama", ReadOnly = true, Width = 100 });
            dgvPrices.Columns.Add(new DataGridViewTextBoxColumn { Name = "NewPrice", HeaderText = "Jual Baru", Width = 100 });
            dgvPrices.Columns.Add(new DataGridViewTextBoxColumn { Name = "BuyPrice", HeaderText = "Beli", ReadOnly = true, Width = 100 });

            // Style editable column
            dgvPrices.Columns["NewPrice"].DefaultCellStyle.BackColor = Color.FromArgb(30, 30, 0);
            dgvPrices.Columns["NewPrice"].DefaultCellStyle.ForeColor = Color.Yellow;

            this.Controls.Add(dgvPrices);
        }

        private void LoadProducts()
        {
            dgvPrices.Rows.Clear();
            var products = _productRepo.GetAll(1000, 0);

            foreach (var p in products)
            {
                int rowIdx = dgvPrices.Rows.Add(
                    p.ProductCode,
                    p.Name,
                    (p.Price / 100.0).ToString("F0"),
                    (p.Price / 100.0).ToString("F0"),
                    (p.BuyingPrice / 100.0).ToString("F0"));
                dgvPrices.Rows[rowIdx].Tag = p;
            }

            SetAction(string.Format("Loaded {0} products. Edit 'Jual Baru' column, then F10 to save.", products.Count));
        }

        private void SaveChanges()
        {
            var changes = new List<PriceChangeEntry>();

            foreach (DataGridViewRow row in dgvPrices.Rows)
            {
                if (row.Tag == null) continue;
                var product = row.Tag as Models.Product;
                if (product == null) continue;

                string newPriceStr = row.Cells["NewPrice"].Value?.ToString() ?? "";
                double newPriceVal;
                if (!double.TryParse(newPriceStr, out newPriceVal)) continue;

                int newPrice = (int)(newPriceVal * 100);
                if (newPrice == product.Price) continue;

                changes.Add(new PriceChangeEntry
                {
                    ProductCode = product.ProductCode,
                    NewPrice = newPrice
                });
            }

            if (changes.Count == 0)
            {
                MessageBox.Show("Tidak ada perubahan harga.", "Info");
                return;
            }

            if (MessageBox.Show(
                string.Format("Simpan {0} perubahan harga?", changes.Count),
                "Konfirmasi", MessageBoxButtons.YesNo) == DialogResult.Yes)
            {
                int count = _priceService.ApplyBatchPriceChange(changes, 1);
                MessageBox.Show(string.Format("{0} harga berhasil diubah.", count), "Selesai");
                LoadProducts(); // Refresh
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.F5: LoadProducts(); return true;
                case Keys.F10: SaveChanges(); return true;
                case Keys.Escape: this.Close(); return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
