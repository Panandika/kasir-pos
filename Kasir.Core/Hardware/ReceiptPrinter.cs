using System.Collections.Generic;
using System.Linq;
using Kasir.Data.Repositories;

namespace Kasir.Hardware
{
    public class ReceiptPrinter : IReceiptPrinter
    {
        private readonly IRawPrinter _raw;

        public ReceiptPrinter(IRawPrinter raw)
        {
            _raw = raw;
        }

        public ReceiptPrinter(string printerName)
            : this(RawPrinterFactory.Create("", printerName))
        {
        }

        public ReceiptPrinter(ConfigRepository config)
            : this(RawPrinterFactory.Create(
                config.Get("printer_kind") ?? "",
                config.Get("printer_name") ?? "",
                ParseBaud(config.Get("printer_baud"))))
        {
        }

        public string LastError => _raw.LastError;

        public bool Print(byte[] escPosData)
        {
            return _raw.Send(escPosData);
        }

        public bool IsAvailable()
        {
            return _raw.Send(EscPosCommands.Init);
        }

        public bool PrintTestReceipt(string storeName)
        {
            var receipt = new List<byte[]>();
            receipt.Add(EscPosCommands.Init);
            receipt.Add(EscPosCommands.CenterAlign);
            receipt.Add(EscPosCommands.BoldOn);
            receipt.Add(EscPosCommands.Text(storeName + "\n"));
            receipt.Add(EscPosCommands.BoldOff);
            receipt.Add(EscPosCommands.LeftAlign);
            receipt.Add(EscPosCommands.Text("================================\n"));
            receipt.Add(EscPosCommands.Text("TEST PRINT\n"));
            receipt.Add(EscPosCommands.Text("================================\n"));
            receipt.Add(EscPosCommands.Text("\n\n\n"));
            receipt.Add(EscPosCommands.PartialCut);

            byte[] allBytes = receipt.SelectMany(b => b).ToArray();
            return Print(allBytes);
        }

        private static int ParseBaud(string s)
        {
            return int.TryParse(s, out var b) && b > 0 ? b : RawPrinterFactory.DefaultBaud;
        }
    }
}
