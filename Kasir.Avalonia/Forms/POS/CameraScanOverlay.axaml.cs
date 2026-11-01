using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using FlashCap;
using Kasir.Hardware;
using Kasir.Utils;

namespace Kasir.Avalonia.Forms.POS;

// Webcam fallback when the barcode scanner is dead or unplugged (F7 / "Kamera" on the
// sale screen). Shows the live camera, decodes retail barcodes from the frames and
// returns the first code read twice in a row, or null on Esc / no camera.
//
// Threading (FlashCap): frames arrive on a FlashCap worker thread, one at a time, and
// frames that arrive while one is being handled are dropped (isScattering:false,
// maxQueuingFrames:1). The frame buffer is reused by later frames, so it is decoded
// inside the handler; the preview gets its own copy. The device is never disposed from
// the handler (that deadlocks); Show() closes it after the result.
public partial class CameraScanOverlay : UserControl
{
    private static readonly TimeSpan PreviewInterval = TimeSpan.FromMilliseconds(100);

    private readonly TaskCompletionSource<string?> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly SemaphoreSlim _deviceLock = new(1, 1);
    private readonly CameraBarcodeDecoder _decoder = new();
    private readonly StableReadFilter _filter = new();
    private readonly Stopwatch _previewClock = Stopwatch.StartNew();
    private CaptureDeviceDescriptor[] _cameras = Array.Empty<CaptureDeviceDescriptor>();
    private int _cameraIndex;
    private CaptureDevice? _device;
    private Bitmap? _preview;
    private volatile bool _closed;
    private bool _started;
    private bool _switching;
    private bool _frameErrorLogged;

    public CameraScanOverlay()
    {
        InitializeComponent();

        // The camera is opened only once shown, never in the constructor (layout tests
        // build this view on CI machines).
        AttachedToVisualTree += (_, _) =>
        {
            Focus();
            Dispatcher.UIThread.Post(() => Focus(), DispatcherPriority.Loaded);
            if (_started) return;
            _started = true;
            _ = StartAsync();
        };
        // Replaced by another overlay (or the screen closed): end the scan as cancelled,
        // otherwise the F7 caller would wait forever.
        DetachedFromVisualTree += (_, _) =>
        {
            _tcs.TrySetResult(null);
            _ = CloseCameraAsync();
        };
        KeyDown += OnKey;
        // Tunnel + handledEventsToo: anything a half-working HID scanner types while the
        // camera is open is swallowed here and cannot reach the code box behind.
        AddHandler(TextInputEvent, (_, e) => e.Handled = true,
            global::Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    public Task<string?> Result => _tcs.Task;

    private void OnKey(object? sender, KeyEventArgs e)
    {
        e.Handled = true; // Enter/Space/F-keys must not leak to the sale screen behind
        if (KeyboardRouter.IsEscape(e)) _tcs.TrySetResult(null);
        else if (KeyboardRouter.IsTab(e) && _cameras.Length > 1) _ = SwitchCameraAsync();
    }

    private async Task StartAsync()
    {
        try
        {
            // The previous scan may still be releasing the webcam.
            await s_release;
            // DirectShow binds every device to enumerate and macOS waits on the camera
            // permission prompt, so keep enumeration off the UI thread.
            _cameras = await Task.Run(FindCameras);
            if (_closed) return;
            if (_cameras.Length == 0)
            {
                SetStatus("Kamera tidak ditemukan. Pastikan webcam terpasang. Esc = kembali.");
                return;
            }
            await OpenCameraAsync(0);
        }
        catch (Exception ex)
        {
            CrashLog.Write("CameraScan.Start", ex);
            SetStatus("Kamera gagal dibuka: " + ex.Message);
        }
    }

    // The same webcam shows up once per Windows API; use the best API that has one.
    private static CaptureDeviceDescriptor[] FindCameras()
    {
        var usable = new CaptureDevices().EnumerateDescriptors()
            .Where(d => d.Characteristics.Any(IsUsable))
            .ToArray();
        DeviceTypes[] preference =
        {
            DeviceTypes.DirectShow, DeviceTypes.MediaFoundation,
            DeviceTypes.V4L2, DeviceTypes.AVFoundation, DeviceTypes.VideoForWindows,
        };
        foreach (var type in preference)
        {
            var ofType = usable.Where(d => d.DeviceType == type).ToArray();
            if (ofType.Length > 0) return ofType;
        }
        return usable;
    }

    // Formats the decoder can read: YUV is transcoded to 24 bpp BMP, RGB24/32 come as BMP,
    // JPEG goes through the toolkit's decoder. (RGB8/15/16 and PNG are not handled.)
    private static bool IsUsable(VideoCharacteristics c) =>
        c.PixelFormat is PixelFormats.YUYV or PixelFormats.UYVY or PixelFormats.NV12
            or PixelFormats.RGB24 or PixelFormats.RGB32 or PixelFormats.ARGB32 or PixelFormats.JPEG
        && c.Width > 0 && c.Height > 0;

    // YUV first: FlashCap converts it to RGB itself and writes a header that matches its
    // output; device RGB keeps the device's own header. JPEG needs a decode first, so it
    // comes last. Size closest to 640x480: big enough for barcode bars, small enough for
    // an old register PC.
    // Known: FlashCap 1.12 on macOS ignores the chosen format and always captures native
    // 1080p (measured on a MacBook Air: RGB frames are 1080p behind a 640x480 header,
    // YUV frames are 1080p data converted as 640x480), so the preview is garbage there.
    // Windows DirectShow applies the format to the device; that is the target.
    private static int FormatRank(PixelFormats f) => f switch
    {
        PixelFormats.YUYV or PixelFormats.UYVY or PixelFormats.NV12 => 0,
        PixelFormats.JPEG => 2,
        _ => 1,
    };

    private static VideoCharacteristics PickFormat(CaptureDeviceDescriptor camera) =>
        camera.Characteristics.Where(IsUsable)
            .OrderBy(c => FormatRank(c.PixelFormat))
            .ThenBy(c => Math.Abs(c.Width - 640) + Math.Abs(c.Height - 480))
            .ThenByDescending(c => (double)c.FramesPerSecond)
            .First();

    private async Task OpenCameraAsync(int index)
    {
        await _deviceLock.WaitAsync();
        try
        {
            await DisposeDeviceAsync();
            if (_closed) return;
            _cameraIndex = index;
            var camera = _cameras[index];
            var format = PickFormat(camera);
            SetStatus($"Membuka {camera.Name}...");
            _filter.Reset();
            var device = await camera.OpenAsync(format, TranscodeFormats.Auto, false, 1, OnFrame);
            _device = device;
            if (_closed) { await DisposeDeviceAsync(); return; }
            await device.StartAsync();
            string more = _cameras.Length > 1 ? $"  (kamera {index + 1}/{_cameras.Length})" : "";
            SetStatus($"{camera.Name} {format.Width}x{format.Height}{more} - arahkan barcode ke kamera.");
        }
        catch (Exception ex)
        {
            CrashLog.Write("CameraScan.Open", ex);
            SetStatus("Kamera gagal dibuka: " + ex.Message);
        }
        finally
        {
            _deviceLock.Release();
        }
    }

    // Tab again while a switch is still opening the next camera is ignored.
    private async Task SwitchCameraAsync()
    {
        if (_switching) return;
        _switching = true;
        try { await OpenCameraAsync((_cameraIndex + 1) % _cameras.Length); }
        finally { _switching = false; }
    }

    // FlashCap worker thread.
    private void OnFrame(PixelBufferScope scope)
    {
        if (_closed) return;
        Bitmap? preview = null;
        try
        {
            byte[] image = scope.Buffer.CopyImage();
            bool isJpeg = image.Length > 2 && image[0] == 0xFF && image[1] == 0xD8;
            bool previewDue = _previewClock.Elapsed >= PreviewInterval;

            string? code;
            if (isJpeg)
            {
                preview = new Bitmap(new MemoryStream(image));
                code = _decoder.DecodePixels32(CopyPixels(preview), preview.PixelSize.Width, preview.PixelSize.Height);
            }
            else
            {
                code = _decoder.DecodeBitmap(image);
                if (previewDue) preview = new Bitmap(new MemoryStream((byte[])image.Clone()));
            }

            string? accepted = _filter.Offer(code);
            if (accepted != null)
            {
                preview?.Dispose();
                _closed = true;
                _tcs.TrySetResult(accepted);
                return;
            }

            if (previewDue && preview != null)
            {
                _previewClock.Restart();
                ShowPreview(preview);
            }
            else
            {
                preview?.Dispose();
            }
        }
        catch (Exception ex)
        {
            // A bad frame (driver hiccup, odd format) is skipped; the next one may be fine.
            preview?.Dispose();
            if (!_frameErrorLogged)
            {
                _frameErrorLogged = true;
                CrashLog.Write("CameraScan.Frame", ex);
            }
        }
    }

    private static byte[] CopyPixels(Bitmap bitmap)
    {
        var size = bitmap.PixelSize;
        var pixels = new byte[size.Width * size.Height * 4];
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect(size), handle.AddrOfPinnedObject(), pixels.Length, size.Width * 4);
        }
        finally
        {
            handle.Free();
        }
        return pixels;
    }

