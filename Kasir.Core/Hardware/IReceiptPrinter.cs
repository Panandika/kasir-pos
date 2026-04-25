namespace Kasir.Hardware
{
    public interface IReceiptPrinter
    {
        bool Print(byte[] escPosData);
        bool IsAvailable();
        string LastError { get; }
    }
}
