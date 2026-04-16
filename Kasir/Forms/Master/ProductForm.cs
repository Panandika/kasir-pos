using System;
using System.Drawing;
using System.Windows.Forms;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Models;
using Kasir.Utils;

namespace Kasir.Forms.Master
{
    public class ProductForm : BaseForm
    {
        private DataGridView dgvProducts;
        private TextBox txtSearch;
        private TabControl tabDetail;
        private ProductRepository _productRepo;
        private DepartmentRepository _deptRepo;

        // Detail fields — General tab
        private TextBox txtCode, txtName, txtBarcode, txtUnit;
        private ComboBox cboDept;
        private ComboBox cboStatus;

        // Detail fields — Pricing tab
        private TextBox txtPrice, txtPrice1, txtPrice2, txtPrice3, txtPrice4;
        private TextBox txtBuyingPrice, txtCostPrice;
        private TextBox txtQtyBreak2, txtQtyBreak3;
        private ComboBox cboOpenPrice;

        // Detail fields — Other tab
        private TextBox txtDiscPct, txtVendorCode;
        private ComboBox cboVatFlag;

        private Product _currentProduct;
        private bool _isEditing;
        private int _currentUserId;

        public ProductForm(int userId = 1)
        {
            _currentUserId = userId;
            var conn = DbConnection.GetConnection();
            _productRepo = new ProductRepository(conn);
            _deptRepo = new DepartmentRepository(conn);
            InitializeLayout();
            SetAction("Master Barang — F2: Search, Ins: Tambah, Enter: Ubah, F9: Save, Esc: Keluar");
            LoadGrid();
        }

        private void InitializeLayout()
        {
            // Search bar
            var pnlSearch = new Panel { Dock = DockStyle.Top, Height = 35, BackColor = ThemeConstants.BgFooter };
            var lblSearch = new Label { Text = "Search:", Location = new Point(5, 8), AutoSize = true, ForeColor = ThemeConstants.FgLabel };
            txtSearch = new TextBox
            {
                Location = new Point(80, 5),
                Width = 300,
                BackColor = ThemeConstants.BgInput,
                ForeColor = ThemeConstants.FgPrimary,
                Font = ThemeConstants.FontInputSmall
            };
            txtSearch.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    SearchProducts();
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                }
            };
            ApplyFocusIndicator(txtSearch);
            pnlSearch.Controls.AddRange(new Control[] { lblSearch, txtSearch });

