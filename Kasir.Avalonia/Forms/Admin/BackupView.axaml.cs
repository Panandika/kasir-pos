using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Kasir.Avalonia.Forms.Shared;
using Kasir.Avalonia.Navigation;
using Kasir.Avalonia.Utils;
using Kasir.Services;

namespace Kasir.Avalonia.Forms.Admin;

public partial class BackupView : UserControl
{
    public BackupView()
    {
        InitializeComponent();
        BtnBackup.Click += async (_, _) => await OnBackup();
        FooterStatus.RegisterDefault(StatusLabel, "Backup Database — Klik tombol untuk backup — Esc=Keluar");
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (KeyboardRouter.IsEscape(e)) { e.Handled = true; NavigationService.GoBack(); }
    }

    private async System.Threading.Tasks.Task OnBackup()
    {
        string src = System.IO.Path.Combine(AppContext.BaseDirectory, "data", "kasir.db");
        if (!File.Exists(src))
        {
            await MsgBox.Show(NavigationService.Owner, "Database tidak ditemukan: " + src);
            return;
        }

        var folders = await NavigationService.Owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Pilih folder backup"
        });

        if (folders.Count == 0) return;

        try
        {
            string dest = System.IO.Path.Combine(
                folders[0].Path.LocalPath,
                $"kasir_{DateTime.Now:yyyyMMdd_HHmmss}.db");
            // kasir.db is in WAL mode: File.Copy would miss commits still in kasir.db-wal.
            // BackupService takes a consistent online snapshot and verifies it first.
            SetStatus("Membuat backup...");
            await System.Threading.Tasks.Task.Run(() => BackupService.BackupTo(src, dest));
            SetStatus("Backup selesai");
            await MsgBox.Show(NavigationService.Owner, $"Backup tersimpan:\n{dest}");
        }
        catch (Exception ex)
        {
            SetStatus("Backup gagal");
            await MsgBox.Show(NavigationService.Owner, "Backup gagal: " + ex.Message);
        }
    }

    private void SetStatus(string t) => FooterStatus.Show(StatusLabel, t);
}
