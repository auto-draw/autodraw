using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Autodraw.Configuration;
using SkiaSharp;

namespace Autodraw.Painting.Recreate;

/// <summary>
/// The calibrated geometry of a saturation/value rectangle plus a linear hue strip.  Corners
/// are normalised so the user may capture either diagonal order.
/// </summary>
public sealed record BoxGeometry(
    ScreenPoint HueStart,
    ScreenPoint HueEnd,
    ScreenPoint TopLeft,
    ScreenPoint BottomRight)
{
    public int Left => Math.Min(TopLeft.X, BottomRight.X);
    public int Right => Math.Max(TopLeft.X, BottomRight.X);
    public int Top => Math.Min(TopLeft.Y, BottomRight.Y);
    public int Bottom => Math.Max(TopLeft.Y, BottomRight.Y);

    public int Width => Right - Left;
    public int Height => Bottom - Top;

    public double HueLength => Distance(HueStart, HueEnd);

    private static double Distance(ScreenPoint a, ScreenPoint b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}

/// <summary>Pure RGB -> colour-box click-point math.</summary>
public static class BoxPickerMath
{
    /// <summary>Below this length the hue strip cannot resolve a hue.</summary>
    public const double MinimumHueStripPixels = 8;

    /// <summary>Below this size the saturation/value rectangle cannot resolve a colour.</summary>
    public const int MinimumBoxPixels = 8;

    public static BoxGeometry CreateGeometry(
        ScreenPoint hueStart,
        ScreenPoint hueEnd,
        ScreenPoint topLeft,
        ScreenPoint bottomRight)
        => new(hueStart, hueEnd, topLeft, bottomRight);

    public static bool TryCreateGeometry(
        PickerCalibration calibration,
        out BoxGeometry geometry,
        out string error)
    {
        geometry = null!;
        error = string.Empty;
        ArgumentNullException.ThrowIfNull(calibration);

        if (!calibration.TryGet(PickerPoints.BoxHueStart, out var hueStart)) { error = "the hue strip start is missing"; return false; }
        if (!calibration.TryGet(PickerPoints.BoxHueEnd, out var hueEnd)) { error = "the hue strip end is missing"; return false; }
        if (!calibration.TryGet(PickerPoints.BoxTopLeft, out var topLeft)) { error = "the colour box top-left corner is missing"; return false; }
        if (!calibration.TryGet(PickerPoints.BoxBottomRight, out var bottomRight)) { error = "the colour box bottom-right corner is missing"; return false; }

        geometry = CreateGeometry(hueStart, hueEnd, topLeft, bottomRight);
        if (geometry.HueLength < MinimumHueStripPixels)
        {
            error = $"the hue strip is {geometry.HueLength:0.#} px long; click its ends at least {MinimumHueStripPixels:0} px apart";
            return false;
        }

        if (geometry.Width < MinimumBoxPixels || geometry.Height < MinimumBoxPixels)
        {
            error = $"the colour box is {geometry.Width}×{geometry.Height} px; click two opposite corners at least {MinimumBoxPixels} px apart";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Converts a target colour into the two screen positions to click: the point on the hue
    /// strip and the point in the saturation/value rectangle.
    /// </summary>
    public static (ScreenPoint Hue, ScreenPoint SaturationValue) ComputeClickPoints(BoxGeometry geometry, SKColor color)
    {
        ArgumentNullException.ThrowIfNull(geometry);

        var hsv = ColorMath.ToHsv(color);

        var hue = ColorMath.ToPoint(
            geometry.HueStart.X + (geometry.HueEnd.X - geometry.HueStart.X) * (hsv.H / 360d),
            geometry.HueStart.Y + (geometry.HueEnd.Y - geometry.HueStart.Y) * (hsv.H / 360d));

        // Saturation increases left -> right; value increases bottom -> top.
        var x = geometry.Left + (geometry.Right - geometry.Left) * hsv.S;
        var y = geometry.Top + (geometry.Bottom - geometry.Top) * (1d - hsv.V);
        return (hue, ColorMath.ToPoint(x, y));
    }
}

/// <summary>Drives a hue strip plus a saturation/value rectangle.</summary>
public sealed class BoxColorPicker : IColorPicker
{
    private readonly IInputSimulator _input;
    private readonly RecreateOptions _options;
    private readonly BoxGeometry _geometry;

    public BoxColorPicker(IInputSimulator input, BoxGeometry geometry, RecreateOptions options)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _geometry = geometry ?? throw new ArgumentNullException(nameof(geometry));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task SelectColorAsync(SKColor color, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (hue, saturationValue) = BoxPickerMath.ComputeClickPoints(_geometry, color);

        await _input.ClickAsync(hue, cancellationToken).ConfigureAwait(false);
        await DelayAsync(cancellationToken).ConfigureAwait(false);

        await _input.ClickAsync(saturationValue, cancellationToken).ConfigureAwait(false);
        await DelayAsync(cancellationToken).ConfigureAwait(false);
    }

    private Task DelayAsync(CancellationToken cancellationToken)
        => _options.DelayAsync(_options.PickerStepDelayMs, cancellationToken);
}

/// <summary>Registry entry for the colour box picker.</summary>
public sealed class BoxPickerDefinition : IPickerDefinition
{
    public string Id => "Box";

    public string DisplayNameKey => "picker_style_box";

    public IReadOnlyList<PickerPointRequirement> Points { get; } = new[]
    {
        new PickerPointRequirement(PickerPoints.BoxHueStart, "calib_step_hue_start"),
        new PickerPointRequirement(PickerPoints.BoxHueEnd, "calib_step_hue_end"),
        new PickerPointRequirement(PickerPoints.BoxTopLeft, "calib_step_box_tl"),
        new PickerPointRequirement(PickerPoints.BoxBottomRight, "calib_step_box_br")
    };

    public string? Validate(PickerCalibration calibration, ScreenRect? screen)
    {
        ArgumentNullException.ThrowIfNull(calibration);
        if (!BoxPickerMath.TryCreateGeometry(calibration, out _, out var error))
            return "Colour box calibration is incomplete or degenerate: " + error + ".";

        foreach (var requirement in Points)
        {
            var point = calibration.Point(requirement.Key);
            if (point is null) return $"Colour box calibration is missing '{requirement.Key}'.";
            if (screen is { } bounds && !ScreenGeometry.Contains(bounds, point.Value))
                return $"Colour box calibration point {requirement.Key} ({point.Value.X}, {point.Value.Y}) is outside the screen.";
        }

        return null;
    }

    public IColorPicker Create(IInputSimulator input, PickerCalibration calibration, RecreateOptions options, Action<string>? log)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!BoxPickerMath.TryCreateGeometry(calibration, out var geometry, out var error))
            throw new ColorSelectionException("Colour box picker is not calibrated: " + error + ".");

        log?.Invoke(
            $"Colour box picker ready: hue strip ({geometry.HueStart.X}, {geometry.HueStart.Y})->" +
            $"({geometry.HueEnd.X}, {geometry.HueEnd.Y}), SV box {geometry.Width}×{geometry.Height} px.");
        return new BoxColorPicker(input, geometry, options);
    }
}