            // Split: top = grid, bottom = detail tabs
            var splitContainer = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterDistance = 300,
                BackColor = ThemeConstants.BgPrimary
            };

            // Top: Product grid
            dgvProducts = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true };
            ApplyGridTheme(dgvProducts);
            dgvProducts.Columns.Add("Code", "Kode");
            dgvProducts.Columns.Add("Name", "Nama Barang");
            dgvProducts.Columns.Add("Barcode", "Barcode");
            dgvProducts.Columns.Add("Price", "Harga");
            dgvProducts.Columns.Add("Status", "Status");
            dgvProducts.Columns["Code"].FillWeight = 120;
            dgvProducts.Columns["Name"].FillWeight = 300;
            dgvProducts.Columns["Barcode"].FillWeight = 140;
            dgvProducts.Columns["Price"].FillWeight = 100;
            dgvProducts.Columns["Status"].FillWeight = 60;
            dgvProducts.SelectionChanged += DgvProducts_SelectionChanged;

            splitContainer.Panel1.Controls.Add(dgvProducts);

            // Bottom: Detail TabControl
            tabDetail = new TabControl
            {
                Dock = DockStyle.Fill,
                BackColor = ThemeConstants.BgDialog,
                Font = ThemeConstants.FontSmall
            };

            tabDetail.TabPages.Add(CreateGeneralTab());
            tabDetail.TabPages.Add(CreatePricingTab());
            tabDetail.TabPages.Add(CreateOtherTab());

            splitContainer.Panel2.Controls.Add(tabDetail);

            this.Controls.Add(splitContainer);
            this.Controls.Add(pnlSearch);
            SetDetailEnabled(false);
        }

        private TabPage CreateGeneralTab()
        {
            var tab = new TabPage("Umum") { BackColor = ThemeConstants.BgDialog };
            int y = 10;

            AddLabel(tab, "Kode Barang:", 10, y);
            txtCode = AddTextBox(tab, 160, y, 200); ApplyFocusIndicator(txtCode); y += 30;

            AddLabel(tab, "Nama:", 10, y);
            txtName = AddTextBox(tab, 160, y, 400); ApplyFocusIndicator(txtName); y += 30;

            AddLabel(tab, "Barcode:", 10, y);
            txtBarcode = AddTextBox(tab, 160, y, 200); ApplyFocusIndicator(txtBarcode); y += 30;

            AddLabel(tab, "Satuan:", 10, y);
            txtUnit = AddTextBox(tab, 160, y, 100); ApplyFocusIndicator(txtUnit); y += 30;

            AddLabel(tab, "Departemen:", 10, y);
            cboDept = new ComboBox
            {
                Location = new Point(160, y),
                Width = 250,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = ThemeConstants.BgInput,
                ForeColor = ThemeConstants.FgPrimary
            };
            var depts = _deptRepo.GetAll();
            foreach (var d in depts)
            {
                cboDept.Items.Add(string.Format("{0} - {1}", d.DeptCode, d.Name));
            }
            tab.Controls.Add(cboDept);
            ApplyFocusIndicator(cboDept); y += 30;

            AddLabel(tab, "Status:", 10, y);
            cboStatus = new ComboBox
            {
                Location = new Point(160, y),
                Width = 100,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = ThemeConstants.BgInput,
                ForeColor = ThemeConstants.FgPrimary
            };
            cboStatus.Items.AddRange(new object[] { "A - Active", "I - Inactive" });
            tab.Controls.Add(cboStatus);
            ApplyFocusIndicator(cboStatus);

            return tab;
        }

        private TabPage CreatePricingTab()
        {
            var tab = new TabPage("Harga") { BackColor = ThemeConstants.BgDialog };
            int y = 10;

            AddLabel(tab, "Harga Jual:", 10, y);
            txtPrice = AddTextBox(tab, 160, y, 150); ApplyFocusIndicator(txtPrice); y += 30;

            AddLabel(tab, "Harga 1 (Grosir):", 10, y);
            txtPrice1 = AddTextBox(tab, 160, y, 150); ApplyFocusIndicator(txtPrice1); y += 30;

            AddLabel(tab, "Harga 2:", 10, y);
            txtPrice2 = AddTextBox(tab, 160, y, 150); ApplyFocusIndicator(txtPrice2);
            AddLabel(tab, "Batas Qty 2:", 350, y);
            txtQtyBreak2 = AddTextBox(tab, 460, y, 80); ApplyFocusIndicator(txtQtyBreak2); y += 30;

            AddLabel(tab, "Harga 3:", 10, y);
            txtPrice3 = AddTextBox(tab, 160, y, 150); ApplyFocusIndicator(txtPrice3);
            AddLabel(tab, "Batas Qty 3:", 350, y);
            txtQtyBreak3 = AddTextBox(tab, 460, y, 80); ApplyFocusIndicator(txtQtyBreak3); y += 30;

            AddLabel(tab, "Harga 4:", 10, y);
            txtPrice4 = AddTextBox(tab, 160, y, 150); ApplyFocusIndicator(txtPrice4); y += 30;

            AddLabel(tab, "Harga Beli:", 10, y);
            txtBuyingPrice = AddTextBox(tab, 160, y, 150); ApplyFocusIndicator(txtBuyingPrice); y += 30;

            AddLabel(tab, "Harga Pokok:", 10, y);
            txtCostPrice = AddTextBox(tab, 160, y, 150); ApplyFocusIndicator(txtCostPrice); y += 30;

            AddLabel(tab, "Open Price:", 10, y);
            cboOpenPrice = new ComboBox
            {
                Location = new Point(160, y),
                Width = 100,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = ThemeConstants.BgInput,
                ForeColor = ThemeConstants.FgPrimary
            };
            cboOpenPrice.Items.AddRange(new object[] { "N", "Y" });
            tab.Controls.Add(cboOpenPrice);
            ApplyFocusIndicator(cboOpenPrice);

            return tab;
        }

        private TabPage CreateOtherTab()
        {
            var tab = new TabPage("Lain-lain") { BackColor = ThemeConstants.BgDialog };
            int y = 10;

            AddLabel(tab, "Disc %:", 10, y);
            txtDiscPct = AddTextBox(tab, 160, y, 100); ApplyFocusIndicator(txtDiscPct); y += 30;

            AddLabel(tab, "Kode Vendor:", 10, y);
            txtVendorCode = AddTextBox(tab, 160, y, 200); ApplyFocusIndicator(txtVendorCode); y += 30;

            AddLabel(tab, "PPN:", 10, y);
            cboVatFlag = new ComboBox
            {
                Location = new Point(160, y),
                Width = 100,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = ThemeConstants.BgInput,
                ForeColor = ThemeConstants.FgPrimary
            };
            cboVatFlag.Items.AddRange(new object[] { "N", "Y" });
            tab.Controls.Add(cboVatFlag);
            ApplyFocusIndicator(cboVatFlag);

            return tab;
        }

        private static void AddLabel(TabPage tab, string text, int x, int y)
        {
            tab.Controls.Add(new Label
            {
                Text = text,
                Location = new Point(x, y + 3),
                AutoSize = true,
                ForeColor = ThemeConstants.FgLabel
            });
        }

        private static TextBox AddTextBox(TabPage tab, int x, int y, int width)
        {
            var txt = new TextBox
            {
                Location = new Point(x, y),
                Width = width,
                BackColor = ThemeConstants.BgInput,
                ForeColor = ThemeConstants.FgPrimary,
                Font = ThemeConstants.FontGrid
            };
            tab.Controls.Add(txt);
            return txt;
        }

        private void LoadGrid()
        {
            dgvProducts.Rows.Clear();
            var products = _productRepo.GetAll(500, 0);
            foreach (var p in products)
            {
                dgvProducts.Rows.Add(
                    p.ProductCode,
                    p.Name,
                    p.Barcode ?? "",
                    Formatting.FormatCurrencyShort(p.Price),
                    p.Status);
                dgvProducts.Rows[dgvProducts.Rows.Count - 1].Tag = p;
            }
        }

        private void SearchProducts()
        {
            string query = txtSearch.Text.Trim();
            if (string.IsNullOrEmpty(query))
            {
                LoadGrid();
                return;
            }

            dgvProducts.Rows.Clear();
            var results = _productRepo.SearchByText(query, 100);
            foreach (var p in results)
            {
                dgvProducts.Rows.Add(p.ProductCode, p.Name, p.Barcode ?? "",
                    Formatting.FormatCurrencyShort(p.Price), p.Status);
                dgvProducts.Rows[dgvProducts.Rows.Count - 1].Tag = p;
            }

            SetAction(string.Format("Found {0} products", results.Count));
        }

        private void DgvProducts_SelectionChanged(object sender, EventArgs e)
        {
            if (_isEditing) return;
            if (dgvProducts.CurrentRow == null) return;

            _currentProduct = dgvProducts.CurrentRow.Tag as Product;
            if (_currentProduct != null)
            {
                PopulateDetail(_currentProduct);
            }
        }

        private void PopulateDetail(Product p)
        {
            txtCode.Text = p.ProductCode ?? "";
            txtName.Text = p.Name ?? "";
            txtBarcode.Text = p.Barcode ?? "";
            txtUnit.Text = p.Unit ?? "";
            txtPrice.Text = (p.Price / 100.0).ToString("F0");
            txtPrice1.Text = (p.Price1 / 100.0).ToString("F0");
            txtPrice2.Text = (p.Price2 / 100.0).ToString("F0");
            txtPrice3.Text = (p.Price3 / 100.0).ToString("F0");
            txtPrice4.Text = (p.Price4 / 100.0).ToString("F0");
            txtBuyingPrice.Text = (p.BuyingPrice / 100.0).ToString("F0");
            txtCostPrice.Text = (p.CostPrice / 100.0).ToString("F0");
            txtQtyBreak2.Text = p.QtyBreak2.ToString();
            txtQtyBreak3.Text = p.QtyBreak3.ToString();
            txtDiscPct.Text = (p.DiscPct / 100.0).ToString("F2");
            txtVendorCode.Text = p.VendorCode ?? "";

            cboOpenPrice.SelectedIndex = p.OpenPrice == "Y" ? 1 : 0;
            cboVatFlag.SelectedIndex = p.VatFlag == "Y" ? 1 : 0;
            cboStatus.SelectedIndex = p.Status == "I" ? 1 : 0;

            // Find department in dropdown
            for (int i = 0; i < cboDept.Items.Count; i++)
            {
                if (cboDept.Items[i].ToString().StartsWith(p.DeptCode ?? ""))
                {
                    cboDept.SelectedIndex = i;
                    break;
                }
            }
        }

        private Product ReadDetailToProduct()
        {
            var p = _currentProduct ?? new Product();

            p.ProductCode = txtCode.Text.Trim();
            p.Name = txtName.Text.Trim();
            p.Barcode = txtBarcode.Text.Trim();
            p.Unit = txtUnit.Text.Trim();

            decimal val;
            p.Price = decimal.TryParse(txtPrice.Text, out val) ? (int)(val * 100m) : 0;
            p.Price1 = decimal.TryParse(txtPrice1.Text, out val) ? (int)(val * 100m) : 0;
            p.Price2 = decimal.TryParse(txtPrice2.Text, out val) ? (int)(val * 100m) : 0;
            p.Price3 = decimal.TryParse(txtPrice3.Text, out val) ? (int)(val * 100m) : 0;
            p.Price4 = decimal.TryParse(txtPrice4.Text, out val) ? (int)(val * 100m) : 0;
            p.BuyingPrice = decimal.TryParse(txtBuyingPrice.Text, out val) ? (int)(val * 100m) : 0;
            p.CostPrice = decimal.TryParse(txtCostPrice.Text, out val) ? (int)(val * 100m) : 0;

            int intVal;
            p.QtyBreak2 = int.TryParse(txtQtyBreak2.Text, out intVal) ? intVal : 0;
            p.QtyBreak3 = int.TryParse(txtQtyBreak3.Text, out intVal) ? intVal : 0;

            decimal discVal;
            p.DiscPct = decimal.TryParse(txtDiscPct.Text, out discVal) ? (int)(discVal * 100m) : 0;

            p.VendorCode = txtVendorCode.Text.Trim();
            p.OpenPrice = cboOpenPrice.SelectedIndex == 1 ? "Y" : "N";
            p.VatFlag = cboVatFlag.SelectedIndex == 1 ? "Y" : "N";
            p.Status = cboStatus.SelectedIndex == 1 ? "I" : "A";

            if (cboDept.SelectedItem != null)
            {
                string deptItem = cboDept.SelectedItem.ToString();
                int dashIndex = deptItem.IndexOf(" - ");
                p.DeptCode = dashIndex > 0 ? deptItem.Substring(0, dashIndex) : "";
            }

            p.ChangedBy = _currentUserId;

            return p;
        }

        private void SetDetailEnabled(bool enabled)
        {
            _isEditing = enabled;
            foreach (TabPage tab in tabDetail.TabPages)
            {
                foreach (Control ctrl in tab.Controls)
                {
                    if (ctrl is TextBox || ctrl is ComboBox)
                    {
                        ctrl.Enabled = enabled;
                    }
                }
            }
        }

        private void SaveProduct()
        {
            if (!_isEditing) return;

            var product = ReadDetailToProduct();

            if (string.IsNullOrEmpty(product.ProductCode) || string.IsNullOrEmpty(product.Name))
            {
                MessageBox.Show("Kode dan nama barang harus diisi.", "Error");
                return;
            }

            try
            {
                if (product.Id == 0)
                {
                    _productRepo.Insert(product);
                    SetAction("Product added: " + product.ProductCode);
                }
                else
                {
                    _productRepo.Update(product);
                    SetAction("Product updated: " + product.ProductCode);
                }

                SetDetailEnabled(false);
                LoadGrid();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Save failed: " + ex.Message, "Error");
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.F2:
                    txtSearch.Focus();
                    return true;
                case Keys.Insert:
                    _currentProduct = new Product();
                    PopulateDetail(_currentProduct);
                    SetDetailEnabled(true);
                    txtCode.Focus();
                    SetAction("Adding new product...");
                    return true;
                case Keys.Enter:
                    if (!_isEditing && _currentProduct != null)
                    {
                        SetDetailEnabled(true);
                        txtName.Focus();
                        SetAction("Editing: " + _currentProduct.ProductCode);
                    }
                    return true;
                case Keys.F9:
                    SaveProduct();
                    return true;
                case Keys.Delete:
                    if (_currentProduct != null && _currentProduct.Id > 0)
                    {
                        if (MessageBox.Show("Deactivate product?", "Confirm", MessageBoxButtons.YesNo) == DialogResult.Yes)
                        {
                            _productRepo.Deactivate(_currentProduct.Id, 1);
                            LoadGrid();
                        }
                    }
                    return true;
                case Keys.Escape:
                    if (_isEditing)
                    {
                        SetDetailEnabled(false);
                        SetAction("Edit cancelled.");
                    }
                    else
                    {
                        this.Close();
                    }
                    return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
