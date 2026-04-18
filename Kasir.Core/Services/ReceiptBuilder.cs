using System.Collections.Generic;
using System.Linq;
using Kasir.Hardware;
using Kasir.Models;
using Kasir.Utils;

namespace Kasir.Services
{
    public class ReceiptBuilder
    {
        public byte[] BuildSaleReceipt(Sale sale, List<SaleItem> items, string storeName, string cashierAlias,
            string storeAddress = null, string storeTagline = null)
        {
            var receipt = new List<byte[]>();

            // Header
            receipt.Add(EscPosCommands.Init);
            receipt.Add(EscPosCommands.CenterAlign);
            receipt.Add(EscPosCommands.BoldOn);
            receipt.Add(EscPosCommands.Text(storeName + "\n"));
            receipt.Add(EscPosCommands.BoldOff);
            if (!string.IsNullOrEmpty(storeAddress))
            {
                receipt.Add(EscPosCommands.Text(storeAddress + "\n"));
            }
            if (!string.IsNullOrEmpty(storeTagline))
            {
                receipt.Add(EscPosCommands.Text(storeTagline + "\n"));
            }
            receipt.Add(EscPosCommands.LeftAlign);

            receipt.Add(EscPosCommands.Text(string.Format("Date: {0}\n",
                Formatting.FormatDate(sale.DocDate))));
            receipt.Add(EscPosCommands.Text(string.Format("No: {0}  Kasir: {1}\n",
                sale.JournalNo, cashierAlias ?? sale.Cashier)));
            receipt.Add(EscPosCommands.Text("================================\n"));

            // Items
            foreach (var item in items)
            {
                string name = item.ProductName ?? item.ProductCode ?? "";
                if (name.Length > 24) name = name.Substring(0, 24);

                string total = Formatting.FormatCurrencyShort(item.Value);
                receipt.Add(EscPosCommands.Text(
                    string.Format("{0,-24}{1,8}\n", name, total)));

                // Show qty x price if qty > 1
                if (item.Quantity > 1)
                {
                    string detail = string.Format("  {0} x {1}",
                        item.Quantity,
                        Formatting.FormatCurrencyShort(item.UnitPrice));
                    receipt.Add(EscPosCommands.Text(detail + "\n"));
                }

                // Show discount if any
                if (item.DiscValue > 0)
                {
                    string disc = string.Format("  Disc: -{0}",
                        Formatting.FormatCurrencyShort(item.DiscValue));
                    receipt.Add(EscPosCommands.Text(disc + "\n"));
                }
            }

            receipt.Add(EscPosCommands.Text("================================\n"));

            // Totals
            if (sale.TotalDisc > 0)
            {
                receipt.Add(EscPosCommands.Text(string.Format("{0,-16}{1,16}\n",
                    "SUBTOTAL:", Formatting.FormatCurrencyShort(sale.GrossAmount))));
                receipt.Add(EscPosCommands.Text(string.Format("{0,-16}{1,16}\n",
                    "DISC:", "-" + Formatting.FormatCurrencyShort(sale.TotalDisc))));
            }

            receipt.Add(EscPosCommands.BoldOn);
            receipt.Add(EscPosCommands.Text(string.Format("{0,-16}{1,16}\n",
                "TOTAL:", Formatting.FormatCurrencyShort(sale.TotalValue))));
            receipt.Add(EscPosCommands.BoldOff);

            // Payment
            if (sale.CashAmount > 0)
            {
                receipt.Add(EscPosCommands.Text(string.Format("{0,-16}{1,16}\n",
                    "TUNAI:", Formatting.FormatCurrencyShort(sale.CashAmount))));
            }
            if (sale.NonCash > 0)
            {
                receipt.Add(EscPosCommands.Text(string.Format("{0,-16}{1,16}\n",
                    "KARTU:", Formatting.FormatCurrencyShort(sale.NonCash))));
            }
            if (sale.VoucherAmount > 0)
            {
                receipt.Add(EscPosCommands.Text(string.Format("{0,-16}{1,16}\n",
                    "VOUCHER:", Formatting.FormatCurrencyShort(sale.VoucherAmount))));
            }
            if (sale.ChangeAmount > 0)
            {
                receipt.Add(EscPosCommands.Text(string.Format("{0,-16}{1,16}\n",
                    "KEMBALI:", Formatting.FormatCurrencyShort(sale.ChangeAmount))));
            }

            // Loyalty
            if (sale.PointValue > 0 && !string.IsNullOrEmpty(sale.MemberCode))
            {
                receipt.Add(EscPosCommands.Text("--------------------------------\n"));
                receipt.Add(EscPosCommands.Text(string.Format("Stiker: {0}  Member: {1}\n",
                    sale.PointValue, sale.MemberCode)));
            }

            // Footer
            receipt.Add(EscPosCommands.Text("================================\n"));
            receipt.Add(EscPosCommands.CenterAlign);
            receipt.Add(EscPosCommands.Text("Terima kasih!\n"));
            receipt.Add(EscPosCommands.Text("Semoga Anda Berbahagia!\n"));
            receipt.Add(EscPosCommands.LeftAlign);
            receipt.Add(EscPosCommands.Text("\n\n\n"));
            receipt.Add(EscPosCommands.PartialCut);

            return receipt.SelectMany(b => b).ToArray();
        }
    }
}
