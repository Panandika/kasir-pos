namespace Kasir.Hardware
{
    public class CashDrawer : ICashDrawer
    {
        private readonly string _printerName;

        public CashDrawer(string printerName)
        {
            _printerName = printerName;
        }

        public bool Open()
        {
            if (string.IsNullOrEmpty(_printerName))
            {
                return false;
            }

            // Try pin 0 first (most common)
            bool success = RawPrinterHelper.SendBytesToPrinter(
                _printerName, EscPosCommands.KickDrawerPin0);

            if (!success)
            {
                // Try pin 1 as fallback
                success = RawPrinterHelper.SendBytesToPrinter(
                    _printerName, EscPosCommands.KickDrawerPin1);
            }

            return success;
        }
    }
}
