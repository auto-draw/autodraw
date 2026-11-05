using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autodraw.Configuration;
using SkiaSharp;

namespace Autodraw.Painting.Recreate;

/// <summary>
/// Immutable snapshot of every setting the recreate engine needs.  The UI builds this once at
/// the start of a run so a configuration change cannot alter a draw that is already in flight.
/// </summary>
public sealed class RecreateOptions
{
    /// <summary>Registered picker id ("Wheel" or "Box").  Resolved through <see cref="PickerRegistry"/>.</summary>
    public string PickerType { get; set; } = "Wheel";

    /// <summary>True when hue increases clockwise around the wheel.</summary>
    public bool WheelClockwise { get; set; }

    /// <summary>Colour space used for quantization and layer assignment: CIELAB, RGB or OKLAB.</summary>
    public string PaletteColorSpace { get; set; } = "CIELAB";

    public string LayerOrder { get; set; } = "coverage-desc";
    public StrokeOrder StrokeOrder { get; set; } = StrokeOrder.Serpentine;
    public int PaletteSize { get; set; } = 16;
    public int AlphaThreshold { get; set; } = 200;
    public int StrokeDelayMs { get; set; } = 10;
    public int MoveStepPixels { get; set; } = 8;
    public int BrushSize { get; set; } = 1;
    public bool Verify { get; set; } = true;
    public double VerifyTolerance { get; set; } = 8;
    public int VerifyRetries { get; set; } = 2;
    public bool DryRun { get; set; }
    public ScreenRect Canvas { get; set; } = new(0, 0, 317, 317);

    /// <summary>Pixel that shows the target application's currently selected colour.</summary>
    public ScreenPoint? CurrentColorPoint { get; set; }

    /// <summary>Time between picker clicks/typing.  Kept non-zero because many paint programs
    /// debounce keyboard focus and repaint asynchronously.</summary>
    public int PickerStepDelayMs { get; set; } = 40;

    /// <summary>Time allowed for the target application's current-colour indicator to repaint
    /// before it is sampled.</summary>
    public int VerifySettleDelayMs { get; set; } = 90;

    /// <summary>How many times a colour selection is attempted before the run aborts.</summary>
    public int SelectionAttempts { get; set; } = 2;

    /// <summary>How many times a single stroke is attempted when the input backend throws.</summary>
    public int StrokeAttempts { get; set; } = 2;

    /// <summary>Enforce that the target window keeps focus for the whole run.</summary>
    public bool FocusGuard { get; set; } = true;

    /// <summary>Resume from a matching checkpoint when one exists.</summary>
    public bool Resume { get; set; } = true;

    /// <summary>Strokes between checkpoint writes.</summary>
    public int CheckpointEveryStrokes { get; set; } = 25;

    /// <summary>Checkpoint to resume from, or null for a fresh run.  Set by the caller after
    /// matching a stored checkpoint against <see cref="Fingerprint"/>.</summary>
    public RecreateCheckpoint? ResumeCheckpoint { get; set; }

    /// <summary>Identity of the image + canvas + palette + stroke plan for checkpoint matching.</summary>
    public string? Fingerprint { get; set; }

    /// <summary>When false the caller retains ownership of every <see cref="Layer"/>.</summary>
    public bool DisposeLayers { get; set; } = true;

    /// <summary>Injected for tests; production uses the real clock through <see cref="Task.Delay(int, CancellationToken)"/>.</summary>
    public Func<int, CancellationToken, Task> DelayAsync { get; set; } =
        (milliseconds, token) => milliseconds <= 0 ? Task.CompletedTask : Task.Delay(milliseconds, token);

    public static RecreateOptions FromConfig(AutoDrawConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return new RecreateOptions
        {
            PickerType = PickerRegistry.Require(config.PickerType).Id,
            WheelClockwise = string.Equals(config.WheelDirection, "CW", StringComparison.OrdinalIgnoreCase),
            PaletteColorSpace = NormalizeColorSpace(config.ColorDistanceMethod),
            LayerOrder = LayerOrdering.Normalize(config.RecreateLayerOrder),
            StrokeOrder = ParseStrokeOrder(config.RecreateStrokeOrder),
            PaletteSize = Math.Clamp(config.KClusters, 1, 64),
            AlphaThreshold = Math.Clamp(config.RecreateAlphaThreshold, 0, 255),
            StrokeDelayMs = Math.Clamp(config.RecreateStrokeDelayMs, 0, 60_000),
            MoveStepPixels = Math.Clamp(config.RecreateMoveStepPixels, 1, 512),
            BrushSize = Math.Clamp(config.BrushSize, 1, 64),
            Verify = config.RecreateVerify,
            VerifyTolerance = Math.Clamp(config.RecreateVerifyTolerance, 0, 100),
            VerifyRetries = Math.Clamp(config.RecreateVerifyRetries, 0, 10),
            DryRun = config.RecreateDryRun,
            Canvas = config.DrawRegion,
            CurrentColorPoint = config.RecreateCurrentColorPoint,
            SelectionAttempts = Math.Clamp(config.RecreateSelectionAttempts, 1, 10),
            StrokeAttempts = Math.Clamp(config.RecreateStrokeAttempts, 1, 10),
            FocusGuard = config.RecreateFocusGuard,
            Resume = config.RecreateResume,
            CheckpointEveryStrokes = Math.Clamp(config.RecreateCheckpointEvery, 1, 500)
        };
    }

