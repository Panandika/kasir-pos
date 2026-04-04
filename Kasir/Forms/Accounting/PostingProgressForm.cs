using System;
using System.Drawing;
using System.Windows.Forms;
using Kasir.Data;
using Kasir.Services;

namespace Kasir.Forms.Accounting
{
    public class PostingProgressForm : BaseForm
    {
        private TextBox txtLog;
        private PostingService _postingService;
        private string _periodCode;

        public PostingProgressForm()
        {
            var conn = DbConnection.GetConnection();
            _postingService = new PostingService(conn);
            _periodCode = DateTime.Now.ToString("yyyyMM");
            InitializeLayout();
            SetAction("Proses Posting — F1: Post POS, F2: Post Pembelian, F3: Post Kas, F5: Tutup Periode, F10: Cek Saldo, Esc: Keluar");
        }

        private void InitializeLayout()
        {
            txtLog = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Color.Black,
                ForeColor = Color.FromArgb(0, 255, 0),
                Font = new Font("Consolas", 12f)
            };

            this.Controls.Add(txtLog);
        }

        private void Log(string message)
        {
            txtLog.AppendText(DateTime.Now.ToString("HH:mm:ss") + " " + message + "\r\n");
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.F1:
                    RunPostSales();
                    return true;
                case Keys.F2:
                    RunPostPurchases();
                    return true;
                case Keys.F3:
                    RunPostCash();
                    return true;
                case Keys.F5:
                    RunClosePeriod();
                    return true;
                case Keys.F10:
                    RunBalanceCheck();
                    return true;
                case Keys.Escape:
                    this.Close();
                    return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void RunPostSales()
        {
            try
            {
                Log("Posting penjualan periode " + _periodCode + "...");
                var result = _postingService.PostSales(_periodCode);
                Log(string.Format("Selesai: {0} diposting, {1} error", result.PostedCount, result.ErrorCount));
                foreach (var err in result.Errors) Log("  ERROR: " + err);
            }
            catch (Exception ex) { Log("GAGAL: " + ex.Message); }
        }

        private void RunPostPurchases()
        {
            try
            {
                Log("Posting pembelian periode " + _periodCode + "...");
                var result = _postingService.PostPurchases(_periodCode);
                Log(string.Format("Selesai: {0} diposting, {1} error", result.PostedCount, result.ErrorCount));

                Log("Posting retur periode " + _periodCode + "...");
                var retResult = _postingService.PostReturns(_periodCode);
                Log(string.Format("Selesai: {0} diposting, {1} error", retResult.PostedCount, retResult.ErrorCount));
            }
            catch (Exception ex) { Log("GAGAL: " + ex.Message); }
        }

        private void RunPostCash()
        {
            try
            {
                Log("Posting transaksi kas/bank periode " + _periodCode + "...");
                var result = _postingService.PostCashTransactions(_periodCode);
                Log(string.Format("Selesai: {0} diposting, {1} error", result.PostedCount, result.ErrorCount));
            }
            catch (Exception ex) { Log("GAGAL: " + ex.Message); }
        }

        private void RunClosePeriod()
        {
            var confirm = MessageBox.Show(
                "Tutup periode " + _periodCode + "? Proses ini tidak dapat dibatalkan.",
                "Konfirmasi", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

            if (confirm != DialogResult.Yes) return;

            try
            {
                Log("Menutup periode " + _periodCode + "...");
                _postingService.ClosePeriod(_periodCode);
                Log("Periode " + _periodCode + " berhasil ditutup.");
            }
            catch (Exception ex) { Log("GAGAL: " + ex.Message); }
        }

        private void RunBalanceCheck()
        {
            try
            {
                Log("Cek saldo periode " + _periodCode + "...");
                var result = _postingService.CheckBalance(_periodCode);

                if (result.IsBalanced)
                {
                    Log("SALDO BALANCE — Debit = Kredit = " + result.TotalDebits);
                }
                else
                {
                    Log(string.Format("TIDAK BALANCE — Debit: {0}, Kredit: {1}, Selisih: {2}",
                        result.TotalDebits, result.TotalCredits, result.Difference));
                    foreach (var acc in result.DiscrepancyAccounts)
                        Log("  " + acc);
                }
            }
            catch (Exception ex) { Log("GAGAL: " + ex.Message); }
        }
    }
}
