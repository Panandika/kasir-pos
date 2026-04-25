using Kasir.Data.Repositories;

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
            : this(RawPrinterFactory.Create("", printerName))
        {
        }

        public CashDrawer(ConfigRepository config)
            : this(RawPrinterFactory.Create(
                config.Get("printer_kind") ?? "",
                config.Get("printer_name") ?? "",
                int.TryParse(config.Get("printer_baud"), out var b) && b > 0 ? b : RawPrinterFactory.DefaultBaud))
        {
        }

        public string LastError => _raw.LastError;

        public bool Open()
        {
            if (_raw.Send(EscPosCommands.KickDrawerPin0))
                return true;

            return _raw.Send(EscPosCommands.KickDrawerPin1);
        }
    }
}
