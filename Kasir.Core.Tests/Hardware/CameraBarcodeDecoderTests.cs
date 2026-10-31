using System;
using System.IO;
using NUnit.Framework;
using FluentAssertions;
using Kasir.Hardware;
using ZXing;
using ZXing.Common;

namespace Kasir.Tests.Hardware
{
    // Webcam fallback for the barcode scanner: frames arrive as BMP files.
    [TestFixture]
    public class CameraBarcodeDecoderTests
    {
        private static ZXing.Rendering.PixelData Render(BarcodeFormat format, string text, int width = 400)
        {
            var writer = new BarcodeWriterPixelData
            {
                Format = format,
                Options = new EncodingOptions { Width = width, Height = 160, Margin = 20, PureBarcode = true },
            };
            return writer.Write(text);
        }

        // BGRA top-down pixels -> BMP file with the requested depth and row order.
        // padded=false mimics FlashCap's transcoded frames (rows not padded to 4 bytes).
        private static byte[] ToBmp(ZXing.Rendering.PixelData px, int bpp, bool bottomUp, bool padded = true)
        {
            int w = px.Width, h = px.Height, bytesPerPixel = bpp / 8;
            int stride = padded ? (w * bpp + 31) / 32 * 4 : w * bytesPerPixel;
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);
            bw.Write((byte)'B'); bw.Write((byte)'M');
            bw.Write(54 + stride * h); bw.Write(0); bw.Write(54);
            bw.Write(40); bw.Write(w); bw.Write(bottomUp ? h : -h);
            bw.Write((short)1); bw.Write((short)bpp); bw.Write(0); bw.Write(stride * h);
            bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);
            var row = new byte[stride];
            for (int i = 0; i < h; i++)
            {
                int y = bottomUp ? h - 1 - i : i;
                Array.Clear(row, 0, row.Length);
                for (int x = 0; x < w; x++)
                    Buffer.BlockCopy(px.Pixels, (y * w + x) * 4, row, x * bytesPerPixel, bytesPerPixel);
                bw.Write(row);
            }
            return ms.ToArray();
        }

        [TestCase(24, true)]
        [TestCase(24, false)]
        [TestCase(32, true)]
        [TestCase(32, false)]
        public void DecodeBitmap_Ean13_AnyDepthAndRowOrder(int bpp, bool bottomUp)
        {
            var bmp = ToBmp(Render(BarcodeFormat.EAN_13, "8992761002015"), bpp, bottomUp);
            new CameraBarcodeDecoder().DecodeBitmap(bmp).Should().Be("8992761002015");
        }

        [Test]
        public void DecodeBitmap_Code128()
        {
            var bmp = ToBmp(Render(BarcodeFormat.CODE_128, "ABC-1234"), 24, true);
            new CameraBarcodeDecoder().DecodeBitmap(bmp).Should().Be("ABC-1234");
        }

        [TestCase(true)]
        [TestCase(false)]
        public void DecodeBitmap_OddWidth_PaddedAndUnpaddedRows(bool padded)
        {
            var px = Render(BarcodeFormat.EAN_13, "8992761002015", width: 322);
            px.Width.Should().Be(322); // 322*3 is not a multiple of 4
            new CameraBarcodeDecoder().DecodeBitmap(ToBmp(px, 24, true, padded)).Should().Be("8992761002015");
        }

        [Test]
        public void DecodePixels32_Ean13()
        {
            var px = Render(BarcodeFormat.EAN_13, "8992761002015");
            new CameraBarcodeDecoder().DecodePixels32(px.Pixels, px.Width, px.Height).Should().Be("8992761002015");
            new CameraBarcodeDecoder().DecodePixels32(new byte[8], px.Width, px.Height).Should().BeNull();
        }

        [Test]
        public void DecodeBitmap_BlankFrame_ReturnsNull()
        {
            var px = Render(BarcodeFormat.EAN_13, "8992761002015");
            for (int i = 0; i < px.Pixels.Length; i++) px.Pixels[i] = 255;
            new CameraBarcodeDecoder().DecodeBitmap(ToBmp(px, 24, true)).Should().BeNull();
        }

        [Test]
        public void DecodeBitmap_Garbage_ReturnsNullWithoutThrowing()
        {
            var decoder = new CameraBarcodeDecoder();
            decoder.DecodeBitmap(null).Should().BeNull();
            decoder.DecodeBitmap(new byte[0]).Should().BeNull();
            decoder.DecodeBitmap(new byte[] { (byte)'B', (byte)'M', 1, 2, 3 }).Should().BeNull();

            var bmp = ToBmp(Render(BarcodeFormat.EAN_13, "8992761002015"), 24, true);
            Array.Resize(ref bmp, bmp.Length / 2); // truncated frame
            decoder.DecodeBitmap(bmp).Should().BeNull();

            var jpeg = new byte[100]; jpeg[0] = 0xFF; jpeg[1] = 0xD8;
            decoder.DecodeBitmap(jpeg).Should().BeNull();
        }

        [Test]
        public void StableReadFilter_NeedsTwoMatchingReads()
        {
            var filter = new StableReadFilter();
            filter.Offer("A").Should().BeNull();
            filter.Offer(null).Should().BeNull(); // empty frame does not break the run
            filter.Offer("A").Should().Be("A");
        }

        [Test]
        public void StableReadFilter_DifferentCodesRestartTheRun()
        {
            var filter = new StableReadFilter();
            filter.Offer("A").Should().BeNull();
            filter.Offer("B").Should().BeNull();
            filter.Offer("B").Should().Be("B");
            // Accepted -> reset: the next read starts over.
            filter.Offer("B").Should().BeNull();
        }
    }
}
