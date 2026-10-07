namespace Kasir.Hardware
{
    public interface IRawPrinter
    {
        bool Send(byte[] data);

        /// <summary>
        /// Non-intrusive presence check for status polling: never sends print data
        /// (no spool job, no bytes on the port). Sets LastError when false.
        /// </summary>
        bool IsReachable();
        string LastError { get; }
    }
}
