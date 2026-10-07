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

        // Port existence only: opening it would take exclusive access away from a print.
        public bool IsReachable()
        {
            LastError = null;
            if (string.IsNullOrEmpty(_port)) { LastError = "Port serial kosong"; return false; }
            foreach (var p in PrinterDiscovery.EnumerateSerialPorts())
            {
                if (string.Equals(p, _port, StringComparison.OrdinalIgnoreCase)) return true;
            }
            LastError = $"Port serial '{_port}' tidak ditemukan";
            return false;
        }

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
