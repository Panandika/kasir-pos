using System;
using System.IO;
using System.Windows.Forms;
using Kasir.Data;
using Kasir.Services;
using Kasir.Utils;

namespace Kasir
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            try
            {
                DbConnection.InitializeDatabase();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Database initialization failed:\n\n" + ex.Message,
                    "Kasir - Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            // Check for failed update and auto-recover
            CheckUpdateRecovery();

            // Check for successful update marker
            CheckUpdateComplete();

            Application.Run(new Forms.LoginForm());
        }

        private static void CheckUpdateRecovery()
        {
            try
            {
                string stateFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "update-state.txt");
                if (!File.Exists(stateFile))
                    return;

                string state = File.ReadAllText(stateFile).Trim();

                if (state == "COPY_IN_PROGRESS" || state == "BACKUP_COMPLETE")
                {
                    var svc = new UpdateService(DbConnection.GetConnection());
                    svc.RecoverFromFailedUpdate();

                    MessageBox.Show(
                        UpdateMessages.RolledBack,
                        "Kasir - Update",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
                else if (state == "COPY_COMPLETE" || state == "ROLLED_BACK")
                {
                    // Clean up state file
                    try { File.Delete(stateFile); }
                    catch { }
                }
            }
            catch
            {
                // Don't block startup for recovery issues
            }
        }

        private static void CheckUpdateComplete()
        {
            try
            {
                string basePath = AppDomain.CurrentDomain.BaseDirectory;
                string markerFile = Path.Combine(basePath, "update-complete.marker");

                if (!File.Exists(markerFile))
                    return;

                string newVersion = File.ReadAllText(markerFile).Trim();

                // Read patch notes if available
                string notesFile = Path.Combine(basePath, "whatsnew.txt");
                if (File.Exists(notesFile))
                {
                    AppVersion.PatchNotes = File.ReadAllText(notesFile);
                }
                AppVersion.JustUpdated = true;

                // Clean up
                try { File.Delete(markerFile); } catch { }
                try { File.Delete(Path.Combine(basePath, "update-state.txt")); } catch { }
                try { File.Delete(Path.Combine(basePath, "updater.log")); } catch { }
            }
            catch
            {
                // Don't block startup
            }
        }
    }
}
