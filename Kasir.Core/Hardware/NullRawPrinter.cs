namespace Kasir.Hardware
{
    public class NullRawPrinter : IRawPrinter
    {
        public bool Send(byte[] data) => false;
    }
}
