using System.Collections.Generic;
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

public partial class DepartmentView : UserControl
{
    private record DeptRow(string DeptCode, string Name, string ChangedAt, Department Tag);

    private readonly ObservableCollection<DeptRow> _rows = new();
    private readonly List<DeptRow> _allRows = new();
    private DepartmentRepository _deptRepo;
    private int _currentUserId;

    public DepartmentView(int currentUserId)
    {
        InitializeComponent();
        _currentUserId = currentUserId;
        _deptRepo = new DepartmentRepository(DbConnection.GetConnection());
        DgvDepts.ItemsSource = _rows;
        TxtSearch.TextChanged += (_, _) => FilterGrid(TxtSearch.Text ?? "");
        ViewShortcuts.WireGridEnter(DgvDepts, EditDept);
        FooterStatus.RegisterDefault(StatusLabel, "Ins=Tambah  Enter=Edit  Del=Hapus  Esc=Keluar");
        LoadData();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ViewShortcuts.AutoFocus(TxtSearch);
    }

    private void LoadData()
    {
        _allRows.Clear();
        foreach (var dept in _deptRepo.GetAll())
        {
            _allRows.Add(new DeptRow(
                dept.DeptCode,
                dept.Name,
                Formatting.FormatDate(dept.ChangedAt),
                dept));
        }
        FilterGrid(TxtSearch.Text ?? "");
    }

    private void FilterGrid(string query)
    {
        string q = query.Trim().ToLower();
        _rows.Clear();
        foreach (var row in _allRows)
        {
            if (string.IsNullOrEmpty(q) ||
                row.DeptCode.ToLower().Contains(q) ||
                row.Name.ToLower().Contains(q))
            {
                _rows.Add(row);
            }
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (KeyboardRouter.IsInsert(e)) { e.Handled = true; AddDept(); }
        else if (KeyboardRouter.IsEnter(e)) { e.Handled = true; EditDept(); }
        else if (KeyboardRouter.IsDelete(e)) { e.Handled = true; DeleteDept(); }
        else if (KeyboardRouter.IsEscape(e)) { e.Handled = true; NavigationService.GoBack(); }
    }

    private async void AddDept()
    {
        var (ok, vals) = await InputDialogWindow.Show(NavigationService.Owner,
            "Tambah Departemen",
            new[] { "Kode Dept", "Nama" },
            new[] { "", "" });

        if (!ok) return;

        string code = vals[0].Trim().ToUpper();
        string name = vals[1].Trim();

        if (!Validators.IsValidDeptCode(code))
        {
            await MsgBox.Show(NavigationService.Owner, "Kode departemen tidak valid (1-6 karakter).");
            return;
        }

        if (string.IsNullOrEmpty(name))
        {
            await MsgBox.Show(NavigationService.Owner, "Nama tidak boleh kosong.");
            return;
        }

        var existing = _deptRepo.GetByCode(code);
        if (existing != null)
        {
            await MsgBox.Show(NavigationService.Owner, $"Kode '{code}' sudah digunakan.");
            return;
        }

        var dept = new Department
        {
            DeptCode = code,
            Name = name,
            ChangedBy = _currentUserId
        };
        _deptRepo.Insert(dept);
        LoadData();
        SetStatus($"Departemen '{name}' ditambahkan.");
    }

    private async void EditDept()
    {
        var row = DgvDepts.SelectedItem as DeptRow;
        if (row == null)
        {
            await MsgBox.Show(NavigationService.Owner, "Pilih departemen yang akan diedit.");
            return;
        }

        var dept = row.Tag;
        var (ok, vals) = await InputDialogWindow.Show(NavigationService.Owner,
            "Edit Departemen",
            new[] { "Nama" },
            new[] { dept.Name });

        if (!ok) return;

        string name = vals[0].Trim();

        if (string.IsNullOrEmpty(name))
        {
            await MsgBox.Show(NavigationService.Owner, "Nama tidak boleh kosong.");
            return;
        }

        dept.Name = name;
        dept.ChangedBy = _currentUserId;
        _deptRepo.Update(dept);
        LoadData();
        SetStatus($"Departemen '{name}' diperbarui.");
    }

    private async void DeleteDept()
    {
        var row = DgvDepts.SelectedItem as DeptRow;
        if (row == null)
        {
            await MsgBox.Show(NavigationService.Owner, "Pilih departemen yang akan dihapus.");
            return;
        }

        bool confirmed = await MsgBox.Confirm(NavigationService.Owner, $"Hapus departemen '{row.Name}'?");
        if (!confirmed) return;

        _deptRepo.Delete(row.Tag.Id);
        LoadData();
        SetStatus($"Departemen '{row.Name}' dihapus.");
    }

    private void SetStatus(string text) => FooterStatus.Show(StatusLabel, text);
}
