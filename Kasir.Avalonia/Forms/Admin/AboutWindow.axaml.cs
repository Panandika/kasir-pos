using Avalonia.Controls;
using Avalonia.Input;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Utils;

namespace Kasir.Avalonia.Forms.Admin;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();

        var db = DbConnection.GetConnection();
        var config = new ConfigRepository(db);

        string registerId = config.Get("register_id") ?? "-";
        string schemaVersion = config.Get("schema_version") ?? "-";
        string syncRole = config.Get("sync_role") ?? "-";
        string lastUpdateCheck = config.Get("last_update_check") ?? "-";
        string version = AppVersion.Current;

        InfoText.Text =
            $"KASIR POS\n" +
            $"Versi: {version}\n\n" +
            $"Register ID : {registerId}\n" +
            $"Schema DB   : {schemaVersion}\n" +
            $"Peran Sinkron: {syncRole}\n" +
            $"Cek Update   : {lastUpdateCheck}\n\n" +
            $"© Sinar Makmur";

        StatusLabel.Text = $"Tentang — Kasir v{version}";
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (KeyboardRouter.IsEscape(e))
        {
            e.Handled = true;
            Close();
        }
    }
}
