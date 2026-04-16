using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Kasir.Forms.Admin
{
    public class BackupForm : BaseForm
    {
        public BackupForm()
        {
            InitializeLayout();
            SetAction("Database Backup");
        }

        private void InitializeLayout()
        {
            var pnl = new Panel
            {
                Size = new Size(500, 200),
                BackColor = ThemeConstants.BgDialog
            };

            var lblTitle = new Label
            {
                Text = "Backup Database",
                Font = ThemeConstants.FontTitle,
                ForeColor = ThemeConstants.FgPrimary,
                Location = new Point(15, 15),
                AutoSize = true
            };

            var lblInfo = new Label
            {
                Text = "Copies kasir.db to selected folder with timestamp.\nRecommended: backup to USB drive daily.",
                ForeColor = ThemeConstants.FgLabel,
                Location = new Point(15, 55),
                AutoSize = true
            };

            var btnBackup = new Button
            {
                Text = "Backup Now",
                Location = new Point(15, 110),
                Size = new Size(200, 40),
                ForeColor = ThemeConstants.FgWhite,
                BackColor = ThemeConstants.BtnPrimary,
                FlatStyle = FlatStyle.Flat,
                Font = ThemeConstants.FontInputSmall
            };
            btnBackup.Click += BtnBackup_Click;

            pnl.Controls.AddRange(new Control[] { lblTitle, lblInfo, btnBackup });

            this.Load += (s, e) =>
            {
                pnl.Location = new Point(
                    (this.ClientSize.Width - pnl.Width) / 2,
                    (this.ClientSize.Height - pnl.Height) / 2);
            };

            this.Controls.Add(pnl);
        }

        private void BtnBackup_Click(object sender, EventArgs e)
        {
            string sourcePath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "data", "kasir.db");

            if (!File.Exists(sourcePath))
            {
                MessageBox.Show("Database file not found: " + sourcePath, "Error");
                return;
            }

            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "Select backup destination folder";

                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    string destName = string.Format("kasir_{0}.db", timestamp);
                    string destPath = Path.Combine(dlg.SelectedPath, destName);

                    try
                    {
                        File.Copy(sourcePath, destPath, false);
                        MessageBox.Show(
                            string.Format("Backup saved to:\n{0}", destPath),
                            "Backup Complete",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(
                            "Backup failed: " + ex.Message,
                            "Error",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                    }
                }
            }
        }
    }
}
