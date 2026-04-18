using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Kasir.Data;

namespace Kasir.Avalonia.Forms.Admin;

public enum FirstRunChoice { None, Seed, Import }

public partial class FirstRunWindow : Window
{
    public FirstRunChoice Choice { get; private set; }
    public string? ImportPath { get; private set; }

    public FirstRunWindow()
    {
        Choice = FirstRunChoice.None;
        InitializeComponent();

        BtnSeed.Click   += (_, _) => { Choice = FirstRunChoice.Seed; Close(); };
        BtnCancel.Click += (_, _) => { Choice = FirstRunChoice.None; Close(); };
        BtnImport.Click += (_, _) => OnImport();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Choice = FirstRunChoice.None;
            Close();
        }
    }

    private async void OnImport()
    {
        var files = await this.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Pilih database SQLite (kasir.db)",
            FileTypeFilter = new[]
            {
                new FilePickerFileType("SQLite database (*.db)") { Patterns = new[] { "*.db" } },
                new FilePickerFileType("Semua file") { Patterns = new[] { "*" } }
            }
        });

        if (files.Count == 0) return;

        LblStatus.Text = "Memvalidasi database...";
        LblStatus.Foreground = Brush.Parse("#008800");

        var validation = DatabaseValidator.Validate(files[0].Path.LocalPath, runIntegrityCheck: true);
        if (!validation.IsValid)
        {
            LblStatus.Foreground = Brush.Parse("#ff5050");
            LblStatus.Text = "Database tidak valid:\n - " + string.Join("\n - ", validation.Errors);
            return;
        }

        ImportPath = files[0].Path.LocalPath;
        Choice = FirstRunChoice.Import;
        Close();
    }
}
