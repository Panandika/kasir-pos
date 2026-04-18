using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Services;
using Kasir.Utils;
using Kasir.Avalonia.Forms.Shared;

namespace Kasir.Avalonia.Forms.Admin;

public partial class UpdateWindow : Window
{
    private readonly UpdateService _updateService;
    private bool _updateAvailable;
    private string? _newVersion;

    public UpdateWindow()
    {
        _updateService = new UpdateService(DbConnection.GetConnection());
        InitializeComponent();

        string syncRole = new ConfigRepository(DbConnection.GetConnection()).Get("sync_role") ?? "hub";
        BtnImportZip.IsVisible = (syncRole == "hub");
        LblCurrentVersion.Text = $"Kasir v{AppVersion.Current}";

        BtnCheck.Click    += (_, _) => CheckForUpdate();
        BtnApply.Click    += (_, _) => ApplyUpdate();
        BtnImportZip.Click += BtnImportZip_Click;

        SetStatus("Periksa Update — F5=Periksa  F8=Update  Esc=Keluar");
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (KeyboardRouter.IsF5(e))
        {
            e.Handled = true;
            CheckForUpdate();
        }
        else if (KeyboardRouter.IsF8(e))
        {
            e.Handled = true;
            if (_updateAvailable) ApplyUpdate();
        }
        else if (KeyboardRouter.IsEscape(e))
        {
            e.Handled = true;
            Close();
        }
    }

    private async void CheckForUpdate()
    {
        LblStatus.Text = UpdateMessages.Checking;
        _updateAvailable = false;
        TxtPatchNotes.IsVisible = false;
        BtnApply.IsVisible = false;

        try
        {
            var result = await _updateService.CheckForUpdateAsync();
            if (!string.IsNullOrEmpty(result.Error))
            {
                LblStatus.Text = result.Error;
                return;
            }

            if (result.Available)
            {
                _updateAvailable = true;
                _newVersion = result.NewVersion;
                LblStatus.Text = $"Update tersedia: v{result.NewVersion}";
                string notes = await Task.Run(() => _updateService.GetPatchNotes());
                if (!string.IsNullOrEmpty(notes))
                {
                    TxtPatchNotes.Text = notes;
                    TxtPatchNotes.IsVisible = true;
                }
                BtnApply.IsVisible = true;
            }
            else
            {
                LblStatus.Text = UpdateMessages.UpToDate;
            }
        }
        catch (Exception ex)
        {
            LblStatus.Text = UpdateMessages.Unreachable + " (" + ex.Message + ")";
        }
    }

    private async void ApplyUpdate()
    {
        bool ok = await MsgBox.Confirm(this, $"Update ke v{_newVersion}?");
        if (!ok) return;

        LblStatus.Text = UpdateMessages.Preparing;
        var prep = _updateService.PrepareUpdate();
        if (!prep.Success)
        {
            LblStatus.Text = prep.Error;
            return;
        }

        if (!_updateService.WalCheckpoint())
        {
            LblStatus.Text = UpdateMessages.WalCheckpointFailed;
            return;
        }

        DbConnection.CloseConnection();
        LblStatus.Text = UpdateMessages.InProgress;
        _updateService.ApplyUpdate();
    }

    private async void BtnImportZip_Click(object? sender, RoutedEventArgs e)
    {
        var files = await this.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import Update ZIP",
            FileTypeFilter = new[]
            {
                new FilePickerFileType("ZIP") { Patterns = new[] { "*.zip" } }
            }
        });

        if (files.Count == 0) return;

        try
        {
            LblStatus.Text = "Importing...";
            _updateService.PublishToShare(files[0].Path.LocalPath);
            LblStatus.Text = UpdateMessages.ZipImported;
        }
        catch (Exception ex)
        {
            LblStatus.Text = ex.Message;
        }
    }

    private void SetStatus(string text)
    {
        StatusLabel.Text = text;
    }
}
