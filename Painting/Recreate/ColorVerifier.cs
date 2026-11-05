using System;
using Autodraw.Configuration;
using Autodraw.ImageInput;
using SkiaSharp;

namespace Autodraw.Painting.Recreate;

public interface IScreenColorSampler
{
    /// <summary>Returns null when the pixel could not be captured.</summary>
    SKColor? TrySample(ScreenPoint point);
}

/// <summary>One-pixel GDI capture.  The capture object is disposed immediately.</summary>
public sealed class GdiScreenColorSampler : IScreenColorSampler
{
    public SKColor? TrySample(ScreenPoint point)
    {
        try
        {
            using var bitmap = ScreenCapture.Capture(new ScreenRect(point.X, point.Y, point.X, point.Y));
            return bitmap is null ? null : bitmap.GetPixel(0, 0);
        }
        catch
        {
            return null;
        }
    }
}

public readonly record struct ColorVerificationResult(
    bool Matched,
    SKColor Sampled,
    double DeltaE,
    string Detail)
{
    public static ColorVerificationResult SampleFailed(string detail)
        => new(false, default, double.PositiveInfinity, detail);
}

public interface IColorVerifier
{
    ColorVerificationResult Verify(SKColor expected);
}

/// <summary>Reads the target application's current-colour indicator and compares it with ΔE2000.</summary>
public sealed class ColorVerifier : IColorVerifier
{
    private readonly IScreenColorSampler _sampler;
    private readonly ScreenPoint _point;
    private readonly double _tolerance;

    public ColorVerifier(IScreenColorSampler sampler, ScreenPoint point, double tolerance)
    {
        _sampler = sampler ?? throw new ArgumentNullException(nameof(sampler));
        _point = point;
        _tolerance = Math.Max(0, tolerance);
    }

    public ColorVerificationResult Verify(SKColor expected)
    {
        var sampled = _sampler.TrySample(_point);
        if (sampled is null)
            return ColorVerificationResult.SampleFailed(
                $"could not capture the current-colour indicator at ({_point.X}, {_point.Y})");

        var delta = ColorMatcher.DeltaE2000(expected, sampled.Value);
        return new ColorVerificationResult(
            delta <= _tolerance,
            sampled.Value,
            delta,
            $"expected #{expected.Red:X2}{expected.Green:X2}{expected.Blue:X2}, sampled " +
            $"#{sampled.Value.Red:X2}{sampled.Value.Green:X2}{sampled.Value.Blue:X2}, ΔE {delta:0.0} (limit {_tolerance:0.0})");
    }
}
