using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Models;
using Kasir.Utils;
using Kasir.Avalonia.Behaviors;
using Kasir.Avalonia.Forms.Shared;
using Kasir.Avalonia.Navigation;
using Kasir.Avalonia.Infrastructure;
using Kasir.Avalonia.Utils;
using Kasir.Services;

namespace Kasir.Avalonia.Forms.Master;

public partial class ProductView : UserControl
{
    private record ProductRow(string Code, string Name, string Price, string StockStore, string StockWarehouse, string Status, Product Tag);
    private readonly ObservableCollection<ProductRow> _rows = new();
    private ProductRepository _productRepo;
    private DepartmentRepository _deptRepo;
    private InventoryService _inventoryService;
    private Product? _currentProduct;
    private bool _isEditing;
    private int _userId;

    public ProductView(int userId = 1)
    {
        InitializeComponent();
        _userId = userId;
        var conn = DbConnection.GetConnection();
        _productRepo = new ProductRepository(conn);
        _deptRepo = new DepartmentRepository(conn);
        _inventoryService = new InventoryService(conn);
        DgvProducts.ItemsSource = _rows;
        LoadDepts();
        LoadGrid();
        SetDetailEnabled(false);
        DgvProducts.SelectionChanged += OnSelectionChanged;
        TxtSearch.KeyDown += OnSearchKeyDown;
        TxtSearch.TextChanged += (_, _) => SearchProducts();
        BtnSearch.Click += (_, _) => SearchProducts();

        // Numeric input behavior on numeric TextBoxes
        NumericInputBehavior.Attach(TxtDiscMax);
        NumericInputBehavior.AttachLiveFormatting(TxtBuyingPrice);
        NumericInputBehavior.AttachLiveFormatting(TxtCostPrice);
        NumericInputBehavior.AttachLiveFormatting(TxtSellingPrice);

        ViewShortcuts.WireGridEnter(DgvProducts, () =>
        {
            if (!_isEditing && _currentProduct != null)
            {
                SetDetailEnabled(true);
                TxtName.Focus();
                SetStatus("Edit: " + _currentProduct.ProductCode);
            }
        });
        FooterStatus.RegisterDefault(StatusLabel, "F2=Cari  F8=Harga Grosir  F10=Simpan  Esc=Batal");
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ViewShortcuts.AutoFocus(TxtSearch);
    }

    private void LoadDepts()
    {
        CboDept.ItemsSource = _deptRepo.GetAll().Select(d => $"{d.DeptCode} - {d.Name}").ToList();
    }

    private void LoadGrid()
    {
        _rows.Clear();
        foreach (var p in _productRepo.GetAll(500, 0))
            _rows.Add(MakeRow(p));
    }

