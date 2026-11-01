using System;
using System.Collections.Generic;
using ZXing;
using ZXing.Common;

namespace Kasir.Hardware
{
    // Webcam fallback for a dead barcode scanner: reads a retail 1D barcode out of one
    // camera frame. Frames arrive as BMP files (the webcam library's output), so the
    // header is parsed here and the pixels go to ZXing as 8-bit grayscale.
    // Not thread-safe (ZXing's reader keeps state): one instance per decode loop.
    public sealed class CameraBarcodeDecoder
    {
        private readonly BarcodeReaderGeneric _reader = new BarcodeReaderGeneric
        {
            AutoRotate = false,
            // TryHarder scans every row (and a rotated copy) of every frame, too slow for an
            // old register PC; the default scans rows around the middle, where the cashier
            // holds the barcode.
            Options = new DecodingOptions
            {
                TryHarder = false,
                TryInverted = false,
                PureBarcode = false,
                PossibleFormats = new List<BarcodeFormat>
                {
                    BarcodeFormat.EAN_13, BarcodeFormat.EAN_8, BarcodeFormat.UPC_A,
                    BarcodeFormat.UPC_E, BarcodeFormat.CODE_128, BarcodeFormat.CODE_39,
                    BarcodeFormat.ITF,
                },
            },
        };

        // Returns the barcode text, or null when the frame has none or is not a BMP we
        // understand (uncompressed 24 or 32 bpp). Never throws.
        public string DecodeBitmap(byte[] bmp)
        {
            try
            {
                byte[] gray = BitmapToGray(bmp, out int width, out int height);
                if (gray == null) return null;
                return DecodeGray(gray, width, height);
            }
            catch (Exception)
            {
                return null;
            }
        }

        // Tightly packed 32 bpp pixels (B,G,R,x or R,G,B,x: channel order barely moves the
        // luma of black-on-white bars), e.g. a JPEG frame decoded by the UI toolkit.
        public string DecodePixels32(byte[] pixels, int width, int height)
        {
            if (pixels == null || width <= 0 || height <= 0 || (long)width * height * 4 > pixels.Length) return null;
            var gray = new byte[width * height];
            for (int i = 0, src = 0; i < gray.Length; i++, src += 4)
                gray[i] = (byte)((pixels[src] * 29 + pixels[src + 1] * 150 + pixels[src + 2] * 77) >> 8);
            return DecodeGray(gray, width, height);
        }

        // ZXing's 1D readers threshold each row from that row's histogram. On a webcam frame
        // the background beside the label (counter, hand) adds a grey peak, and with the
        // usual webcam blur the soft bar edges then fall on the wrong side: the barcode is
        // intact yet unreadable. A centre crop, where the label dominates the row, reads it.
        // Measured on real MacBook camera frames with pasted, degraded EAN-13/Code 128:
        // slightly blurred barcodes read 11/108 full-frame only, 44/108 with the crops,
        // no misreads. (Per-row contrast stretching was tried: worse, and it misread.)
        private static readonly double[] CropWidths = { 1.0, 0.6, 0.4 };

        public string DecodeGray(byte[] gray, int width, int height)
        {
            if (gray == null || width <= 0 || height <= 0 || gray.Length < (long)width * height) return null;
            foreach (double fraction in CropWidths)
            {
                string text = fraction >= 1.0
                    ? DecodeOnce(gray, width, height)
                    : DecodeCentre(gray, width, height, fraction);
                if (text != null) return text;
            }
            return null;
        }

        private string DecodeCentre(byte[] gray, int width, int height, double fraction)
        {
            int w = (int)(width * fraction);
            int h = Math.Max(1, height / 2);
            if (w < 64) return null;
            int left = (width - w) / 2, top = (height - h) / 2;
            var crop = new byte[w * h];
            for (int y = 0; y < h; y++)
                Buffer.BlockCopy(gray, (top + y) * width + left, crop, y * w, w);
            return DecodeOnce(crop, w, h);
        }

