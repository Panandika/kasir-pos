namespace Kasir.Hardware
{
    public interface ICashDrawer
    {
        bool Open();
        string LastError { get; }
    }
}
