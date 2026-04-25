using System;
using ESCPOS_NET;

namespace Kasir.Hardware
{
    public class SerialRawPrinter : IRawPrinter
    {
        private readonly string _port;
        private readonly int _baud;

        public SerialRawPrinter(string port, int baud)
        {
            _port = port;
            _baud = baud;
        }

        public string LastError { get; private set; }

        public bool Send(byte[] data)
        {
            LastError = null;
            if (string.IsNullOrEmpty(_port)) { LastError = "Port serial kosong"; return false; }
            if (data == null || data.Length == 0) { LastError = "Data kosong"; return false; }

            try
            {
                using var printer = new SerialPrinter(_port, _baud);
                printer.Write(data);
                return true;
            }
            catch (Exception ex)
            {
                LastError = $"{ex.GetType().Name}: {ex.Message} (port='{_port}', baud={_baud})";
                return false;
            }
        }
    }
}
