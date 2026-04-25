using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Runtime.Versioning;

namespace Kasir.Hardware
{
    public static class PrinterDiscovery
    {
        public static IReadOnlyList<string> EnumerateWindowsPrinters()
        {
            if (!OperatingSystem.IsWindows()) return System.Array.Empty<string>();
            return EnumerateWindowsPrintersImpl();
        }

        [SupportedOSPlatform("windows")]
        private static IReadOnlyList<string> EnumerateWindowsPrintersImpl()
        {
            var list = new List<string>();
            foreach (string name in System.Drawing.Printing.PrinterSettings.InstalledPrinters)
            {
                list.Add(name);
            }
            return list;
        }

        public static IReadOnlyList<string> EnumerateSerialPorts()
        {
            try { return SerialPort.GetPortNames(); }
            catch { return System.Array.Empty<string>(); }
        }

        public static int[] CommonBaudRates => new[] { 4800, 9600, 19200, 38400, 57600, 115200 };

        /// <summary>
        /// Queries WMI for printer status. Returns null on non-Windows or on error.
        /// Strings: "ready", "paused", "offline", "error", or descriptive WMI status.
        /// </summary>
        public static string GetWindowsPrinterStatus(string printerName)
        {
            if (!OperatingSystem.IsWindows()) return null;
            if (string.IsNullOrEmpty(printerName)) return null;
            return GetWindowsPrinterStatusImpl(printerName);
        }

        [SupportedOSPlatform("windows")]
        private static string GetWindowsPrinterStatusImpl(string printerName)
        {
            try
            {
                using var searcher = new System.Management.ManagementObjectSearcher(
                    $"SELECT WorkOffline, PrinterStatus, PrinterState FROM Win32_Printer WHERE Name = '{printerName.Replace("'", "''").Replace("\\", "\\\\")}'");
                foreach (System.Management.ManagementObject mo in searcher.Get())
                {
                    bool offline = mo["WorkOffline"] is bool b && b;
                    if (offline) return "offline";

                    // PrinterState is a bitmask; bit 0 = paused
                    if (mo["PrinterState"] is uint state && (state & 0x1) != 0) return "paused";

                    if (mo["PrinterStatus"] is ushort status)
                    {
                        return status switch
                        {
                            1 => "other",
                            2 => "unknown",
                            3 => "ready",
                            4 => "printing",
                            5 => "warmup",
                            6 => "stopped_printing",
                            7 => "offline",
                            _ => "unknown",
                        };
                    }
                    return "ready";
                }
                return "not_found";
            }
            catch (Exception ex)
            {
                return "wmi_error: " + ex.Message;
            }
        }
    }
}
