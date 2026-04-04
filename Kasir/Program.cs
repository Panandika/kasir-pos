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

            Application.Run(new Forms.LoginForm());
        }
    }
}
