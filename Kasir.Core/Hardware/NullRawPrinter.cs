namespace Kasir.Hardware
{
    public class NullRawPrinter : IRawPrinter
    {
        public string LastError => "printer_name belum diset (NullRawPrinter aktif)";
        public bool Send(byte[] data) => false;
    }
}
