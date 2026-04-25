using System;

namespace Kasir.Hardware
{
    /// <summary>
    /// Picks the right IRawPrinter based on configured kind.
    /// kind values: "windows", "serial", "device_file", or empty (legacy fallback).
    /// </summary>
    public static class RawPrinterFactory
    {
        public const int DefaultBaud = 9600;

        public static IRawPrinter Create(string kind, string name, int baud = 0)
        {
            if (string.IsNullOrEmpty(name)) return new NullRawPrinter();

            switch ((kind ?? "").Trim().ToLowerInvariant())
            {
                case "windows":     return new WindowsSpoolPrinter(name);
                case "serial":      return new SerialRawPrinter(name, baud > 0 ? baud : DefaultBaud);
                case "device_file": return new FileRawPrinter(name);
                default:            return InferFromName(name);
            }
        }

        // Backward compat for installs where printer_kind was never set. Routes by name shape:
        //   LPT* or /dev/usb*       → device file
        //   matches a Windows queue → Windows spool (silent migration on Windows)
        //   anything else           → legacy serial-at-115200 fallback
        // The Windows-queue lookup makes the original "EPSON TM-T82 won't print" bug
        // self-heal on first launch after upgrade — user doesn't have to revisit Config.
        private static IRawPrinter InferFromName(string name)
        {
            if (name.StartsWith("/dev/usb", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))
            {
                return new FileRawPrinter(name);
            }

            if (OperatingSystem.IsWindows() && IsInstalledWindowsPrinter(name))
            {
                return new WindowsSpoolPrinter(name);
            }

            return new SerialRawPrinter(name, 115200);
        }

        private static bool IsInstalledWindowsPrinter(string name)
        {
            try
            {
                foreach (var p in PrinterDiscovery.EnumerateWindowsPrinters())
                {
                    if (string.Equals(p, name, StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            catch { /* swallow — fall through to serial */ }
            return false;
        }
    }
}
