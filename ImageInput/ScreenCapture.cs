using System;
using System.Runtime.InteropServices;
using Autodraw.Configuration;
using SkiaSharp;

namespace Autodraw.ImageInput;

/// <summary>Captures a physical screen rectangle with the Win32 GDI. The rectangle is always in
/// the same physical pixel space that <see cref="Avalonia.Platform.Screen.Bounds"/> reports, so a
/// per-monitor DPI scale is applied by the overlay math exactly once and never again here.</summary>
public static class ScreenCapture
{
    /// <summary>Call once before Avalonia creates its first window. Per-monitor-v2 awareness makes
    /// GDI screen coordinates and Avalonia's physical screen bounds agree on mixed-DPI desktops.</summary>
    public static void TryEnablePerMonitorV2Awareness()
    {
#if WINDOWS
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                SetProcessDpiAwarenessContext(new IntPtr(-4)); // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2
        }
        catch
        {
            // Already configured by the host or an app manifest: nothing to do.
        }
#endif
    }

    /// <summary>Returns a caller-owned bitmap, or null when the capture failed. Never throws.</summary>
    public static SKBitmap? Capture(ScreenRect region)
    {
        if (region.Width < 1 || region.Height < 1) return null;
#if WINDOWS
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return null;
        return CaptureWindows(region);
#else
        return null;
#endif
    }

#if WINDOWS
    private const uint Srccopy = 0x00CC0020;
    private const uint Captureblt = 0x40000000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateCompatibleDC(IntPtr hDc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hDc, int width, int height);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr SelectObject(IntPtr hDc, IntPtr hObject);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteDC(IntPtr hDc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool BitBlt(
        IntPtr destDc,
        int destX,
        int destY,
        int width,
        int height,
        IntPtr sourceDc,
        int sourceX,
        int sourceY,
        uint rasterOperation);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern int GetDIBits(
        IntPtr hDc,
        IntPtr hBitmap,
        uint startScan,
        uint scanLines,
        IntPtr bits,
        ref BitmapInfoHeader header,
        uint usage);

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ColorsUsed;
        public uint ColorsImportant;
    }

    private static SKBitmap? CaptureWindows(ScreenRect region)
    {
        var screenDc = IntPtr.Zero;
        var memoryDc = IntPtr.Zero;
        var bitmap = IntPtr.Zero;
        var previous = IntPtr.Zero;
        try
        {
            screenDc = GetDC(IntPtr.Zero);
            if (screenDc == IntPtr.Zero) return null;

            memoryDc = CreateCompatibleDC(screenDc);
            bitmap = CreateCompatibleBitmap(screenDc, region.Width, region.Height);
            if (memoryDc == IntPtr.Zero || bitmap == IntPtr.Zero) return null;

            previous = SelectObject(memoryDc, bitmap);
            if (!BitBlt(memoryDc, 0, 0, region.Width, region.Height, screenDc, region.Left, region.Top, Srccopy | Captureblt))
                return null;

            var header = new BitmapInfoHeader
            {
                Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                Width = region.Width,
                Height = -region.Height, // top-down rows
                Planes = 1,
                BitCount = 32,
                Compression = 0 // BI_RGB
            };

            var pixels = new byte[region.Width * region.Height * 4];
            var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
            try
            {
                if (GetDIBits(memoryDc, bitmap, 0, (uint)region.Height, handle.AddrOfPinnedObject(), ref header, 0) == 0)
                    return null;
            }
            finally
            {
                handle.Free();
            }

            // BI_RGB leaves the alpha byte undefined. Opaque capture pixels must not be mistaken
            // for transparency by the image pipeline's alpha threshold.
            for (var index = 3; index < pixels.Length; index += 4) pixels[index] = 255;

            var result = new SKBitmap(new SKImageInfo(region.Width, region.Height, SKColorType.Bgra8888, SKAlphaType.Opaque));
            Marshal.Copy(pixels, 0, result.GetPixels(), pixels.Length);
            return result;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (previous != IntPtr.Zero && memoryDc != IntPtr.Zero) SelectObject(memoryDc, previous);
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            if (memoryDc != IntPtr.Zero) DeleteDC(memoryDc);
            if (screenDc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screenDc);
        }
    }
#endif
}
