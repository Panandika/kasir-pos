using System;
using System.Diagnostics;
using System.IO.Ports;
using System.Threading;

namespace Kasir.Hardware
{
    public class SerialRawPrinter : IRawPrinter
    {
        private const int WriteTimeoutMs = 10000;

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

            // Written synchronously and drained before closing: ESCPOS_NET's SerialPrinter
            // queued bytes on a background task that Dispose could cut off mid-receipt.
            try
            {
                using (var port = new SerialPort(_port, _baud) { WriteTimeout = WriteTimeoutMs })
                {
                    port.Open();
                    port.Write(data, 0, data.Length);
                    var sw = Stopwatch.StartNew();
                    while (port.BytesToWrite > 0)
                    {
                        if (sw.ElapsedMilliseconds > WriteTimeoutMs)
                        {
                            LastError = $"Timeout: {port.BytesToWrite} byte belum terkirim (port='{_port}', baud={_baud})";
                            return false;
                        }
                        Thread.Sleep(10);
                    }
                }
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