    /// <summary>Canonicalises the palette colour space to one of CIELAB, RGB, OKLAB.</summary>
    public static string NormalizeColorSpace(string? value)
        => (value ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            "RGB" or "SRGB" => "RGB",
            "OKLAB" => "OKLAB",
            _ => "CIELAB"
        };

    public static StrokeOrder ParseStrokeOrder(string? value)
        => (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "nearest" or "nearest-neighbor" or "nearest-neighbour" or "nearest neighbor" or "nearest neighbour"
                => StrokeOrder.NearestNeighbor,
            _ => StrokeOrder.Serpentine
        };

    /// <summary>
    /// Rebuilds the calibration from the active configuration.  Used by the run path and by the
    /// profile save/apply path so both read exactly the same points.
    /// </summary>
    public static PickerCalibration BuildCalibration(AutoDrawConfig config, string pickerType)
    {
        ArgumentNullException.ThrowIfNull(config);
        var definition = PickerRegistry.Require(pickerType);
        var calibration = new PickerCalibration(definition.Id);

        foreach (var requirement in definition.Points)
        {
            var point = requirement.Key switch
            {
                PickerPoints.WheelCenter => config.WheelCenter,
                PickerPoints.WheelEdge => config.WheelEdge,
                PickerPoints.WheelBright => config.WheelBright,
                PickerPoints.WheelDark => config.WheelDark,
                PickerPoints.BoxHueStart => config.BoxHueStart,
                PickerPoints.BoxHueEnd => config.BoxHueEnd,
                PickerPoints.BoxTopLeft => config.BoxTopLeft,
                PickerPoints.BoxBottomRight => config.BoxBottomRight,
                _ => null
            };

            if (point is { } value) calibration.Set(requirement.Key, value);
        }

        return calibration;
    }

    /// <summary>Writes a calibration back into a configuration snapshot.</summary>
    public static void ApplyCalibration(AutoDrawConfig config, PickerCalibration calibration)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(calibration);

        foreach (var pair in calibration.Points)
        {
            switch (pair.Key)
            {
                case PickerPoints.WheelCenter: config.WheelCenter = pair.Value; break;
                case PickerPoints.WheelEdge: config.WheelEdge = pair.Value; break;
                case PickerPoints.WheelBright: config.WheelBright = pair.Value; break;
                case PickerPoints.WheelDark: config.WheelDark = pair.Value; break;
                case PickerPoints.BoxHueStart: config.BoxHueStart = pair.Value; break;
                case PickerPoints.BoxHueEnd: config.BoxHueEnd = pair.Value; break;
                case PickerPoints.BoxTopLeft: config.BoxTopLeft = pair.Value; break;
                case PickerPoints.BoxBottomRight: config.BoxBottomRight = pair.Value; break;
            }
        }

        config.PickerType = calibration.PickerType;
        config.ColorPickingMethod = calibration.PickerType.Equals("Box", StringComparison.OrdinalIgnoreCase) ? "Box" : "Circle";
    }

    internal StrokePlanOptions ToStrokePlanOptions()
        => new()
        {
            AlphaThreshold = AlphaThreshold,
            BrushSize = BrushSize,
            Order = StrokeOrder,
            Start = new ScreenPoint(Canvas.Left, Canvas.Top)
        };

    /// <summary>Human readable colour for logs and status messages.</summary>
    public static string ColorText(SKColor color)
        => $"#{color.Red:X2}{color.Green:X2}{color.Blue:X2}";

    /// <summary>Formats a coordinate pair for status text.</summary>
    public static string PointText(ScreenPoint point)
        => string.Create(CultureInfo.InvariantCulture, $"({point.X}, {point.Y})");
}
