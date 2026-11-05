using System;
using Autodraw.Configuration;

namespace Autodraw.Painting.Recreate;

/// <summary>
/// Maps image pixels into the user-selected screen rectangle with a uniform, aspect-preserving
/// fit.  The image is centred and letterboxed instead of being stretched, so a stroke plan keeps
/// the source proportions even when the canvas is not the same shape as the image.
/// </summary>
public sealed class CanvasMapper
{
    public CanvasMapper(int imageWidth, int imageHeight, ScreenRect canvas)
    {
        if (imageWidth <= 0) throw new ArgumentOutOfRangeException(nameof(imageWidth));
        if (imageHeight <= 0) throw new ArgumentOutOfRangeException(nameof(imageHeight));
        if (canvas.Width <= 0 || canvas.Height <= 0)
            throw new ArgumentException("The draw region must have positive width and height.", nameof(canvas));

        ImageWidth = imageWidth;
        ImageHeight = imageHeight;
        Canvas = canvas;
        Scale = Math.Min(canvas.Width / (double)imageWidth, canvas.Height / (double)imageHeight);
        OffsetX = canvas.Left + (canvas.Width - imageWidth * Scale) / 2d;
        OffsetY = canvas.Top + (canvas.Height - imageHeight * Scale) / 2d;
    }

    public int ImageWidth { get; }
    public int ImageHeight { get; }
    public ScreenRect Canvas { get; }
    public double Scale { get; }
    public double OffsetX { get; }
    public double OffsetY { get; }

    /// <summary>Maps an image-space point (pixel edge coordinates, not necessarily integral).</summary>
    public ScreenPoint Map(double imageX, double imageY)
    {
        var x = OffsetX + imageX * Scale;
        var y = OffsetY + imageY * Scale;
        return new ScreenPoint(
            Math.Clamp((int)Math.Round(x), Canvas.Left, Canvas.Right),
            Math.Clamp((int)Math.Round(y), Canvas.Top, Canvas.Bottom));
    }

    /// <summary>Maps the centre of pixel <paramref name="x"/>,<paramref name="y"/>.</summary>
    public ScreenPoint MapPixel(int x, int y) => Map(x + .5, y + .5);

    /// <summary>Maps an inclusive run on one image row to a screen-space horizontal segment.</summary>
    public (ScreenPoint Start, ScreenPoint End) MapRun(int row, int startX, int endX)
    {
        if (endX < startX) (startX, endX) = (endX, startX);
        return (MapPixel(startX, row), MapPixel(endX, row));
    }
}
