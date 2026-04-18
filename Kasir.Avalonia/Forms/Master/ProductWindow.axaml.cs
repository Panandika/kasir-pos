using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Models;
using Kasir.Utils;
using Kasir.Avalonia.Forms.Shared;

namespace Kasir.Avalonia.Forms.Master;

public partial class ProductWindow : Window
{
    private record ProductRow(string Code, string Name, string Barcode, string Price, string Status, Product Tag);
    private readonly ObservableCollection<ProductRow> _rows = new();
    private ProductRepository _productRepo;
    private DepartmentRepository _deptRepo;
    private Product? _currentProduct;
    private bool _isEditing;
    private int _userId;

    public ProductWindow(int userId = 1)
    {
        InitializeComponent();
        _userId = userId;
        var conn = DbConnection.GetConnection();
        _productRepo = new ProductRepository(conn);
        _deptRepo = new DepartmentRepository(conn);
        DgvProducts.ItemsSource = _rows;
        LoadDepts();
        LoadGrid();
        SetDetailEnabled(false);
        DgvProducts.SelectionChanged += OnSelectionChanged;
        TxtSearch.KeyDown += OnSearchKeyDown;
        BtnSearch.Click += (_, _) => SearchProducts();
        SetStatus("F2=Cari  Ins=Tambah  Enter=Edit  F9=Simpan  Del=Nonaktifkan  Esc=Keluar");
    }

    private void LoadDepts()
    {
        CboDept.ItemsSource = _deptRepo.GetAll().Select(d => $"{d.DeptCode} - {d.Name}").ToList();
    }

    private void LoadGrid()
    {
        _rows.Clear();
        foreach (var p in _productRepo.GetAll(500, 0))
            _rows.Add(new ProductRow(p.ProductCode, p.Name, p.Barcode ?? "", Formatting.FormatCurrencyShort(p.Price), p.Status, p));
    }

