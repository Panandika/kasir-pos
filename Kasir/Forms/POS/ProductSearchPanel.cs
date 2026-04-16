using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Models;
using Kasir.Utils;

namespace Kasir.Forms.POS
{
    public class ProductSearchPanel : UserControl
    {
        private TextBox txtSearch;
        private DataGridView dgvResults;
        private Label lblMode;
        private Timer _debounceTimer;
        private ProductRepository _productRepo;
        private bool _searchByCode;

        public event EventHandler<Product> ProductSelected;
        public event EventHandler SearchCancelled;

        public ProductSearchPanel()
        {
            _productRepo = new ProductRepository(DbConnection.GetConnection());
            _debounceTimer = new Timer { Interval = 200 };
            _debounceTimer.Tick += OnDebounceElapsed;
            InitializeLayout();
            this.Visible = false;
        }

        private void InitializeLayout()
        {
            this.Dock = DockStyle.Top;
            this.Height = 250;
            this.BackColor = ThemeConstants.BgPanel;

            // Mode label (F1=Kode / F2=Nama)
            lblMode = new Label
            {
                Text = "Cari Kode:",
                Dock = DockStyle.Top,
                Height = 25,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = ThemeConstants.FgWarning,
                Font = ThemeConstants.FontGrid,
                Padding = new Padding(10, 0, 0, 0)
            };

            // Search textbox
            txtSearch = new TextBox
            {
                Dock = DockStyle.Top,
                BackColor = ThemeConstants.BgInput,
                ForeColor = ThemeConstants.FgPrimary,
                Font = ThemeConstants.FontInput,
                BorderStyle = BorderStyle.FixedSingle
            };
            txtSearch.TextChanged += OnSearchTextChanged;
            txtSearch.KeyDown += OnSearchKeyDown;

            // Results grid
            dgvResults = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true
            };
            BaseForm.ApplyGridTheme(dgvResults);

            dgvResults.Columns.Add("Code", "KODE");
            dgvResults.Columns.Add("Name", "NAMA BARANG");
            dgvResults.Columns.Add("Price", "HARGA");

            dgvResults.Columns["Code"].FillWeight = 120;
            dgvResults.Columns["Name"].FillWeight = 300;
            dgvResults.Columns["Price"].FillWeight = 120;

            dgvResults.KeyDown += OnGridKeyDown;
            dgvResults.CellDoubleClick += OnGridDoubleClick;

            this.Controls.Add(dgvResults);
            this.Controls.Add(txtSearch);
            this.Controls.Add(lblMode);
        }

        public void ShowSearch(bool byCode)
        {
            _searchByCode = byCode;
            lblMode.Text = byCode ? "Cari Kode (F1):" : "Cari Nama (F2):";
            txtSearch.Text = "";
            this.Visible = true;
            txtSearch.Focus();
            LoadResults("");
        }

        public void HideSearch()
        {
            _debounceTimer.Stop();
            this.Visible = false;
            dgvResults.Rows.Clear();
            if (SearchCancelled != null)
            {
                SearchCancelled(this, EventArgs.Empty);
            }
        }

        private void OnSearchTextChanged(object sender, EventArgs e)
        {
            _debounceTimer.Stop();
            _debounceTimer.Start();
        }

        private void OnDebounceElapsed(object sender, EventArgs e)
        {
            _debounceTimer.Stop();
            LoadResults(txtSearch.Text.Trim());
        }

        private void LoadResults(string query)
        {
            dgvResults.Rows.Clear();
            List<Product> results;

            if (string.IsNullOrEmpty(query))
            {
                results = _productRepo.GetAllActive();
            }
            else if (_searchByCode)
            {
                results = _productRepo.SearchByCodePrefix(query, 50);
            }
            else
            {
                results = _productRepo.SearchByName(query, 50);
            }

            foreach (var p in results)
            {
                dgvResults.Rows.Add(
                    p.ProductCode,
                    p.Name,
                    Formatting.FormatCurrencyShort(p.Price));
            }

            if (dgvResults.Rows.Count > 0)
            {
                dgvResults.CurrentCell = dgvResults.Rows[0].Cells[0];
            }
        }

        private void OnSearchKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                SelectCurrentRow();
            }
            else if (e.KeyCode == Keys.Escape)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                HideSearch();
            }
            else if (e.KeyCode == Keys.Down)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                if (dgvResults.Rows.Count > 0)
                {
                    dgvResults.Focus();
                    if (dgvResults.CurrentRow == null)
                    {
                        dgvResults.CurrentCell = dgvResults.Rows[0].Cells[0];
                    }
                }
            }
            else if (e.KeyCode == Keys.Up)
            {
                // Stay in textbox on Up
            }
        }

        private void OnGridKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                SelectCurrentRow();
            }
            else if (e.KeyCode == Keys.Escape)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                HideSearch();
            }
            else if (e.KeyCode == Keys.Back || (e.KeyCode >= Keys.A && e.KeyCode <= Keys.Z)
                || (e.KeyCode >= Keys.D0 && e.KeyCode <= Keys.D9))
            {
                // Typing in grid — redirect to search box
                txtSearch.Focus();
            }
        }

        private void OnGridDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0)
            {
                SelectCurrentRow();
            }
        }

        private void SelectCurrentRow()
        {
            if (dgvResults.CurrentRow == null) return;

            string code = (dgvResults.CurrentRow.Cells["Code"].Value ?? "").ToString();
            if (string.IsNullOrEmpty(code)) return;

            var product = _productRepo.GetByCode(code);
            if (product != null && ProductSelected != null)
            {
                this.Visible = false;
                ProductSelected(this, product);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_debounceTimer != null)
                {
                    _debounceTimer.Stop();
                    _debounceTimer.Dispose();
                }
            }
            base.Dispose(disposing);
        }
    }
}
