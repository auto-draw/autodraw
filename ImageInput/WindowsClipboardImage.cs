using System;
using System.Runtime.InteropServices;
using System.Threading;
using SkiaSharp;

namespace Autodraw.ImageInput;

/// <summary>Win32 fallback for screenshots and copied bitmaps. Avalonia exposes clipboard pixels
/// through several platform-specific formats; when none of them yield bytes we read CF_DIB
/// directly and re-encode it as PNG so the rest of the pipeline sees a normal encoded image.</summary>
public static class WindowsClipboardImage
{
    public static byte[]? TryReadPng()
    {
#if WINDOWS
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return null;

        for (var attempt = 0; attempt < 5; attempt++)
        {
            if (!OpenClipboard(IntPtr.Zero))
            {
                Thread.Sleep(20);
                continue;
            }

            try
            {
                if (!IsClipboardFormatAvailable(CfDib)) return null;
                var handle = GetClipboardData(CfDib);
                if (handle == IntPtr.Zero) return null;
                return ConvertDib(handle);
            }
            finally
            {
                CloseClipboard();
            }
        }
#endif
        return null;
    }

#if WINDOWS
    private const uint CfDib = 8;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr owner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetClipboardData(uint format);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern UIntPtr GlobalSize(IntPtr handle);

    private static unsafe byte[]? ConvertDib(IntPtr handle)
    {
        var size = (long)GlobalSize(handle);
        if (size <= 0) return null;

        var pointer = GlobalLock(handle);
        if (pointer == IntPtr.Zero) return null;

        try
        {
            var headerSize = Marshal.ReadInt32(pointer, 0);
            if (headerSize < 40) return null;

            var width = Marshal.ReadInt32(pointer, 4);
            var height = Marshal.ReadInt32(pointer, 8);
            var bitCount = (ushort)Marshal.ReadInt16(pointer, 14);
            var compression = Marshal.ReadInt32(pointer, 16);
            if (width <= 0 || height == 0) return null;
            if (bitCount != 24 && bitCount != 32) return null;
            if (compression != 0 && compression != 3) return null;

            var absoluteHeight = Math.Abs(height);
            var offset = headerSize;
            // A 40-byte BITMAPINFOHEADER keeps the BI_BITFIELDS colour masks after the header;
            // BITMAPV4/V5 headers already embed them, so only the short header needs the extra 12.
            if (headerSize == 40 && compression == 3) offset += 12;

            var pitch = ((width * bitCount + 31) / 32) * 4;
            var required = (long)offset + (long)pitch * absoluteHeight;
            if (required > size) return null;

            var bottomUp = height > 0;
            var bitmap = new SKBitmap(new SKImageInfo(width, absoluteHeight, SKColorType.Bgra8888, SKAlphaType.Opaque));
            try
            {
                var destination = (byte*)bitmap.GetPixels().ToPointer();
                var source = (byte*)pointer + offset;
                for (var y = 0; y < absoluteHeight; y++)
                {
                    var sourceRow = source + (bottomUp ? absoluteHeight - 1 - y : y) * pitch;
                    var destinationRow = destination + y * width * 4;
                    for (var x = 0; x < width; x++)
                    {
                        var pixel = sourceRow + x * (bitCount / 8);
                        var destinationPixel = destinationRow + x * 4;
                        destinationPixel[0] = pixel[0];
                        destinationPixel[1] = pixel[1];
                        destinationPixel[2] = pixel[2];
                        destinationPixel[3] = 255;
                    }
                }

                using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
                return data?.ToArray();
            }
            finally
            {
                bitmap.Dispose();
            }
        }
        catch
        {
            return null;
        }
        finally
        {
            GlobalUnlock(handle);
        }
    }
#endif
}
