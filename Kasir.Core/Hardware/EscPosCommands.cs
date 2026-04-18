using System.Text;

namespace Kasir.Hardware
{
    public static class EscPosCommands
    {
        public static readonly byte[] Init        = { 0x1B, 0x40 };                        // ESC @
        public static readonly byte[] CenterAlign = { 0x1B, 0x61, 0x01 };                  // ESC a 1
        public static readonly byte[] LeftAlign   = { 0x1B, 0x61, 0x00 };                  // ESC a 0
        public static readonly byte[] BoldOn      = { 0x1B, 0x45, 0x01 };                  // ESC E 1
        public static readonly byte[] BoldOff     = { 0x1B, 0x45, 0x00 };                  // ESC E 0
        public static readonly byte[] PartialCut  = { 0x1D, 0x56, 0x01 };                  // GS V 1
        public static readonly byte[] FullCut     = { 0x1D, 0x56, 0x00 };                  // GS V 0
        public static readonly byte[] KickDrawerPin0 = { 0x1B, 0x70, 0x00, 0x19, 0xFA };   // ESC p 0 25 250
        public static readonly byte[] KickDrawerPin1 = { 0x1B, 0x70, 0x01, 0x19, 0xFA };   // ESC p 1 25 250
        public static readonly byte[] LineFeed    = { 0x0A };                               // LF

        public static byte[] Text(string text)
        {
            return Encoding.GetEncoding(437).GetBytes(text);
        }
    }
}
