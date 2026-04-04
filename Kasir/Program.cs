using System;
using System.Windows.Forms;
using Kasir.Data;

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

            // Stub: show message until LoginForm is implemented in Chunk D
            MessageBox.Show(
                "Kasir POS initialized successfully.\n\n" +
                "Database: data\\kasir.db\n" +
                "LoginForm will be added in Chunk D.",
                "Kasir - Phase 1",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
    }
}
