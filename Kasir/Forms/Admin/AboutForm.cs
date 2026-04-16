using System;
using System.Configuration;
using System.Drawing;
using System.Windows.Forms;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Utils;

namespace Kasir.Forms.Admin
{
    public class AboutForm : BaseForm
    {
        public AboutForm()
        {
            InitializeLayout();
        }

        private void InitializeLayout()
        {
            var configRepo = new ConfigRepository(DbConnection.GetConnection());

            string registerId = configRepo.Get("register_id") ?? "??";
            string schemaVersion = configRepo.Get("schema_version") ?? "1";
            string syncRole = configRepo.Get("sync_role") ?? "hub";
            string lastUpdateCheck = configRepo.Get("last_update_check") ?? "-";

            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(40)
            };

            var info = new Label
            {
                Text = string.Format(
                    "KASIR POS\n" +
                    "=========\n\n" +
                    "Versi          : {0}\n" +
                    "Register ID    : {1}\n" +
                    "Sync Role      : {2}\n" +
                    "Schema Version : {3}\n" +
                    "Update Terakhir: {4}\n\n" +
                    "Esc = Kembali",
                    AppVersion.Current,
                    registerId,
                    syncRole,
                    schemaVersion,
                    lastUpdateCheck),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = ThemeConstants.FgDimmed,
                Font = ThemeConstants.FontMain
            };

            panel.Controls.Add(info);
            this.Controls.Add(panel);

            SetAction("Tentang — Kasir v" + AppVersion.Current);
        }
    }
}
