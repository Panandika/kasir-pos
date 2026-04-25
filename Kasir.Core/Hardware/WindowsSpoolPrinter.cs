using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Kasir.Hardware
{
    /// <summary>
    /// Sends raw ESC/POS bytes to a Windows print queue via winspool.drv RAW datatype.
    /// Bypasses GDI; the driver passes bytes through to the printer firmware untouched.
    /// </summary>
    public class WindowsSpoolPrinter : IRawPrinter
    {
        private readonly string _printerName;

        public WindowsSpoolPrinter(string printerName)
        {
            _printerName = printerName;
        }

        public string LastError { get; private set; }

        public bool Send(byte[] data)
        {
            LastError = null;
            if (string.IsNullOrEmpty(_printerName)) { LastError = "Nama printer kosong"; return false; }
            if (data == null || data.Length == 0) { LastError = "Data kosong"; return false; }

            if (!OperatingSystem.IsWindows())
            {
                LastError = "Windows spooler tidak tersedia di OS ini";
                return false;
            }

            return SendWindows(data);
        }

        [SupportedOSPlatform("windows")]
        private bool SendWindows(byte[] data)
        {
            IntPtr hPrinter;
            if (!OpenPrinter(_printerName, out hPrinter, IntPtr.Zero))
            {
                LastError = $"OpenPrinter gagal (Win32 error {Marshal.GetLastWin32Error()}, printer='{_printerName}')";
                return false;
            }

            bool docStarted = false;
            bool pageStarted = false;
            try
            {
                var docInfo = new DOCINFOA
                {
                    pDocName = "Kasir Receipt",
                    pOutputFile = null,
                    pDataType = "RAW"
                };

                docStarted = StartDocPrinter(hPrinter, 1, ref docInfo);
                if (!docStarted)
                {
                    LastError = $"StartDocPrinter gagal (Win32 error {Marshal.GetLastWin32Error()})";
                    return false;
                }

                pageStarted = StartPagePrinter(hPrinter);
                if (!pageStarted)
                {
                    LastError = $"StartPagePrinter gagal (Win32 error {Marshal.GetLastWin32Error()})";
                    return false;
                }

                bool ok = WritePrinter(hPrinter, data, data.Length, out int written);
                if (!ok || written != data.Length)
                {
                    LastError = $"WritePrinter incomplete (wrote {written}/{data.Length}, Win32 error {Marshal.GetLastWin32Error()})";
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                LastError = $"{ex.GetType().Name}: {ex.Message} (printer='{_printerName}')";
                return false;
            }
            finally
            {
                // Only call End* for Start* calls that actually succeeded — Win32 contract.
                if (pageStarted) EndPagePrinter(hPrinter);
                if (docStarted) EndDocPrinter(hPrinter);
                ClosePrinter(hPrinter);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DOCINFOA
        {
            [MarshalAs(UnmanagedType.LPStr)] public string pDocName;
            [MarshalAs(UnmanagedType.LPStr)] public string pOutputFile;
            [MarshalAs(UnmanagedType.LPStr)] public string pDataType;
        }

        [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Ansi)]
        private static extern bool OpenPrinter(string pPrinterName, out IntPtr phPrinter, IntPtr pDefault);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool ClosePrinter(IntPtr hPrinter);

        [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Ansi)]
        private static extern bool StartDocPrinter(IntPtr hPrinter, int level, ref DOCINFOA pDocInfo);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool EndDocPrinter(IntPtr hPrinter);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool StartPagePrinter(IntPtr hPrinter);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool EndPagePrinter(IntPtr hPrinter);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool WritePrinter(IntPtr hPrinter, byte[] pBytes, int dwCount, out int dwWritten);
    }
}
