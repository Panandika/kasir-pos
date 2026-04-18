using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Models;
using Kasir.Utils;
using Kasir.Avalonia.Forms.Shared;

namespace Kasir.Avalonia.Forms.Master;

public partial class DepartmentWindow : Window
{
    private record DeptRow(string DeptCode, string Name, string ChangedAt, Department Tag);

    private readonly ObservableCollection<DeptRow> _rows = new();
    private DepartmentRepository _deptRepo;
    private int _currentUserId;

    public DepartmentWindow(int currentUserId)
    {
        InitializeComponent();
        _currentUserId = currentUserId;
        _deptRepo = new DepartmentRepository(DbConnection.GetConnection());
        DgvDepts.ItemsSource = _rows;
        SetStatus("Ins=Tambah  Enter=Edit  Del=Hapus  Esc=Keluar");
        LoadData();
    }

    private void LoadData()
    {
        _rows.Clear();
        foreach (var dept in _deptRepo.GetAll())
        {
            _rows.Add(new DeptRow(
                dept.DeptCode,
                dept.Name,
                Formatting.FormatDate(dept.ChangedAt),
                dept));
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (KeyboardRouter.IsInsert(e)) { e.Handled = true; AddDept(); }
        else if (KeyboardRouter.IsEnter(e)) { e.Handled = true; EditDept(); }
        else if (KeyboardRouter.IsDelete(e)) { e.Handled = true; DeleteDept(); }
        else if (KeyboardRouter.IsEscape(e)) { e.Handled = true; Close(); }
    }

    private async void AddDept()
    {
        var (ok, vals) = await InputDialogWindow.Show(this,
            "Tambah Departemen",
            new[] { "Kode Dept", "Nama" },
            new[] { "", "" });

        if (!ok) return;

        string code = vals[0].Trim().ToUpper();
        string name = vals[1].Trim();

        if (!Validators.IsValidDeptCode(code))
        {
            await MsgBox.Show(this, "Kode departemen tidak valid (1-6 karakter).");
            return;
        }

        if (string.IsNullOrEmpty(name))
        {
            await MsgBox.Show(this, "Nama tidak boleh kosong.");
            return;
        }

        var existing = _deptRepo.GetByCode(code);
        if (existing != null)
        {
            await MsgBox.Show(this, $"Kode '{code}' sudah digunakan.");
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
            await MsgBox.Show(this, "Pilih departemen yang akan diedit.");
            return;
        }

        var dept = row.Tag;
        var (ok, vals) = await InputDialogWindow.Show(this,
            "Edit Departemen",
            new[] { "Nama" },
            new[] { dept.Name });

        if (!ok) return;

        string name = vals[0].Trim();

        if (string.IsNullOrEmpty(name))
        {
            await MsgBox.Show(this, "Nama tidak boleh kosong.");
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
            await MsgBox.Show(this, "Pilih departemen yang akan dihapus.");
            return;
        }

        bool confirmed = await MsgBox.Confirm(this, $"Hapus departemen '{row.Name}'?");
        if (!confirmed) return;

        _deptRepo.Delete(row.Tag.Id);
        LoadData();
        SetStatus($"Departemen '{row.Name}' dihapus.");
    }

    private void SetStatus(string text)
    {
        StatusLabel.Text = text;
    }
}