        private string DecodeOnce(byte[] gray, int width, int height)
        {
            try
            {
                var source = new RGBLuminanceSource(gray, width, height, RGBLuminanceSource.BitmapFormat.Gray8);
                string text = _reader.Decode(source)?.Text;
                return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
            }
            catch (Exception)
            {
                return null;
            }
        }

        // BMP (BITMAPFILEHEADER + BITMAPINFOHEADER or later) -> top-down Gray8, tightly
        // packed. Null when the data is not an uncompressed 24/32 bpp bitmap.
        public static byte[] BitmapToGray(byte[] bmp, out int width, out int height)
        {
            width = 0;
            height = 0;
            if (bmp == null || bmp.Length < 54 || bmp[0] != 'B' || bmp[1] != 'M') return null;

            int pixelOffset = BitConverter.ToInt32(bmp, 10);
            int w = BitConverter.ToInt32(bmp, 18);
            int h = BitConverter.ToInt32(bmp, 22);
            int bpp = BitConverter.ToUInt16(bmp, 28);
            int compression = BitConverter.ToInt32(bmp, 30);
            int sizeImage = BitConverter.ToInt32(bmp, 34);

            // BI_RGB, or BI_BITFIELDS with the usual 32 bpp BGRX masks.
            if (compression != 0 && !(compression == 3 && bpp == 32)) return null;
            if (bpp != 24 && bpp != 32) return null;

            if (h == int.MinValue) return null;
            bool bottomUp = h > 0;
            h = Math.Abs(h);
            if (w <= 0 || h <= 0 || w > 8192 || h > 8192) return null;

            int bytesPerPixel = bpp / 8;
            if (pixelOffset < 54) return null;
            // Standard BMP rows are padded to 4 bytes, but FlashCap's YUV->RGB transcode
            // writes them tightly packed (w*3) and says so in biSizeImage; without that,
            // tell the two apart by the data length.
            int stride = (w * bpp + 31) / 32 * 4;
            long available = bmp.Length - (long)pixelOffset;
            // Far more pixel data than the header describes: the header is wrong (FlashCap
            // on macOS labels native 1080p RGB frames with the requested 640x480), and
            // reading it with these dimensions gives a striped garbage image.
            if (available > (long)stride * h * 2) return null;
            if (sizeImage == (long)w * bytesPerPixel * h && (long)stride * h != sizeImage)
            {
                stride = w * bytesPerPixel;
                if ((long)stride * h > available) return null;
            }
            else if ((long)stride * h > available)
            {
                stride = w * bytesPerPixel;
                if ((long)stride * h > available) return null;
            }

            var gray = new byte[w * h];
            for (int y = 0; y < h; y++)
            {
                int src = pixelOffset + (bottomUp ? h - 1 - y : y) * stride;
                int dst = y * w;
                for (int x = 0; x < w; x++, src += bytesPerPixel)
                {
                    // BT.601 luma from B,G,R in integer math.
                    gray[dst + x] = (byte)((bmp[src] * 29 + bmp[src + 1] * 150 + bmp[src + 2] * 77) >> 8);
                }
            }

            width = w;
            height = h;
            return gray;
        }
    }

    // A camera sees a barcode over many frames and a blurred frame can misread (Code 39 and
    // ITF have weak checksums). A code is accepted only once two decoded frames in a row
    // agree; frames with nothing decoded do not break the run.
    public sealed class StableReadFilter
    {
        private readonly int _required;
        private string _last;
        private int _count;

        public StableReadFilter(int required = 2)
        {
            _required = Math.Max(1, required);
        }

        // Feed one decode result (null = nothing found). Returns the code when accepted.
        public string Offer(string code)
        {
            if (code == null) return null;
            if (code == _last)
            {
                _count++;
            }
            else
            {
                _last = code;
                _count = 1;
            }

            if (_count < _required) return null;
            Reset();
            return code;
        }

        public void Reset()
        {
            _last = null;
            _count = 0;
        }
    }
}
