using System.Collections.Generic;
using System.Linq;
using Kasir.Data.Repositories;

namespace Kasir.Hardware
{
    public class ReceiptPrinter : IReceiptPrinter
    {
        private readonly string _printerName;

        public ReceiptPrinter(string printerName)
        {
            _printerName = printerName;
        }

        public ReceiptPrinter(ConfigRepository config)
        {
            _printerName = config.Get("printer_name") ?? "LPT1:";
        }

        public bool Print(byte[] escPosData)
        {
            if (string.IsNullOrEmpty(_printerName))
            {
                return false;
            }

            return RawPrinterHelper.SendBytesToPrinter(_printerName, escPosData);
        }

        public bool IsAvailable()
        {
            if (string.IsNullOrEmpty(_printerName))
            {
                return false;
            }

            // Send init command to test connectivity
            return RawPrinterHelper.SendBytesToPrinter(_printerName, EscPosCommands.Init);
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
            receipt.Add(EscPosCommands.Text("Printer: " + _printerName + "\n"));
            receipt.Add(EscPosCommands.Text("================================\n"));
            receipt.Add(EscPosCommands.Text("\n\n\n"));
            receipt.Add(EscPosCommands.PartialCut);

            byte[] allBytes = receipt.SelectMany(b => b).ToArray();
            return Print(allBytes);
        }
    }
}
