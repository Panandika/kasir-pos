namespace Kasir.Hardware
{
    public class NullRawPrinter : IRawPrinter
    {
        public string LastError => "printer_name belum diset — pilih printer di menu Admin → Printer Config";
        public bool Send(byte[] data) => false;
    }
}
