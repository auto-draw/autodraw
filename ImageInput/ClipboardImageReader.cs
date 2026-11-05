using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using SkiaSharp;

namespace Autodraw.ImageInput;

/// <summary>Reads an image out of the OS clipboard. File references win over raw pixels; raw
/// pixels are returned as encoded PNG bytes so they can be wrapped in an in-memory image.</summary>
public static class ClipboardImageReader
{
    /// <summary>Format names used by the desktop clipboard backends for copied image pixels.</summary>
    private static readonly string[] PixelFormats = { "PNG", "image/png", "Bitmap" };

    public static async Task<ClipboardPayload> ReadAsync(IClipboard? clipboard, CancellationToken cancellationToken = default)
    {
        if (clipboard is null) return ClipboardPayload.None;

        try
        {
            var filesPath = FirstLocalPath(await clipboard.GetDataAsync(DataFormats.Files));
            if (filesPath is not null)
                return ClipboardPayload.FromFile(filesPath);

            var namesPath = FirstLocalPath(await clipboard.GetDataAsync(DataFormats.FileNames));
            if (namesPath is not null)
                return ClipboardPayload.FromFile(namesPath);

            foreach (var format in PixelFormats)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var bytes = await TryExtractEncodedBytesAsync(await clipboard.GetDataAsync(format), cancellationToken);
                if (bytes is { Length: > 0 }) return ClipboardPayload.FromBytes(bytes);
            }

            // Avalonia's desktop clipboard does not always surface copied screenshots as PNG.
            // The Win32 CF_DIB fallback covers Snipping Tool and browser "Copy image" payloads.
            var dib = WindowsClipboardImage.TryReadPng();
            if (dib is { Length: > 0 }) return ClipboardPayload.FromBytes(dib);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Utils.Log("Clipboard image read failed: " + ex.Message);
        }

        return ClipboardPayload.None;
    }

    /// <summary>Returns the first clipboard file that is a supported image, even when the
    /// clipboard holds several files and the image is not first.</summary>
    private static string? FirstLocalPath(object? data) =>
        EnumerateLocalPaths(data).FirstOrDefault(ClipboardPayloadClassifier.IsSupportedImagePath);

    private static IEnumerable<string> EnumerateLocalPaths(object? data)
    {
        switch (data)
        {
            case string text when !string.IsNullOrWhiteSpace(text):
                yield return text;
                break;
            case IStorageItem item:
                var path = item.TryGetLocalPath();
                if (!string.IsNullOrWhiteSpace(path)) yield return path!;
                break;
            case IEnumerable<IStorageItem> items:
                foreach (var storageItem in items)
                {
                    var itemPath = storageItem.TryGetLocalPath();
                    if (!string.IsNullOrWhiteSpace(itemPath)) yield return itemPath!;
                }
                break;
            case IEnumerable<string> names:
                foreach (var name in names)
                    if (!string.IsNullOrWhiteSpace(name)) yield return name;
                break;
        }
    }

    /// <summary>Synchronous extraction for drag-and-drop, where the platform invalidates the
    /// <see cref="IDataObject"/> streams as soon as the drop handler returns.</summary>
    public static byte[]? TryExtractEncodedBytes(object? data)
    {
        switch (data)
        {
            case null:
                return null;
            case byte[] bytes:
                return bytes;
            case Stream stream:
                using (var copy = new MemoryStream())
                {
                    stream.CopyTo(copy);
                    return copy.ToArray();
                }
            case SKBitmap skiaBitmap:
                using (var encoded = skiaBitmap.Encode(SKEncodedImageFormat.Png, 100))
                    return encoded?.ToArray();
            case Bitmap bitmap:
                using (var copy = new MemoryStream())
                {
                    bitmap.Save(copy);
                    return copy.ToArray();
                }
            default:
                return null;
        }
    }

    public static async Task<byte[]?> TryExtractEncodedBytesAsync(object? data, CancellationToken cancellationToken = default)
    {
        if (data is Stream stream)
        {
            using (var copy = new MemoryStream())
            {
                await stream.CopyToAsync(copy, cancellationToken);
                return copy.ToArray();
            }
        }

        return TryExtractEncodedBytes(data);
    }
}
