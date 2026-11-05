using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Autodraw.Configuration;
using Autodraw.Painting.Recreate;
using SkiaSharp;

namespace Autodraw;

/// <summary>
/// Calibration entry points and the legacy draw-path colour driver.
///
/// The geometry and the clicking live in <see cref="Painting.Recreate"/> so the legacy luminance
/// path and the colour-layer engine share exactly one implementation per picker.  This class only
/// maps captured points into <c>dd.json</c> and resolves the active picker through
/// <see cref="PickerRegistry"/>.
/// </summary>
public static class ColorPicker
{
    public static string GetUsageInstructions() => string.Join(Environment.NewLine, new[]
    {
        "Choose a Picker Type in Settings, then click Calibrate (or press F5).",
        "A guide window tells you exactly what to click for that picker type.",
        "",
        "Color Circle Wheel: the wheel centre, the rim point where RED is, then the bright and",
        "dark ends of the brightness slider.",
        "Color Box: the hue slider start, the hue slider end, then the colour box's top-left and",
        "bottom-right corners.",
        "",
        "AutoDraw minimizes during capture; press ESC or Cancel in the guide window to abort.",
        "Everything you configure saves automatically."
    });

    // ---------------------------------------------------------------------
    // Calibration write-back
    // ---------------------------------------------------------------------

    /// <summary>Persists a wheel calibration: centre, red rim point, bright end, dark end.</summary>
    public static void SaveWheelCalibration(Vector2 center, Vector2 rim, Vector2 brightnessTop, Vector2 brightnessBottom)
    {
        var calibration = new PickerCalibration("Wheel")
            .Set(PickerPoints.WheelCenter, ToPoint(center))
            .Set(PickerPoints.WheelEdge, ToPoint(rim))
            .Set(PickerPoints.WheelBright, ToPoint(brightnessTop))
            .Set(PickerPoints.WheelDark, ToPoint(brightnessBottom));

        SaveCalibration(calibration);
    }

    /// <summary>Persists a color box calibration: hue strip start/end, then the two box corners.</summary>
    public static void SaveBoxFullCalibration(Vector2 hueStart, Vector2 hueEnd, Vector2 topLeft, Vector2 bottomRight)
    {
        var calibration = new PickerCalibration("Box")
            .Set(PickerPoints.BoxHueStart, ToPoint(hueStart))
            .Set(PickerPoints.BoxHueEnd, ToPoint(hueEnd))
            .Set(PickerPoints.BoxTopLeft, ToPoint(topLeft))
            .Set(PickerPoints.BoxBottomRight, ToPoint(bottomRight));

        SaveCalibration(calibration);
    }

    /// <summary>
    /// Validates a captured calibration and writes it into the active configuration.  Invalid
    /// geometry (a collapsed wheel, a zero-length hue strip, a point off-screen) is rejected so a
    /// broken calibration can never reach the click math.
    /// </summary>
    public static bool SaveCalibration(PickerCalibration calibration)
        => TrySaveCalibration(calibration, out _);

    /// <summary>As <see cref="SaveCalibration"/>, but returns the user-facing rejection reason.</summary>
    public static bool TrySaveCalibration(PickerCalibration calibration, out string error)
    {
        ArgumentNullException.ThrowIfNull(calibration);
        error = string.Empty;

        var definition = PickerRegistry.Find(calibration.PickerType);
        if (definition is null)
        {
            error = $"Picker '{calibration.PickerType}' is not registered in this build.";
            Utils.Log("Calibration rejected: " + error);
            return false;
        }

        var validation = definition.Validate(calibration, ScreenGeometry.TryGetVirtualScreenBounds());
        if (validation is not null)
        {
            error = validation;
            Utils.Log("Calibration rejected: " + error);
            return false;
        }

        var config = DdConfigService.Current;
        RecreateOptions.ApplyCalibration(config, calibration);
        if (!DdConfigService.TryApply(config, out var applyError))
        {
            error = applyError;
            Utils.Log("Color calibration write-back rejected: " + applyError);
            return false;
        }

        return true;
    }

    private static ScreenPoint ToPoint(Vector2 point)
        => new((int)Math.Round(point.X), (int)Math.Round(point.Y));

    /// <summary>True when the configured picker type has every coordinate it needs to select a colour.</summary>
    public static bool ConfiguredTypeIsReady()
    {
        var config = DdConfigService.Current;
        var definition = PickerRegistry.Find(config.PickerType);
        if (definition is null) return false;

        var calibration = RecreateOptions.BuildCalibration(config, definition.Id);
        return definition.Validate(calibration, ScreenGeometry.TryGetVirtualScreenBounds()) is null;
    }

    /// <summary>True when a draw can select colours right now.</summary>
    public static bool CanSelectCurrently => ConfiguredTypeIsReady();

    // ---------------------------------------------------------------------
    // Legacy draw-path dispatch
    // ---------------------------------------------------------------------

    /// <summary>
    /// Selects a colour in the target game's picker for the legacy luminance draw path.  The
    /// colour-layer engine calls the same picker implementations directly.
    /// </summary>
    public static async Task SelectColor(string hex)
    {
        var config = DdConfigService.Current;
        var definition = PickerRegistry.Require(config.PickerType);
        var calibration = RecreateOptions.BuildCalibration(config, definition.Id);
        var validation = definition.Validate(calibration, ScreenGeometry.TryGetVirtualScreenBounds());
        if (validation is not null)
            throw new ColorSelectionException("Color picker is not calibrated: " + validation + ".");

        var input = InputSimulatorFactory.Create();
        var options = new RecreateOptions
        {
            PickerType = definition.Id,
            WheelClockwise = string.Equals(config.WheelDirection, "CW", StringComparison.OrdinalIgnoreCase),
            PickerStepDelayMs = Math.Max(0, (int)Math.Round(config.DelayInput * 1000d)),
            SelectionAttempts = Math.Clamp(config.RecreateSelectionAttempts, 1, 10)
        };

        var color = SKColor.Parse(hex);
        if (config.EnableMacros && config.OpenPickerCoordinate is { } open)
        {
            await input.ClickAsync(open, CancellationToken.None).ConfigureAwait(false);
            await options.DelayAsync(Math.Max(0, (int)Math.Round(config.DelayOpen * 1000d)), CancellationToken.None)
                .ConfigureAwait(false);
        }

        var picker = definition.Create(input, calibration, options, Utils.Log);
        await picker.SelectColorAsync(color, CancellationToken.None).ConfigureAwait(false);

        if (config.EnableMacros && config.ClosePickerCoordinate is { } close)
        {
            await options.DelayAsync(Math.Max(0, (int)Math.Round(config.DelayClose * 1000d)), CancellationToken.None)
                .ConfigureAwait(false);
            await input.ClickAsync(close, CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>Formats a colour the way the target applications expect to see it.</summary>
    public static string FormatHex(SKColor color)
        => string.Create(CultureInfo.InvariantCulture, $"#{color.Red:X2}{color.Green:X2}{color.Blue:X2}");
}
