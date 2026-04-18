namespace Kasir.Hardware
{
    public class CashDrawer : ICashDrawer
    {
        private readonly IRawPrinter _raw;

        public CashDrawer(IRawPrinter raw)
        {
            _raw = raw;
        }

        public CashDrawer(string printerName)
        {
            _raw = string.IsNullOrEmpty(printerName)
                ? (IRawPrinter)new NullRawPrinter()
                : new UsbReceiptPrinter(printerName);
        }

        public bool Open()
        {
            if (_raw.Send(EscPosCommands.KickDrawerPin0))
                return true;

            return _raw.Send(EscPosCommands.KickDrawerPin1);
        }
    }
}
