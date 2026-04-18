using System;
using ESCPOS_NET;

namespace Kasir.Hardware
{
    public class UsbReceiptPrinter : IRawPrinter
    {
        private readonly string _portOrPath;

        public UsbReceiptPrinter(string portOrPath)
        {
            _portOrPath = portOrPath;
        }

        public bool Send(byte[] data)
        {
            if (string.IsNullOrEmpty(_portOrPath) || data == null || data.Length == 0)
                return false;

            try
            {
                using var printer = CreatePrinter();
                printer.Write(data);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private BasePrinter CreatePrinter()
        {
            // /dev/usb/lp0, LPT1: — raw device files
            if (_portOrPath.StartsWith("/dev/usb", StringComparison.OrdinalIgnoreCase)
                || _portOrPath.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))
            {
                return new FilePrinter(_portOrPath);
            }

            // COM4, /dev/ttyUSB0, /dev/cu.usbserial-* — serial ports
            return new SerialPrinter(_portOrPath, 115200);
        }
    }
}
