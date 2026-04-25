using System;
using ESCPOS_NET;

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

        public bool Send(byte[] data)
        {
            LastError = null;
            if (string.IsNullOrEmpty(_path)) { LastError = "Path device kosong"; return false; }
            if (data == null || data.Length == 0) { LastError = "Data kosong"; return false; }

            try
            {
                using var printer = new FilePrinter(_path);
                printer.Write(data);
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
