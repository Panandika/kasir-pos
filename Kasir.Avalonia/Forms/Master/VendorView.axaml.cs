using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Models;
using Kasir.Utils;
using Kasir.Avalonia.Utils;
using Kasir.Avalonia.Forms.Shared;
using Kasir.Avalonia.Navigation;
using Kasir.Avalonia.Infrastructure;

namespace Kasir.Avalonia.Forms.Master;

public partial class VendorView : UserControl
{
    private record VendorRow(string Code, string Name, string Address, string City, string Phone, Subsidiary Tag);

    private readonly ObservableCollection<VendorRow> _rows = new();
    private readonly SubsidiaryRepository _repo;
    private readonly int _userId;

    public VendorView(int userId = 1)
    {
        InitializeComponent();
        _userId = userId;
        _repo = new SubsidiaryRepository(DbConnection.GetConnection());
        DgvVendors.ItemsSource = _rows;
        BtnSearch.Click += (_, _) => SearchVendors();
        TxtSearch.KeyDown += OnSearchKeyDown;
        TxtSearch.TextChanged += (_, _) => SearchVendors();
        ViewShortcuts.WireGridEnter(DgvVendors, EditVendor);
        FooterStatus.RegisterDefault(StatusLabel, "F2=Cari  Ins=Tambah  Enter=Ubah  Esc=Keluar");
        LoadData();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ViewShortcuts.AutoFocus(TxtSearch);
    }

    private void LoadData()
    {
        _rows.Clear();
        foreach (var v in _repo.GetAllByGroup("1", 500, 0))
        {
            _rows.Add(new VendorRow(v.SubCode, v.Name, v.Address ?? "", v.City ?? "", v.Phone ?? "", v));
        }
    }

    private void SearchVendors()
    {
        string q = TxtSearch.Text?.Trim() ?? "";
        _rows.Clear();
        if (string.IsNullOrEmpty(q))
        {
            foreach (var v in _repo.GetAllByGroup("1", 500, 0))
                _rows.Add(new VendorRow(v.SubCode, v.Name, v.Address ?? "", v.City ?? "", v.Phone ?? "", v));
        }
        else
        {
            foreach (var v in _repo.SearchByName(q, "1", 100))
                _rows.Add(new VendorRow(v.SubCode, v.Name, v.Address ?? "", v.City ?? "", v.Phone ?? "", v));
        }
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (KeyboardRouter.IsEnter(e))
        {
            e.Handled = true;
            SearchVendors();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (KeyboardRouter.IsF2(e)) { e.Handled = true; TxtSearch.Focus(); }
        else if (KeyboardRouter.IsInsert(e)) { e.Handled = true; AddVendor(); }
        else if (KeyboardRouter.IsEnter(e)) { e.Handled = true; EditVendor(); }
        else if (KeyboardRouter.IsEscape(e)) { e.Handled = true; NavigationService.GoBack(); }
    }

    private async void AddVendor()
    {
        var (ok, vals) = await InputDialogWindow.Show(NavigationService.Owner,
            "Tambah Vendor",
            new[] { "Kode", "Nama", "Alamat", "Kota" },
            new[] { "", "", "", "" });

        if (!ok) return;

        var vendor = new Subsidiary
        {
            SubCode = vals[0].Trim().ToUpper(),
            Name = vals[1].Trim(),
            Address = vals[2].Trim(),
            City = vals[3].Trim(),
            GroupCode = "1",
            Status = "A",
            ChangedBy = _userId
        };
        _repo.Insert(vendor);
        LoadData();
        SetStatus($"Vendor '{vendor.Name}' ditambahkan.");
    }

    private async void EditVendor()
    {
        var row = DgvVendors.SelectedItem as VendorRow;
        if (row == null)
        {
            await MsgBox.Show(NavigationService.Owner, "Pilih vendor yang akan diedit.");
            return;
        }

        var vendor = row.Tag;
        var (ok, vals) = await InputDialogWindow.Show(NavigationService.Owner,
            "Edit Vendor",
            new[] { "Nama", "Alamat", "Kota", "Telepon" },
            new[] { vendor.Name ?? "", vendor.Address ?? "", vendor.City ?? "", vendor.Phone ?? "" });

        if (!ok) return;

        vendor.Name = vals[0].Trim();
        vendor.Address = vals[1].Trim();
        vendor.City = vals[2].Trim();
        vendor.Phone = vals[3].Trim();
        vendor.ChangedBy = _userId;
        _repo.Update(vendor);
        LoadData();
        SetStatus($"Vendor '{vendor.Name}' diperbarui.");
    }

    private void SetStatus(string t) => FooterStatus.Show(StatusLabel, t);
}
