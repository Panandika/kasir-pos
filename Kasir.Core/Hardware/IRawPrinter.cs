namespace Kasir.Hardware
{
    public interface IRawPrinter
    {
        bool Send(byte[] data);
        string LastError { get; }
    }
}
