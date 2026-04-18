using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Models;
using Kasir.Services;
using Kasir.Avalonia.Forms.Shared;
using Kasir.Avalonia.Navigation;

namespace Kasir.Avalonia.Forms.Master;

public partial class PriceChangeView : UserControl
{
    private record PriceRow(string Code, string Name, string OldPrice, string NewPrice, string BuyPrice, Product Tag)
    {
        // NewPrice is mutable for pending changes display
        public string NewPrice { get; set; } = NewPrice;
    }

    private readonly ObservableCollection<PriceRow> _rows = new();
    private readonly List<PriceRow> _allRows = new();
    private readonly ProductRepository _productRepo;
    private readonly PriceChangeService _priceService;

    public PriceChangeView()
    {
        InitializeComponent();
        var conn = DbConnection.GetConnection();
        _productRepo = new ProductRepository(conn);
        _priceService = new PriceChangeService(conn);
        DgvPrices.ItemsSource = _rows;
        TxtSearch.TextChanged += (_, _) => ApplyFilter();
        SetStatus("Ganti Harga Jual — F5: Muat Produk, Enter: Edit Harga, F10: Simpan, Esc: Keluar");
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (KeyboardRouter.IsF5(e))          { e.Handled = true; LoadProducts(); }
        else if (KeyboardRouter.IsEnter(e))  { e.Handled = true; EditSelectedPrice(); }
        else if (KeyboardRouter.IsF10(e))    { e.Handled = true; SaveChanges(); }
        else if (KeyboardRouter.IsEscape(e)) { e.Handled = true; NavigationService.GoBack(); }
    }

    private void LoadProducts()
    {
        _allRows.Clear();
        var products = _productRepo.GetAll(1000, 0);
        foreach (var p in products)
        {
            string oldP = (p.Price / 100.0).ToString("F0");
            string buyP = (p.BuyingPrice / 100.0).ToString("F0");
            _allRows.Add(new PriceRow(p.ProductCode, p.Name, oldP, oldP, buyP, p));
        }
        ApplyFilter();
        SetStatus($"Muat {products.Count} produk. Enter: edit harga terpilih, F10: simpan perubahan.");
    }

    private void ApplyFilter()
    {
        string term = TxtSearch.Text?.Trim().ToLower() ?? "";
        _rows.Clear();
        foreach (var row in _allRows)
        {
            if (string.IsNullOrEmpty(term) ||
                row.Code.ToLower().Contains(term) ||
                row.Name.ToLower().Contains(term))
            {
                _rows.Add(row);
            }
        }
    }

    private async void EditSelectedPrice()
    {
        var row = DgvPrices.SelectedItem as PriceRow;
        if (row == null) { await MsgBox.Show(NavigationService.Owner, "Pilih produk dulu."); return; }

        var (ok, vals) = await InputDialogWindow.Show(NavigationService.Owner,
            $"Edit Harga: {row.Name}",
            new[] { "Harga Jual Baru" },
            new[] { row.NewPrice });

        if (!ok || string.IsNullOrWhiteSpace(vals[0])) return;

        if (!decimal.TryParse(vals[0], out decimal newPriceVal) || newPriceVal < 0)
        { await MsgBox.Show(NavigationService.Owner, "Harga tidak valid."); return; }

        row.NewPrice = ((long)(newPriceVal * 100m) / 100.0).ToString("F0");

        // Refresh to show updated NewPrice in grid
        int idx = _rows.IndexOf(row);
        if (idx >= 0)
        {
            _rows.RemoveAt(idx);
            _rows.Insert(idx, row);
            DgvPrices.SelectedIndex = idx;
        }

        // Mark pending count in status
        int pendingCount = CountPendingChanges();
        SetStatus($"{pendingCount} perubahan harga tertunda. F10 untuk simpan.");
    }

    private int CountPendingChanges()
    {
        int count = 0;
        foreach (var row in _allRows)
        {
            if (row.NewPrice != row.OldPrice) count++;
        }
        return count;
    }

    private async void SaveChanges()
    {
        var changes = new List<PriceChangeEntry>();
        foreach (var row in _allRows)
        {
            if (!decimal.TryParse(row.NewPrice, out decimal newVal)) continue;
            int newPrice = (int)(newVal * 100m);
            if (newPrice == row.Tag.Price) continue;
            changes.Add(new PriceChangeEntry { ProductCode = row.Tag.ProductCode, NewPrice = newPrice });
        }

        if (changes.Count == 0) { await MsgBox.Show(NavigationService.Owner, "Tidak ada perubahan harga."); return; }

        bool confirmed = await MsgBox.Confirm(NavigationService.Owner, $"Simpan {changes.Count} perubahan harga?");
        if (!confirmed) return;

        int count = _priceService.ApplyBatchPriceChange(changes, 1);
        await MsgBox.Show(NavigationService.Owner, $"{count} harga berhasil diubah.");
        LoadProducts();
    }

    private void SetStatus(string text) => StatusLabel.Text = text;
}