    private void ShowPreview(Bitmap bitmap)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_closed) { bitmap.Dispose(); return; }
            var old = _preview;
            _preview = bitmap;
            ImgPreview.Source = bitmap;
            old?.Dispose();
        });
    }

    private void SetStatus(string text) => Dispatcher.UIThread.Post(() => LblStatus.Text = text);

    // Idempotent; safe from any thread except the frame handler.
    public async Task CloseCameraAsync()
    {
        _closed = true;
        await _deviceLock.WaitAsync();
        try { await DisposeDeviceAsync(); }
        finally { _deviceLock.Release(); }
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            ImgPreview.Source = null;
            _preview?.Dispose();
            _preview = null;
        });
    }

    private async Task DisposeDeviceAsync()
    {
        var device = _device;
        _device = null;
        if (device == null) return;
        try { await device.StopAsync(); } catch (Exception) { }
        try { await device.DisposeAsync(); } catch (Exception) { }
    }

    public static async Task<string?> Show(Visual? owner)
    {
        ShellWindow? shell = TopLevel.GetTopLevel(owner) as ShellWindow;
        if (shell is null
            && Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            shell = desktop.MainWindow as ShellWindow;
        }
        if (shell is null) return null;

        var overlay = new CameraScanOverlay();
        shell.ShowOverlay(overlay);
        shell.UpdateLayout();
        overlay.Focus();
        try { return await overlay.Result; }
        finally
        {
            shell.HideOverlay(overlay);
            // Stopping a DirectShow camera can take a second on an old PC: hand the code
            // back now and release in the background; the next open waits for it.
            s_release = overlay.CloseCameraAsync();
        }
    }

    private static Task s_release = Task.CompletedTask;
}