    private void SearchProducts()
    {
        string q = TxtSearch.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(q)) { LoadGrid(); return; }
        _rows.Clear();
        foreach (var p in _productRepo.SearchByText(q, 100))
            _rows.Add(new ProductRow(p.ProductCode, p.Name, p.Barcode ?? "", Formatting.FormatCurrencyShort(p.Price), p.Status, p));
        SetStatus($"Found {_rows.Count} products");
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (KeyboardRouter.IsEnter(e)) { e.Handled = true; SearchProducts(); }
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isEditing) return;
        if (DgvProducts.SelectedItem is ProductRow row)
        {
            _currentProduct = row.Tag;
            PopulateDetail(_currentProduct);
        }
    }

    private void PopulateDetail(Product p)
    {
        TxtCode.Text = p.ProductCode ?? "";
        TxtName.Text = p.Name ?? "";
        TxtBarcode.Text = p.Barcode ?? "";
        TxtUnit.Text = p.Unit ?? "";
        TxtPrice.Text = (p.Price / 100.0).ToString("F0");
        TxtPrice1.Text = (p.Price1 / 100.0).ToString("F0");
        TxtPrice2.Text = (p.Price2 / 100.0).ToString("F0");
        TxtPrice3.Text = (p.Price3 / 100.0).ToString("F0");
        TxtPrice4.Text = (p.Price4 / 100.0).ToString("F0");
        TxtBuyingPrice.Text = (p.BuyingPrice / 100.0).ToString("F0");
        TxtCostPrice.Text = (p.CostPrice / 100.0).ToString("F0");
        TxtQtyBreak2.Text = p.QtyBreak2.ToString();
        TxtQtyBreak3.Text = p.QtyBreak3.ToString();
        TxtDiscPct.Text = (p.DiscPct / 100.0).ToString("F2");
        TxtVendorCode.Text = p.VendorCode ?? "";
        CboOpenPrice.SelectedIndex = p.OpenPrice == "Y" ? 1 : 0;
        CboVatFlag.SelectedIndex = p.VatFlag == "Y" ? 1 : 0;
        CboStatus.SelectedIndex = p.Status == "I" ? 1 : 0;
        var items = CboDept.ItemsSource as List<string>;
        if (items != null)
        {
            int idx = items.FindIndex(s => s.StartsWith(p.DeptCode ?? ""));
            CboDept.SelectedIndex = idx >= 0 ? idx : -1;
        }
    }

    private Product ReadDetail()
    {
        var p = _currentProduct ?? new Product();
        p.ProductCode = TxtCode.Text?.Trim() ?? "";
        p.Name = TxtName.Text?.Trim() ?? "";
        p.Barcode = TxtBarcode.Text?.Trim() ?? "";
        p.Unit = TxtUnit.Text?.Trim() ?? "";
        p.Price = ParseMoney(TxtPrice.Text);
        p.Price1 = ParseMoney(TxtPrice1.Text);
        p.Price2 = ParseMoney(TxtPrice2.Text);
        p.Price3 = ParseMoney(TxtPrice3.Text);
        p.Price4 = ParseMoney(TxtPrice4.Text);
        p.BuyingPrice = ParseMoney(TxtBuyingPrice.Text);
        p.CostPrice = ParseMoney(TxtCostPrice.Text);
        p.QtyBreak2 = int.TryParse(TxtQtyBreak2.Text, out int qb2) ? qb2 : 0;
        p.QtyBreak3 = int.TryParse(TxtQtyBreak3.Text, out int qb3) ? qb3 : 0;
        p.DiscPct = (int)(decimal.TryParse(TxtDiscPct.Text, out decimal dp) ? dp * 100m : 0);
        p.VendorCode = TxtVendorCode.Text?.Trim() ?? "";
        p.OpenPrice = CboOpenPrice.SelectedIndex == 1 ? "Y" : "N";
        p.VatFlag = CboVatFlag.SelectedIndex == 1 ? "Y" : "N";
        p.Status = CboStatus.SelectedIndex == 1 ? "I" : "A";
        if (CboDept.SelectedItem is string deptItem)
        {
            int dash = deptItem.IndexOf(" - ");
            p.DeptCode = dash > 0 ? deptItem.Substring(0, dash) : "";
        }
        p.ChangedBy = _userId;
        return p;
    }

    private static int ParseMoney(string? text)
    {
        return decimal.TryParse(text, out decimal v) ? (int)(v * 100m) : 0;
    }

    private void SetDetailEnabled(bool enabled)
    {
        _isEditing = enabled;
        TxtCode.IsEnabled = TxtName.IsEnabled = TxtBarcode.IsEnabled = TxtUnit.IsEnabled = enabled;
        TxtPrice.IsEnabled = TxtPrice1.IsEnabled = TxtPrice2.IsEnabled = TxtPrice3.IsEnabled = TxtPrice4.IsEnabled = enabled;
        TxtBuyingPrice.IsEnabled = TxtCostPrice.IsEnabled = TxtQtyBreak2.IsEnabled = TxtQtyBreak3.IsEnabled = enabled;
        TxtDiscPct.IsEnabled = TxtVendorCode.IsEnabled = enabled;
        CboDept.IsEnabled = CboStatus.IsEnabled = CboOpenPrice.IsEnabled = CboVatFlag.IsEnabled = enabled;
    }

    private async void SaveProduct()
    {
        if (!_isEditing) return;
        var p = ReadDetail();
        if (string.IsNullOrEmpty(p.ProductCode) || string.IsNullOrEmpty(p.Name))
        {
            await MsgBox.Show(this, "Kode dan nama barang harus diisi.");
            return;
        }
        try
        {
            if (p.Id == 0) _productRepo.Insert(p);
            else _productRepo.Update(p);
            SetDetailEnabled(false);
            LoadGrid();
            SetStatus("Produk tersimpan: " + p.ProductCode);
        }
        catch (Exception ex) { await MsgBox.Show(this, "Gagal: " + ex.Message); }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (KeyboardRouter.IsF2(e)) { e.Handled = true; TxtSearch.Focus(); }
        else if (KeyboardRouter.IsInsert(e))
        {
            e.Handled = true;
            _currentProduct = new Product();
            PopulateDetail(_currentProduct);
            SetDetailEnabled(true);
            TxtCode.Focus();
            SetStatus("Tambah produk baru...");
        }
        else if (KeyboardRouter.IsEnter(e) && !_isEditing && _currentProduct != null)
        {
            e.Handled = true;
            SetDetailEnabled(true);
            TxtName.Focus();
            SetStatus("Edit: " + _currentProduct.ProductCode);
        }
        else if (KeyboardRouter.IsF9(e)) { e.Handled = true; SaveProduct(); }
        else if (KeyboardRouter.IsDelete(e))
        {
            e.Handled = true;
            DeactivateProduct();
        }
        else if (KeyboardRouter.IsEscape(e))
        {
            e.Handled = true;
            if (_isEditing) { SetDetailEnabled(false); SetStatus("Edit dibatalkan."); }
            else Close();
        }
    }

    private async void DeactivateProduct()
    {
        if (_currentProduct == null || _currentProduct.Id == 0) return;
        bool ok = await MsgBox.Confirm(this, $"Nonaktifkan produk '{_currentProduct.Name}'?");
        if (!ok) return;
        _productRepo.Deactivate(_currentProduct.Id, _userId);
        LoadGrid();
        SetStatus("Produk dinonaktifkan.");
    }

    private void SetStatus(string t) => StatusLabel.Text = t;
}
