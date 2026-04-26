using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using Kasir.Avalonia.Forms.Shared;
using Kasir.Avalonia.Navigation;
using Kasir.Avalonia.Utils;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Models;
using Kasir.Services;
using Kasir.Utils;

namespace Kasir.Avalonia.Forms.Inventory;

public partial class TransferView : UserControl
{
    private record ItemRow(string Code, string Name, string Qty);

    private readonly ObservableCollection<ItemRow> _rows = new();
    private readonly List<StockTransferItem> _items = new();
    private readonly StockTransferService _service;
    private readonly ProductRepository _productRepo;

    public TransferView()
    {
        InitializeComponent();
        var db = DbConnection.GetConnection();
        _service = new StockTransferService(db, new ClockImpl());
        _productRepo = new ProductRepository(db);

        TxtFrom.Text = "TOKO";
        TxtTo.Text = "GUDANG";

        DgvItems.ItemsSource = _rows;
        FooterStatus.RegisterDefault(StatusLabel, "Transfer Barang — Ins: Add Item, F10: Save, Esc: Close");
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (KeyboardRouter.IsInsert(e)) { AddItem(); e.Handled = true; }
        else if (KeyboardRouter.IsF10(e)) { Save(); e.Handled = true; }
        else if (KeyboardRouter.IsEscape(e)) { NavigationService.GoBack(); e.Handled = true; }
    }

    private async void AddItem()
    {
        var (ok, vals) = await InputDialogWindow.Show(
            NavigationService.Owner,
            "Tambah Barang",
            new[] { "Kode Barang", "Qty" },
            new[] { "", "1" });

        if (!ok) return;

        string code = vals[0].Trim();
        if (string.IsNullOrEmpty(code)) return;

        if (!int.TryParse(vals[1], out int qty) || qty <= 0)
        {
            await MsgBox.Show(NavigationService.Owner, "Qty tidak valid.");
            return;
        }

        var product = _productRepo.GetByCode(code);
        if (product == null)
        {
            await MsgBox.Show(NavigationService.Owner, $"Barang '{code}' tidak ditemukan.");
            return;
        }

        _items.Add(new StockTransferItem
        {
            ProductCode = product.ProductCode,
            ProductName = product.Name,
            Quantity = qty
        });

        RefreshGrid();
    }

    private void RefreshGrid()
    {
        _rows.Clear();
        foreach (var item in _items)
        {
            _rows.Add(new ItemRow(item.ProductCode ?? "", item.ProductName ?? "", item.Quantity.ToString()));
        }
    }

    private async void Save()
    {
        if (_items.Count == 0)
        {
            await MsgBox.Show(NavigationService.Owner, "Tidak ada barang yang diinput.");
            return;
        }

        string from = TxtFrom.Text?.Trim() ?? "";
        string to = TxtTo.Text?.Trim() ?? "";

        if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to))
        {
            await MsgBox.Show(NavigationService.Owner, "Lokasi asal dan tujuan harus diisi.");
            return;
        }

        bool confirmed = await MsgBox.Confirm(NavigationService.Owner, $"Transfer {_items.Count} item dari {from} ke {to}?");
        if (!confirmed) return;

        string journalNo = _service.CreateTransfer(from, to, _items, 1);
        await MsgBox.Show(NavigationService.Owner, $"Tersimpan: {journalNo}");

        _items.Clear();
        RefreshGrid();
    }

    private void SetStatus(string text) => FooterStatus.Show(StatusLabel, text);
}
