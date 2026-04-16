using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using Kasir.Data;
using Kasir.Data.Repositories;
using Kasir.Services;
using Kasir.Utils;

namespace Kasir.Forms.Admin
{
    public class UpdateForm : BaseForm
    {
        private Label lblCurrentVersion;
        private Label lblStatus;
        private TextBox txtPatchNotes;
        private Label lblKeys;
        private UpdateService _updateService;
        private bool _updateAvailable;
        private string _newVersion;

        public UpdateForm()
        {
            _updateService = new UpdateService(DbConnection.GetConnection());
            InitializeLayout();
        }

        private void InitializeLayout()
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(40, 20, 40, 10)
            };

            var title = new Label
            {
                Text = "PERIKSA UPDATE",
                Dock = DockStyle.Top,
                Height = 40,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = ThemeConstants.FgPrimary,
                Font = ThemeConstants.FontTitle
            };

            lblCurrentVersion = new Label
            {
                Text = string.Format(UpdateMessages.CurrentVersion, AppVersion.Current),
                Dock = DockStyle.Top,
                Height = 30,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = ThemeConstants.FgDimmed,
                Font = ThemeConstants.FontInputSmall
            };

            lblStatus = new Label
            {
                Text = "",
                Dock = DockStyle.Top,
                Height = 30,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = ThemeConstants.FgWarning,
                Font = ThemeConstants.FontInputSmall
            };

            txtPatchNotes = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                Dock = DockStyle.Fill,
                BackColor = ThemeConstants.BgPanel,
                ForeColor = ThemeConstants.FgDimmed,
                Font = ThemeConstants.FontGrid,
                ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.FixedSingle,
                Visible = false
            };

            lblKeys = new Label
            {
                Text = "F5=Periksa  Esc=Tutup",
                Dock = DockStyle.Bottom,
                Height = 30,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = ThemeConstants.FgWhite,
                BackColor = ThemeConstants.BgHeader,
                Font = ThemeConstants.FontSmall
            };

            // Hub-only: Import ZIP button
            var configRepo = new ConfigRepository(DbConnection.GetConnection());
            string syncRole = configRepo.Get("sync_role") ?? "hub";

            if (syncRole == "hub")
            {
                var btnImport = new Button
                {
                    Text = "Import ZIP (Hub)",
                    Dock = DockStyle.Bottom,
                    Height = 35,
                    FlatStyle = FlatStyle.Flat,
                    BackColor = ThemeConstants.BgHeader,
                    ForeColor = ThemeConstants.FgWhite,
                    Font = ThemeConstants.FontSmall
                };
                btnImport.Click += OnImportZipClick;
                panel.Controls.Add(btnImport);
            }

            panel.Controls.Add(txtPatchNotes);
            panel.Controls.Add(lblStatus);
            panel.Controls.Add(lblCurrentVersion);
            panel.Controls.Add(title);
            this.Controls.Add(panel);
            this.Controls.Add(lblKeys);

            SetAction(string.Format("Periksa Update — Kasir v{0}", AppVersion.Current));
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.F5:
                    CheckForUpdate();
                    return true;
                case Keys.F8:
                    if (_updateAvailable)
                    {
                        ApplyUpdate();
                    }
                    return true;
                case Keys.Escape:
                    this.Close();
                    return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private async void CheckForUpdate()
        {
            lblStatus.Text = UpdateMessages.Checking;
            lblStatus.ForeColor = ThemeConstants.FgWarning;
            txtPatchNotes.Visible = false;
            _updateAvailable = false;

            try
            {
                var result = await _updateService.CheckForUpdateAsync();

                if (!string.IsNullOrEmpty(result.Error))
                {
                    lblStatus.Text = result.Error;
                    lblStatus.ForeColor = ThemeConstants.FgError;
                    return;
                }

                if (result.Available)
                {
                    _updateAvailable = true;
                    _newVersion = result.NewVersion;
                    lblStatus.Text = string.Format(UpdateMessages.Available, result.NewVersion);
                    lblStatus.ForeColor = ThemeConstants.FgPrimary;

                    // Load patch notes (async to avoid UI freeze on slow UNC)
                    string notes = await Task.Run(() => _updateService.GetPatchNotes());
                    if (!string.IsNullOrEmpty(notes))
                    {
                        txtPatchNotes.Text = notes;
                        txtPatchNotes.Visible = true;
                    }

                    lblKeys.Text = "F5=Periksa  F8=Update Sekarang  Esc=Tutup";
                }
                else
                {
                    lblStatus.Text = UpdateMessages.UpToDate;
                    lblStatus.ForeColor = ThemeConstants.FgDimmed;
                }
            }
            catch (Exception ex)
            {
                lblStatus.Text = UpdateMessages.Unreachable + " (" + ex.Message + ")";
                lblStatus.ForeColor = ThemeConstants.FgError;
            }
        }

        private void ApplyUpdate()
        {
            var confirm = MessageBox.Show(
                string.Format(UpdateMessages.Confirm, _newVersion),
                "Konfirmasi Update",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (confirm != DialogResult.Yes)
                return;

            lblStatus.Text = UpdateMessages.Preparing;
            lblStatus.ForeColor = ThemeConstants.FgWarning;
            Application.DoEvents();

            // Prepare: copy files to staging, verify checksums
            var prepResult = _updateService.PrepareUpdate();
            if (!prepResult.Success)
            {
                lblStatus.Text = prepResult.Error;
                lblStatus.ForeColor = ThemeConstants.FgError;
                return;
            }

            // WAL checkpoint before update
            if (!_updateService.WalCheckpoint())
            {
                lblStatus.Text = UpdateMessages.WalCheckpointFailed;
                lblStatus.ForeColor = ThemeConstants.FgError;
                return;
            }

            // Close database connection
            DbConnection.CloseConnection();

            // Launch updater and exit
            lblStatus.Text = UpdateMessages.InProgress;
            Application.DoEvents();
            _updateService.ApplyUpdate();
        }

        private void OnImportZipClick(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Filter = "ZIP files (*.zip)|*.zip";
                dlg.Title = "Import Update ZIP";

                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        lblStatus.Text = "Importing...";
                        lblStatus.ForeColor = ThemeConstants.FgWarning;
                        Application.DoEvents();

                        _updateService.PublishToShare(dlg.FileName);

                        lblStatus.Text = UpdateMessages.ZipImported;
                        lblStatus.ForeColor = ThemeConstants.FgPrimary;
                    }
                    catch (Exception ex)
                    {
                        lblStatus.Text = ex.Message;
                        lblStatus.ForeColor = ThemeConstants.FgError;
                    }
                }
            }
        }
    }
}
