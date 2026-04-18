using System;
using System.Drawing;
using System.Windows.Forms;
using Kasir.Data;

namespace Kasir.Forms.Admin
{
    public enum FirstRunChoice
    {
        None,
        Seed,
        Import
    }

    public class FirstRunDialog : Form
    {
        public FirstRunChoice Choice { get; private set; }
        public string ImportPath { get; private set; }

        private Label lblStatus;
        private Button btnSeed;
        private Button btnImport;
        private Button btnCancel;

        public FirstRunDialog()
        {
            Choice = FirstRunChoice.None;
            ImportPath = null;
            InitializeLayout();
        }

        private void InitializeLayout()
        {
            this.Text = "RASIO / YONICO POS — First Run";
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.ClientSize = new Size(560, 360);
            this.BackColor = ThemeConstants.BgDialog;
            this.ForeColor = ThemeConstants.FgPrimary;
            this.Font = ThemeConstants.FontMain;
            this.KeyPreview = true;

            var lblTitle = new Label
            {
                Text = "Database belum ditemukan.",
                Font = ThemeConstants.FontTitle,
                ForeColor = ThemeConstants.FgPrimary,
                AutoSize = false,
                Size = new Size(520, 40),
                Location = new Point(20, 20)
            };

            var lblPrompt = new Label
            {
                Text = "Pilih cara memulai:",
                Font = ThemeConstants.FontMain,
                ForeColor = ThemeConstants.FgDimmed,
                AutoSize = false,
                Size = new Size(520, 28),
                Location = new Point(20, 70)
            };

            btnSeed = MakeButton(
                "&1. Gunakan data contoh (Seed)",
                new Point(20, 110),
                ThemeConstants.BtnPrimary);
            btnSeed.Click += (s, e) => OnSeed();

            btnImport = MakeButton(
                "&2. Impor database yang sudah ada...",
                new Point(20, 160),
                ThemeConstants.BtnSecondary);
            btnImport.Click += (s, e) => OnImport();

            btnCancel = MakeButton(
                "&3. Batal (keluar)",
                new Point(20, 210),
                ThemeConstants.BtnDanger);
            btnCancel.Click += (s, e) => OnCancel();

            lblStatus = new Label
            {
                Text = string.Empty,
                Font = ThemeConstants.FontSmall,
                ForeColor = ThemeConstants.FgError,
                AutoSize = false,
                Size = new Size(520, 80),
                Location = new Point(20, 270)
            };

            this.Controls.Add(lblTitle);
            this.Controls.Add(lblPrompt);
            this.Controls.Add(btnSeed);
            this.Controls.Add(btnImport);
            this.Controls.Add(btnCancel);
            this.Controls.Add(lblStatus);

            this.AcceptButton = btnSeed;
            this.CancelButton = btnCancel;
        }

        private Button MakeButton(string text, Point location, Color bg)
        {
            var btn = new Button
            {
                Text = text,
                Size = new Size(520, 40),
                Location = location,
                BackColor = bg,
                ForeColor = ThemeConstants.FgWhite,
                Font = ThemeConstants.FontMain,
                FlatStyle = FlatStyle.Flat,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(16, 0, 0, 0),
                UseVisualStyleBackColor = false
            };
            btn.FlatAppearance.BorderSize = 1;
            btn.FlatAppearance.BorderColor = ThemeConstants.GridLine;
            return btn;
        }

        private void OnSeed()
        {
            Choice = FirstRunChoice.Seed;
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private void OnImport()
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Title = "Pilih database SQLite (kasir.db)";
                ofd.Filter = "SQLite database (*.db)|*.db|All files (*.*)|*.*";
                ofd.CheckFileExists = true;
                if (ofd.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                lblStatus.ForeColor = ThemeConstants.FgDimmed;
                lblStatus.Text = "Memvalidasi database...";
                Application.DoEvents();

                var validation = DatabaseValidator.Validate(ofd.FileName, runIntegrityCheck: true);
                if (!validation.IsValid)
                {
                    lblStatus.ForeColor = ThemeConstants.FgError;
                    lblStatus.Text = "Database tidak valid:\n - " +
                        string.Join("\n - ", validation.Errors);
                    return;
                }

                ImportPath = ofd.FileName;
                Choice = FirstRunChoice.Import;
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
        }

        private void OnCancel()
        {
            Choice = FirstRunChoice.None;
            this.DialogResult = DialogResult.Cancel;
            this.Close();
        }
    }
}
