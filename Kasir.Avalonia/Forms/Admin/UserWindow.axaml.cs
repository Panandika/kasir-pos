using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using Kasir.Auth;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Models;
using Kasir.Utils;
using Kasir.Avalonia.Forms.Shared;

namespace Kasir.Avalonia.Forms.Admin;

public partial class UserWindow : Window
{
    private record UserRow(string Username, string DisplayName, string Alias, string Role, string Active, User Tag);

    private readonly ObservableCollection<UserRow> _rows = new();
    private readonly UserRepository _userRepo;
    private readonly RoleRepository _roleRepo;
    private List<Role> _roles = new();

    public UserWindow()
    {
        InitializeComponent();
        _userRepo = new UserRepository(DbConnection.GetConnection());
        _roleRepo = new RoleRepository(DbConnection.GetConnection());
        _roles = _roleRepo.GetAll();
        DgvUsers.ItemsSource = _rows;
        SetStatus("Ins=Tambah  Enter=Edit  Del=Hapus  P=Ganti Password  Esc=Keluar");
        LoadData();
    }

    private void LoadData()
    {
        _rows.Clear();
        foreach (var user in _userRepo.GetAll())
        {
            string roleName = GetRoleName(user.RoleId);
            string active = user.IsActive == 1 ? "Ya" : "Tidak";
            _rows.Add(new UserRow(user.Username, user.DisplayName ?? "", user.Alias ?? "", roleName, active, user));
        }
    }

    private string GetRoleName(int roleId)
    {
        foreach (var r in _roles)
            if (r.Id == roleId) return r.Name ?? roleId.ToString();
        return roleId.ToString();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (KeyboardRouter.IsInsert(e)) { e.Handled = true; AddUser(); }
        else if (KeyboardRouter.IsEnter(e)) { e.Handled = true; EditUser(); }
        else if (KeyboardRouter.IsDelete(e)) { e.Handled = true; DeleteUser(); }
        else if (e.Key == Key.P && e.KeyModifiers == KeyModifiers.None) { e.Handled = true; ChangePassword(); }
        else if (KeyboardRouter.IsEscape(e)) { e.Handled = true; Close(); }
    }

    private async void AddUser()
    {
        var (ok, vals) = await InputDialogWindow.Show(this,
            "Tambah User",
            new[] { "Username", "Display Name", "Alias (3 char)", "Password", "Role ID (1=admin 2=supervisor 3=cashier)" },
            new[] { "", "", "", "", "3" });

        if (!ok) return;

        var user = new User
        {
            Username = vals[0].Trim().ToUpper(),
            DisplayName = vals[1].Trim(),
            Alias = vals[2].Trim(),
            PasswordHash = AuthService.HashPassword(vals[3]),
            PasswordSalt = "",
            RoleId = int.Parse(vals[4]),
            IsActive = 1
        };
        _userRepo.Insert(user);
        LoadData();
        SetStatus($"User '{user.Username}' ditambahkan.");
    }

    private async void EditUser()
    {
        var row = DgvUsers.SelectedItem as UserRow;
        if (row == null)
        {
            await MsgBox.Show(this, "Pilih user yang akan diedit.");
            return;
        }

        var user = row.Tag;
        var (ok, vals) = await InputDialogWindow.Show(this,
            "Edit User",
            new[] { "Display Name", "Alias", "Role ID", "Active (1/0)" },
            new[] { user.DisplayName ?? "", user.Alias ?? "", user.RoleId.ToString(), user.IsActive.ToString() });

        if (!ok) return;

        user.DisplayName = vals[0].Trim();
        user.Alias = vals[1].Trim();
        user.RoleId = int.Parse(vals[2]);
        user.IsActive = int.Parse(vals[3]);
        _userRepo.Update(user);
        LoadData();
        SetStatus($"User '{user.Username}' diperbarui.");
    }

    private async void DeleteUser()
    {
        var row = DgvUsers.SelectedItem as UserRow;
        if (row == null)
        {
            await MsgBox.Show(this, "Pilih user yang akan dihapus.");
            return;
        }

        var user = row.Tag;
        if (user.Id == 1)
        {
            await MsgBox.Show(this, "User admin utama tidak dapat dihapus.");
            return;
        }

        bool confirmed = await MsgBox.Confirm(this, $"Hapus user '{user.Username}'?");
        if (!confirmed) return;

        _userRepo.Delete(user.Id);
        LoadData();
        SetStatus($"User '{user.Username}' dihapus.");
    }

    private async void ChangePassword()
    {
        var row = DgvUsers.SelectedItem as UserRow;
        if (row == null)
        {
            await MsgBox.Show(this, "Pilih user untuk ganti password.");
            return;
        }

        var user = row.Tag;
        var (ok, vals) = await InputDialogWindow.Show(this,
            "Ganti Password",
            new[] { "Password baru" },
            new[] { "" });

        if (!ok) return;

        string hash = AuthService.HashPassword(vals[0]);
        _userRepo.UpdatePassword(user.Id, hash, "");
        await MsgBox.Show(this, "Password diubah.");
    }

    private void SetStatus(string t) => StatusLabel.Text = t;
}
