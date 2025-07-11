using System.IO;
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace Autodraw.Models.Utils;

public static class ImageExtensions
{
    public static Bitmap ConvertToAvaloniaBitmap(this SKBitmap bitmap)
    {
        using var encodedStream = new MemoryStream();
        bitmap.Encode(encodedStream, SKEncodedImageFormat.Png, 100);
        encodedStream.Seek(0, SeekOrigin.Begin);
        return new Bitmap(encodedStream);
    }
}