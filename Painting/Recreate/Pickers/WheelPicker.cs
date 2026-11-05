using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Autodraw.Configuration;
using SkiaSharp;

namespace Autodraw.Painting.Recreate;

/// <summary>
/// The calibrated geometry of a hue/saturation disc plus a linear brightness slider.  Every
/// field is derived from a captured point; none of it is a hardcoded screen constant.
/// </summary>
public sealed record WheelGeometry(
    ScreenPoint Center,
    ScreenPoint Edge,
    ScreenPoint Bright,
    ScreenPoint Dark,
    bool Clockwise)
{
    public double Radius => Distance(Center, Edge);

    /// <summary>Angle of the captured red rim point, in radians, in screen space (y grows down).</summary>
    public double RedAngleRadians => Math.Atan2(Edge.Y - Center.Y, Edge.X - Center.X);

    public double SliderLength => Distance(Dark, Bright);

    private static double Distance(ScreenPoint a, ScreenPoint b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}

/// <summary>
/// Pure RGB -> wheel click-point math.  Split out from <see cref="WheelColorPicker"/> so it can
/// be tested without a mouse, a screen, or a running game.
/// </summary>
public static class WheelPickerMath
{
    /// <summary>Below this radius the disc is too small to click reliably.</summary>
    public const double MinimumRadiusPixels = 8;

    /// <summary>Below this slider length the two endpoints are indistinguishable.</summary>
    public const double MinimumSliderPixels = 8;

    public static WheelGeometry CreateGeometry(
        ScreenPoint center,
        ScreenPoint edge,
        ScreenPoint bright,
        ScreenPoint dark,
        bool clockwise)
        => new(center, edge, bright, dark, clockwise);

    /// <summary>
    /// Builds the geometry from a saved calibration.  Returns false with a user-facing message
    /// when a required point is missing or the geometry is degenerate.
    /// </summary>
    public static bool TryCreateGeometry(
        PickerCalibration calibration,
        bool clockwise,
        out WheelGeometry geometry,
        out string error)
    {
        geometry = null!;
        error = string.Empty;
        ArgumentNullException.ThrowIfNull(calibration);

        if (!calibration.TryGet(PickerPoints.WheelCenter, out var center)) { error = "the wheel centre is missing"; return false; }
        if (!calibration.TryGet(PickerPoints.WheelEdge, out var edge)) { error = "the wheel rim point is missing"; return false; }
        if (!calibration.TryGet(PickerPoints.WheelBright, out var bright)) { error = "the bright end of the brightness slider is missing"; return false; }
        if (!calibration.TryGet(PickerPoints.WheelDark, out var dark)) { error = "the dark end of the brightness slider is missing"; return false; }

        geometry = CreateGeometry(center, edge, bright, dark, clockwise);
        if (geometry.Radius < MinimumRadiusPixels)
        {
            error = $"the wheel radius is {geometry.Radius:0.#} px; click the rim at least {MinimumRadiusPixels:0} px from the centre";
            return false;
        }

        if (geometry.SliderLength < MinimumSliderPixels)
        {
            error = $"the brightness slider is {geometry.SliderLength:0.#} px long; click its two ends further apart";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Converts a target colour into the two screen positions to click: the point on the disc
    /// (hue = angle, saturation = radius) and the point on the brightness slider (value).
    /// </summary>
    public static (ScreenPoint Wheel, ScreenPoint Brightness) ComputeClickPoints(WheelGeometry geometry, SKColor color)
    {
        ArgumentNullException.ThrowIfNull(geometry);

        var hsv = ColorMath.ToHsv(color);

        // Hue is irrelevant at zero saturation, and ColorMath reports 0 for neutrals, so a grey
        // lands exactly on the calibrated centre instead of on an arbitrary rim point.
        var angle = geometry.RedAngleRadians + (geometry.Clockwise ? 1d : -1d) * hsv.H * Math.PI / 180d;
        var radius = geometry.Radius * hsv.S;
        var wheel = ColorMath.ToPoint(
            geometry.Center.X + Math.Cos(angle) * radius,
            geometry.Center.Y + Math.Sin(angle) * radius);

        var brightness = ColorMath.ToPoint(
            geometry.Dark.X + (geometry.Bright.X - geometry.Dark.X) * hsv.V,
            geometry.Dark.Y + (geometry.Bright.Y - geometry.Dark.Y) * hsv.V);

        return (wheel, brightness);
    }
}

/// <summary>Drives a hue/saturation wheel plus its brightness slider.</summary>
public sealed class WheelColorPicker : IColorPicker
{
    private readonly IInputSimulator _input;
    private readonly RecreateOptions _options;
    private readonly WheelGeometry _geometry;

    public WheelColorPicker(IInputSimulator input, WheelGeometry geometry, RecreateOptions options)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _geometry = geometry ?? throw new ArgumentNullException(nameof(geometry));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task SelectColorAsync(SKColor color, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (wheel, brightness) = WheelPickerMath.ComputeClickPoints(_geometry, color);

        await _input.ClickAsync(wheel, cancellationToken).ConfigureAwait(false);
        await DelayAsync(cancellationToken).ConfigureAwait(false);

        await _input.ClickAsync(brightness, cancellationToken).ConfigureAwait(false);
        await DelayAsync(cancellationToken).ConfigureAwait(false);
    }

    private Task DelayAsync(CancellationToken cancellationToken)
        => _options.DelayAsync(_options.PickerStepDelayMs, cancellationToken);
}

/// <summary>Registry entry for the wheel picker.</summary>
public sealed class WheelPickerDefinition : IPickerDefinition
{
    public string Id => "Wheel";

    public string DisplayNameKey => "picker_style_circle";

    public IReadOnlyList<PickerPointRequirement> Points { get; } = new[]
    {
        new PickerPointRequirement(PickerPoints.WheelCenter, "calib_step_wheel_center"),
        new PickerPointRequirement(PickerPoints.WheelEdge, "calib_step_wheel_rim"),
        new PickerPointRequirement(PickerPoints.WheelBright, "calib_step_wheel_bright"),
        new PickerPointRequirement(PickerPoints.WheelDark, "calib_step_wheel_dark")
    };

    public string? Validate(PickerCalibration calibration, ScreenRect? screen)
    {
        ArgumentNullException.ThrowIfNull(calibration);
        if (!WheelPickerMath.TryCreateGeometry(calibration, clockwise: true, out _, out var error))
            return "Wheel calibration is incomplete or degenerate: " + error + ".";

        foreach (var requirement in Points)
        {
            var point = calibration.Point(requirement.Key);
            if (point is null) return $"Wheel calibration is missing '{requirement.Key}'.";
            if (screen is { } bounds && !ScreenGeometry.Contains(bounds, point.Value))
                return $"Wheel calibration point {requirement.Key} ({point.Value.X}, {point.Value.Y}) is outside the screen.";
        }

        return null;
    }

    public IColorPicker Create(IInputSimulator input, PickerCalibration calibration, RecreateOptions options, Action<string>? log)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!WheelPickerMath.TryCreateGeometry(calibration, options.WheelClockwise, out var geometry, out var error))
            throw new ColorSelectionException("Wheel picker is not calibrated: " + error + ".");

        log?.Invoke(
            $"Wheel picker ready: centre ({geometry.Center.X}, {geometry.Center.Y}), radius {geometry.Radius:0.#} px, " +
            $"red at {geometry.RedAngleRadians * 180d / Math.PI:0.#}°, direction {(geometry.Clockwise ? "CW" : "CCW")}.");
        return new WheelColorPicker(input, geometry, options);
    }
}