    private void SearchProducts()
    {
        string q = TxtSearch.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(q)) { LoadGrid(); return; }
        _rows.Clear();
        foreach (var p in _productRepo.SearchByText(q, 100))
            _rows.Add(MakeRow(p));
        SetStatus($"Found {_rows.Count} products");
    }

    private ProductRow MakeRow(Product p)
    {
        int stockStore = _inventoryService.GetStockOnHandByLocation(p.ProductCode, "TOKO");
        int stockWarehouse = _inventoryService.GetStockOnHandByLocation(p.ProductCode, "GUDANG");
        return new ProductRow(
            p.ProductCode,
            p.Name,
            Formatting.FormatCurrencyShort(p.Price),
            stockStore.ToString(),
            stockWarehouse.ToString(),
            p.Status,
            p);
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
        TxtUnit.Text = p.Unit ?? "";
        TxtVendorCode.Text = p.VendorCode ?? "";
        TxtBuyingPrice.Text = FormatMoney(p.BuyingPrice);
        TxtCostPrice.Text = FormatMoney(p.CostPrice);
        TxtSellingPrice.Text = FormatMoney(p.Price);
        TxtDiscMax.Text = (p.DiscPct / 100.0).ToString("F2", CultureInfo.InvariantCulture);
        LblMargin.Text = (p.MarginPct / 100.0).ToString("F2", CultureInfo.InvariantCulture);
        CboStatus.SelectedIndex = p.Status == "I" ? 1 : 0;
        var items = CboDept.ItemsSource as List<string>;
        if (items != null)
        {
            int idx = items.FindIndex(s => s.StartsWith(p.DeptCode ?? ""));
            CboDept.SelectedIndex = idx >= 0 ? idx : -1;
        }

        // Stok grid: Maximum / Ideal / Minimum / Awal / Sekarang per location (T = Toko, G = Gudang)
        // qty_max/qty_min/qty_order are stored × 100 (display whole units)
        string max = (p.QtyMax / 100).ToString();
        string ideal = (p.QtyOrder / 100).ToString();
        string min = (p.QtyMin / 100).ToString();
        LblStokMaxG.Text = max; LblStokMaxT.Text = max;
        LblStokIdealG.Text = ideal; LblStokIdealT.Text = ideal;
        LblStokMinG.Text = min; LblStokMinT.Text = min;

        // Awal (period opening) — placeholder until stock_register is populated by posting.
        // For now show "0" for both columns; production data has zero rows in stock_register.
        LblStokAwalG.Text = "0";
        LblStokAwalT.Text = "0";

        // Sekarang = current on-hand from stock_movements aggregate (matches grid)
        int nowT = _inventoryService.GetStockOnHandByLocation(p.ProductCode ?? "", "TOKO");
        int nowG = _inventoryService.GetStockOnHandByLocation(p.ProductCode ?? "", "GUDANG");
        LblStokNowT.Text = nowT.ToString();
        LblStokNowG.Text = nowG.ToString();
    }

    private static string FormatMoney(long cents)
    {
        // Display whole rupiah with Indonesian thousands separators
        long whole = cents / 100;
        return whole.ToString("#,0", CultureInfo.GetCultureInfo("id-ID"));
    }

    private Product ReadDetail()
    {
        var p = _currentProduct ?? new Product();
        p.ProductCode = TxtCode.Text?.Trim() ?? "";
        p.Name = TxtName.Text?.Trim() ?? "";
        p.Unit = TxtUnit.Text?.Trim() ?? "";
        p.VendorCode = TxtVendorCode.Text?.Trim() ?? "";
        p.BuyingPrice = ParseMoney(TxtBuyingPrice.Text);
        p.CostPrice = ParseMoney(TxtCostPrice.Text);
        p.Price = ParseMoney(TxtSellingPrice.Text);
        p.DiscPct = ParsePct(TxtDiscMax.Text);
        p.Status = CboStatus.SelectedIndex == 1 ? "I" : "A";
        if (CboDept.SelectedItem is string deptItem)
        {
            int dash = deptItem.IndexOf(" - ");
            p.DeptCode = dash > 0 ? deptItem.Substring(0, dash) : "";
        }
        p.ChangedBy = _userId;
        return p;
    }

    private static long ParseMoney(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0L;
        // Strip Indonesian thousands dots, then parse as whole rupiah
        string digits = new string((text ?? "").Where(char.IsDigit).ToArray());
        if (string.IsNullOrEmpty(digits)) return 0L;
        return long.Parse(digits, CultureInfo.InvariantCulture) * 100L;
    }

    private static int ParsePct(string? text)
    {
        if (decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal v))
            return (int)(v * 100m);
        if (decimal.TryParse(text, NumberStyles.Any, CultureInfo.GetCultureInfo("id-ID"), out v))
            return (int)(v * 100m);
        return 0;
    }

    private void SetDetailEnabled(bool enabled)
    {
        _isEditing = enabled;
        TxtCode.IsEnabled = TxtName.IsEnabled = TxtUnit.IsEnabled = TxtVendorCode.IsEnabled = enabled;
        TxtBuyingPrice.IsEnabled = TxtCostPrice.IsEnabled = TxtSellingPrice.IsEnabled = enabled;
        TxtDiscMax.IsEnabled = enabled;
        CboDept.IsEnabled = CboStatus.IsEnabled = enabled;
    }

    private async void SaveProduct()
    {
        if (!_isEditing) return;
        var p = ReadDetail();
        if (string.IsNullOrEmpty(p.ProductCode) || string.IsNullOrEmpty(p.Name))
        {
            await MsgBox.Show(NavigationService.Owner, "Kode dan nama barang harus diisi.");
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
        catch (Exception ex) { await MsgBox.Show(NavigationService.Owner, "Gagal: " + ex.Message); }
    }

    private async void OpenWholesaleDialog()
    {
        if (_currentProduct == null) return;
        var dlg = new WholesaleTierDialog(_currentProduct);
        var owner = NavigationService.Owner;
        if (owner == null) return;
        var result = await dlg.ShowDialog<bool>(owner);
        if (result)
        {
            // Persist updated tier values
            try
            {
                if (_currentProduct.Id != 0)
                {
                    _productRepo.Update(_currentProduct);
                    LoadGrid();
                }
                SetStatus("Harga grosir tersimpan");
            }
            catch (Exception ex) { await MsgBox.Show(owner, "Gagal: " + ex.Message); }
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (KeyboardRouter.IsF2(e)) { e.Handled = true; TxtSearch.Focus(); }
        else if (e.Key == Key.F8)
        {
            e.Handled = true;
            OpenWholesaleDialog();
        }
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
        else if (e.Key == Key.F10) { e.Handled = true; SaveProduct(); }
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
            else NavigationService.GoBack();
        }
    }

    private async void DeactivateProduct()
    {
        if (_currentProduct == null || _currentProduct.Id == 0) return;
        bool ok = await MsgBox.Confirm(NavigationService.Owner, $"Nonaktifkan produk '{_currentProduct.Name}'?");
        if (!ok) return;
        _productRepo.Deactivate(_currentProduct.Id, _userId);
        LoadGrid();
        SetStatus("Produk dinonaktifkan.");
    }

    private void SetStatus(string t) => FooterStatus.Show(StatusLabel, t);
}
