using System;
using System.IO;

namespace Kasir.Hardware
{
    public class FileRawPrinter : IRawPrinter
    {
        private readonly string _path;

        public FileRawPrinter(string path)
        {
            _path = path;
        }

        public string LastError { get; private set; }

        // Device node existence only, nothing written. Windows LPTn/COMn device names are
        // not files and cannot be checked without opening the port, so they count as present.
        public bool IsReachable()
        {
            LastError = null;
            if (string.IsNullOrEmpty(_path)) { LastError = "Path device kosong"; return false; }
            if (File.Exists(_path)) return true;
            if (OperatingSystem.IsWindows() && IsDosDeviceName(_path)) return true;
            LastError = $"Device '{_path}' tidak ditemukan";
            return false;
        }

        private static bool IsDosDeviceName(string path)
        {
            string name = path.Trim().TrimEnd(':');
            if (name.StartsWith(@"\\.\", StringComparison.Ordinal)) name = name.Substring(4);
            if (string.Equals(name, "PRN", StringComparison.OrdinalIgnoreCase)) return true;
            return name.Length == 4
                && (name.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
                && char.IsDigit(name[3]);
        }

        public bool Send(byte[] data)
        {
            LastError = null;
            if (string.IsNullOrEmpty(_path)) { LastError = "Path device kosong"; return false; }
            if (data == null || data.Length == 0) { LastError = "Data kosong"; return false; }

            // Written synchronously: ESCPOS_NET's FilePrinter queued bytes on a background
            // task and dropped them on Dispose, so a receipt could report success yet never
            // print. FileMode.Open so a wrong path fails instead of creating a plain file.
            try
            {
                using (var stream = new FileStream(_path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
                {
                    stream.Write(data, 0, data.Length);
                    stream.Flush();
                }
                return true;
            }
            catch (Exception ex)
            {
                LastError = $"{ex.GetType().Name}: {ex.Message} (path='{_path}')";
                return false;
            }
        }
    }
}
