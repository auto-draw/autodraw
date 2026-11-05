using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autodraw.Configuration;
using Autodraw.Painting.Recreate;
using SkiaSharp;
using Xunit;

namespace Autodraw.Tests;

public class ColorMathTests
{
    [Theory]
    [InlineData(255, 0, 0, 0)]
    [InlineData(0, 255, 0, 120)]
    [InlineData(0, 0, 255, 240)]
    [InlineData(255, 255, 0, 60)]
    [InlineData(0, 255, 255, 180)]
    [InlineData(255, 0, 255, 300)]
    public void ToHsv_ReportsTheExpectedHue(int red, int green, int blue, double hue)
    {
        var hsv = ColorMath.ToHsv(new SKColor((byte)red, (byte)green, (byte)blue));

        Assert.Equal(hue, hsv.H, 6);
        Assert.Equal(1, hsv.S, 6);
        Assert.Equal(1, hsv.V, 6);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(128)]
    [InlineData(255)]
    public void NeutralColours_HaveNoHueOrSaturation(int level)
    {
        var hsv = ColorMath.ToHsv(new SKColor((byte)level, (byte)level, (byte)level));

        Assert.Equal(0, hsv.H, 6);
        Assert.Equal(0, hsv.S, 6);
        Assert.Equal(level / 255d, hsv.V, 6);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(360, 0)]
    [InlineData(-120, 240)]
    [InlineData(480, 120)]
    [InlineData(725, 5)]
    public void NormalizeHue_WrapsIntoZeroToThreeSixty(double input, double expected)
    {
        Assert.Equal(expected, ColorMath.NormalizeHue(input), 6);
    }

    [Fact]
    public void NormalizeHue_TreatsNonFiniteValuesAsZero()
    {
        Assert.Equal(0, ColorMath.NormalizeHue(double.NaN));
        Assert.Equal(0, ColorMath.NormalizeHue(double.PositiveInfinity));
    }

    [Theory]
    [InlineData(10.4, 10.6, 10, 11)]
    [InlineData(10.5, 11.5, 11, 12)]
    [InlineData(-0.4, -0.6, 0, -1)]
    public void ToPoint_RoundsAwayFromZero(double x, double y, int expectedX, int expectedY)
    {
        Assert.Equal(new ScreenPoint(expectedX, expectedY), ColorMath.ToPoint(x, y));
    }
}

public class WheelPickerMathTests
{
    private static readonly WheelGeometry Geometry = WheelPickerMath.CreateGeometry(
        center: new ScreenPoint(100, 100),
        edge: new ScreenPoint(200, 100),
        bright: new ScreenPoint(300, 150),
        dark: new ScreenPoint(300, 250),
        clockwise: true);

    private static WheelGeometry Ccw()
        => Geometry with { Clockwise = false };

    [Fact]
    public void Geometry_DerivesRadiusSliderLengthAndRedAngleFromTheCapturedPoints()
    {
        Assert.Equal(100, Geometry.Radius, 6);
        Assert.Equal(100, Geometry.SliderLength, 6);
        Assert.Equal(0, Geometry.RedAngleRadians, 6);
    }

    [Fact]
    public void Red_LandsOnTheCapturedRimPointAndTheBrightSliderEnd()
    {
        var (wheel, brightness) = WheelPickerMath.ComputeClickPoints(Geometry, new SKColor(255, 0, 0));

        Assert.Equal(new ScreenPoint(200, 100), wheel);
        Assert.Equal(new ScreenPoint(300, 150), brightness);
    }

    [Fact]
    public void Green_MovesAThirdOfATurnClockwiseFromRed()
    {
        var (wheel, _) = WheelPickerMath.ComputeClickPoints(Geometry, new SKColor(0, 255, 0));

        Assert.Equal(new ScreenPoint(50, 187), wheel);
    }

    [Fact]
    public void Green_MovesCounterClockwiseWhenTheWheelIsInverted()
    {
        var (wheel, _) = WheelPickerMath.ComputeClickPoints(Ccw(), new SKColor(0, 255, 0));

        Assert.Equal(new ScreenPoint(50, 13), wheel);
    }

    [Fact]
    public void Blue_IsTwoThirdsOfATurnClockwiseFromRed()
    {
        var (wheel, _) = WheelPickerMath.ComputeClickPoints(Geometry, new SKColor(0, 0, 255));

        Assert.Equal(new ScreenPoint(50, 13), wheel);
    }

    [Fact]
    public void Blue_ReversesWithTheRotationDirection()
    {
        var (wheel, _) = WheelPickerMath.ComputeClickPoints(Ccw(), new SKColor(0, 0, 255));

        Assert.Equal(new ScreenPoint(50, 187), wheel);
    }

    [Fact]
    public void Black_LandsOnTheCentreAndTheDarkSliderEnd()
    {
        var (wheel, brightness) = WheelPickerMath.ComputeClickPoints(Geometry, new SKColor(0, 0, 0));

        Assert.Equal(new ScreenPoint(100, 100), wheel);
        Assert.Equal(new ScreenPoint(300, 250), brightness);
    }

    [Fact]
    public void White_LandsOnTheCentreAndTheBrightSliderEnd()
    {
        var (wheel, brightness) = WheelPickerMath.ComputeClickPoints(Geometry, new SKColor(255, 255, 255));

        Assert.Equal(new ScreenPoint(100, 100), wheel);
        Assert.Equal(new ScreenPoint(300, 150), brightness);
    }

    [Fact]
    public void Gray_LandsOnTheCentreAndMidSlider()
    {
        var (wheel, brightness) = WheelPickerMath.ComputeClickPoints(Geometry, new SKColor(128, 128, 128));

        Assert.Equal(new ScreenPoint(100, 100), wheel);
        Assert.Equal(new ScreenPoint(300, 200), brightness);
    }

    [Fact]
    public async Task WheelPicker_ClicksTheDiscThenTheSlider()
    {
        var input = new RecordingInputSimulator();
        var options = RecreateTestData.Options(new ScreenRect(0, 0, 10, 10));
        var picker = new WheelColorPicker(input, Geometry, options);

        await picker.SelectColorAsync(new SKColor(0, 255, 0), CancellationToken.None);

        Assert.Equal(new[] { new ScreenPoint(50, 187), new ScreenPoint(300, 150) }, input.Clicks);
    }

    [Fact]
    public void CreateGeometryFromCalibration_NeedsEveryPoint()
    {
        var complete = CompleteWheelCalibration();

        Assert.True(WheelPickerMath.TryCreateGeometry(complete, true, out var geometry, out var error), error);
        Assert.Equal(new ScreenPoint(100, 100), geometry.Center);

        foreach (var key in new[]
                 {
                     PickerPoints.WheelCenter, PickerPoints.WheelEdge,
                     PickerPoints.WheelBright, PickerPoints.WheelDark
                 })
        {
            var partial = CompleteWheelCalibration();
            var calibration = new PickerCalibration("Wheel");
            foreach (var pair in partial.Points.Where(pair => pair.Key != key)) calibration.Set(pair.Key, pair.Value);

            Assert.False(WheelPickerMath.TryCreateGeometry(calibration, true, out _, out var missing));
            Assert.Contains("missing", missing, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void TryCreateGeometry_RejectsACollapsedRadius()
    {
        var calibration = new PickerCalibration("Wheel")
            .Set(PickerPoints.WheelCenter, new ScreenPoint(100, 100))
            .Set(PickerPoints.WheelEdge, new ScreenPoint(103, 100))
            .Set(PickerPoints.WheelBright, new ScreenPoint(300, 150))
            .Set(PickerPoints.WheelDark, new ScreenPoint(300, 250));

        Assert.False(WheelPickerMath.TryCreateGeometry(calibration, true, out _, out var error));
        Assert.Contains("radius", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryCreateGeometry_RejectsACollapsedSlider()
    {
        var calibration = new PickerCalibration("Wheel")
            .Set(PickerPoints.WheelCenter, new ScreenPoint(100, 100))
            .Set(PickerPoints.WheelEdge, new ScreenPoint(200, 100))
            .Set(PickerPoints.WheelBright, new ScreenPoint(300, 150))
            .Set(PickerPoints.WheelDark, new ScreenPoint(302, 151));

        Assert.False(WheelPickerMath.TryCreateGeometry(calibration, true, out _, out var error));
        Assert.Contains("slider", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WheelDefinition_CreateWithoutCalibrationFailsClearly()
    {
        var definition = PickerRegistry.Require("Wheel");
        var options = RecreateTestData.Options(new ScreenRect(0, 0, 10, 10));

        var error = Assert.Throws<ColorSelectionException>(
            () => definition.Create(new RecordingInputSimulator(), new PickerCalibration("Wheel"), options, null));

        Assert.Contains("not calibrated", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WheelDefinition_CreateLogsTheDerivedGeometry()
    {
        var definition = PickerRegistry.Require("Wheel");
        var options = RecreateTestData.Options(new ScreenRect(0, 0, 10, 10));
        string? log = null;

        var picker = definition.Create(new RecordingInputSimulator(), CompleteWheelCalibration(), options, message => log = message);

        Assert.IsType<WheelColorPicker>(picker);
        Assert.NotNull(log);
        Assert.Contains("radius 100", log);
        Assert.Contains("CW", log);
    }

    internal static PickerCalibration CompleteWheelCalibration()
        => new PickerCalibration("Wheel")
            .Set(PickerPoints.WheelCenter, new ScreenPoint(100, 100))
            .Set(PickerPoints.WheelEdge, new ScreenPoint(200, 100))
            .Set(PickerPoints.WheelBright, new ScreenPoint(300, 150))
            .Set(PickerPoints.WheelDark, new ScreenPoint(300, 250));
}

public class BoxPickerMathTests
{
    private static readonly BoxGeometry Geometry = BoxPickerMath.CreateGeometry(
        hueStart: new ScreenPoint(10, 10),
        hueEnd: new ScreenPoint(10, 370),
        topLeft: new ScreenPoint(100, 100),
        bottomRight: new ScreenPoint(300, 200));

    [Fact]
    public void Geometry_NormalizesEitherDiagonalCornerOrder()
    {
        var reversed = BoxPickerMath.CreateGeometry(
            new ScreenPoint(10, 10),
            new ScreenPoint(10, 370),
            new ScreenPoint(300, 200),
            new ScreenPoint(100, 100));

        Assert.Equal(100, reversed.Left);
        Assert.Equal(300, reversed.Right);
        Assert.Equal(100, reversed.Top);
        Assert.Equal(200, reversed.Bottom);
        Assert.Equal(200, reversed.Width);
        Assert.Equal(100, reversed.Height);
        Assert.Equal(360, reversed.HueLength, 6);
    }

    [Theory]
    [InlineData(255, 0, 0, 10, 300, 100)]
    [InlineData(0, 255, 0, 130, 300, 100)]
    [InlineData(0, 0, 255, 250, 300, 100)]
    [InlineData(255, 255, 255, 10, 100, 100)]
    [InlineData(0, 0, 0, 10, 100, 200)]
    [InlineData(128, 128, 128, 10, 100, 150)]
    public void ComputeClickPoints_MapsHueToTheStripAndSaturationValueToTheBox(
        int red,
        int green,
        int blue,
        int expectedHueY,
        int expectedX,
        int expectedY)
    {
        var (hue, saturationValue) = BoxPickerMath.ComputeClickPoints(
            Geometry,
            new SKColor((byte)red, (byte)green, (byte)blue));

        Assert.Equal(new ScreenPoint(10, expectedHueY), hue);
        Assert.Equal(new ScreenPoint(expectedX, expectedY), saturationValue);
    }

    [Fact]
    public void Saturation_MovesLeftToRightAndValueMovesBottomToTop()
    {
        var (_, saturated) = BoxPickerMath.ComputeClickPoints(Geometry, new SKColor(255, 0, 0));
        var (_, pale) = BoxPickerMath.ComputeClickPoints(Geometry, new SKColor(255, 128, 128));
        var (_, dark) = BoxPickerMath.ComputeClickPoints(Geometry, new SKColor(128, 0, 0));

        Assert.Equal(new ScreenPoint(300, 100), saturated);
        Assert.True(pale.X < saturated.X);
        Assert.Equal(saturated.Y, pale.Y);
        Assert.Equal(saturated.X, dark.X);
        Assert.True(dark.Y > saturated.Y);
    }

    [Fact]
    public async Task BoxPicker_ClicksTheHueStripThenTheBox()
    {
        var input = new RecordingInputSimulator();
        var options = RecreateTestData.Options(new ScreenRect(0, 0, 10, 10));
        var picker = new BoxColorPicker(input, Geometry, options);

        await picker.SelectColorAsync(new SKColor(0, 0, 255), CancellationToken.None);

        Assert.Equal(new[] { new ScreenPoint(10, 250), new ScreenPoint(300, 100) }, input.Clicks);
    }

    [Fact]
    public void TryCreateGeometry_RejectsACollapsedHueStrip()
    {
        var calibration = new PickerCalibration("Box")
            .Set(PickerPoints.BoxHueStart, new ScreenPoint(10, 10))
            .Set(PickerPoints.BoxHueEnd, new ScreenPoint(10, 14))
            .Set(PickerPoints.BoxTopLeft, new ScreenPoint(100, 100))
            .Set(PickerPoints.BoxBottomRight, new ScreenPoint(300, 200));

        Assert.False(BoxPickerMath.TryCreateGeometry(calibration, out _, out var error));
        Assert.Contains("hue strip", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryCreateGeometry_RejectsACollapsedBox()
    {
        var calibration = new PickerCalibration("Box")
            .Set(PickerPoints.BoxHueStart, new ScreenPoint(10, 10))
            .Set(PickerPoints.BoxHueEnd, new ScreenPoint(10, 370))
            .Set(PickerPoints.BoxTopLeft, new ScreenPoint(100, 100))
            .Set(PickerPoints.BoxBottomRight, new ScreenPoint(104, 104));

        Assert.False(BoxPickerMath.TryCreateGeometry(calibration, out _, out var error));
        Assert.Contains("colour box", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryCreateGeometry_RequiresEveryPoint()
    {
        var calibration = new PickerCalibration("Box")
            .Set(PickerPoints.BoxHueStart, new ScreenPoint(10, 10))
            .Set(PickerPoints.BoxHueEnd, new ScreenPoint(10, 370))
            .Set(PickerPoints.BoxTopLeft, new ScreenPoint(100, 100));

        Assert.False(BoxPickerMath.TryCreateGeometry(calibration, out _, out var error));
        Assert.Contains("missing", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BoxDefinition_CreateWithoutCalibrationFailsClearly()
    {
        var definition = PickerRegistry.Require("Box");
        var options = RecreateTestData.Options(new ScreenRect(0, 0, 10, 10));

        var error = Assert.Throws<ColorSelectionException>(
            () => definition.Create(new RecordingInputSimulator(), new PickerCalibration("Box"), options, null));

        Assert.Contains("not calibrated", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    internal static PickerCalibration CompleteBoxCalibration()
        => new PickerCalibration("Box")
            .Set(PickerPoints.BoxHueStart, new ScreenPoint(10, 10))
            .Set(PickerPoints.BoxHueEnd, new ScreenPoint(10, 370))
            .Set(PickerPoints.BoxTopLeft, new ScreenPoint(100, 100))
            .Set(PickerPoints.BoxBottomRight, new ScreenPoint(300, 200));
}

public class CalibrationValidationTests
{
    [Fact]
    public void WheelValidate_AcceptsACompleteOnScreenCalibration()
    {
        var definition = PickerRegistry.Require("Wheel");
        var calibration = WheelPickerMathTests.CompleteWheelCalibration();

        Assert.Null(definition.Validate(calibration, new ScreenRect(0, 0, 1920, 1080)));
    }

    [Fact]
    public void WheelValidate_RejectsAPointOutsideTheScreen()
    {
        var definition = PickerRegistry.Require("Wheel");
        var calibration = WheelPickerMathTests.CompleteWheelCalibration();

        var error = definition.Validate(calibration, new ScreenRect(0, 0, 50, 50));

        Assert.NotNull(error);
        Assert.Contains("outside the screen", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WheelValidate_SkipsTheBoundsCheckWhenTheScreenIsUnknown()
    {
        var definition = PickerRegistry.Require("Wheel");

        Assert.Null(definition.Validate(WheelPickerMathTests.CompleteWheelCalibration(), null));
    }

    [Fact]
    public void BoxValidate_AcceptsACompleteOnScreenCalibration()
    {
        var definition = PickerRegistry.Require("Box");

        Assert.Null(definition.Validate(
            BoxPickerMathTests.CompleteBoxCalibration(),
            new ScreenRect(0, 0, 1920, 1080)));
    }

    [Fact]
    public void BoxValidate_RejectsAPointOutsideTheScreen()
    {
        var definition = PickerRegistry.Require("Box");

        var error = definition.Validate(BoxPickerMathTests.CompleteBoxCalibration(), new ScreenRect(0, 0, 50, 50));

        Assert.NotNull(error);
        Assert.Contains("outside the screen", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_ReportsMissingPointsForBothPickers()
    {
        var emptyWheel = PickerRegistry.Require("Wheel").Validate(new PickerCalibration("Wheel"), null);
        var emptyBox = PickerRegistry.Require("Box").Validate(new PickerCalibration("Box"), null);

        Assert.NotNull(emptyWheel);
        Assert.NotNull(emptyBox);
        Assert.Contains("incomplete or degenerate", emptyWheel);
        Assert.Contains("incomplete or degenerate", emptyBox);
    }

    [Fact]
    public void PickerPoints_AreSharedBetweenTheDefinitionsAndConfig()
    {
        var wheelKeys = PickerRegistry.Require("Wheel").Points.Select(point => point.Key);
        var boxKeys = PickerRegistry.Require("Box").Points.Select(point => point.Key);

        Assert.Equal(
            new[] { PickerPoints.WheelCenter, PickerPoints.WheelEdge, PickerPoints.WheelBright, PickerPoints.WheelDark },
            wheelKeys);
        Assert.Equal(
            new[] { PickerPoints.BoxHueStart, PickerPoints.BoxHueEnd, PickerPoints.BoxTopLeft, PickerPoints.BoxBottomRight },
            boxKeys);
    }

    [Fact]
    public void BuildCalibration_ReadsTheMatchingConfigPointsAndIgnoresTheOtherPicker()
    {
        var config = new AutoDrawConfig
        {
            WheelCenter = new ScreenPoint(10, 10),
            WheelEdge = new ScreenPoint(60, 10),
            WheelBright = new ScreenPoint(80, 20),
            WheelDark = new ScreenPoint(80, 60),
            BoxHueStart = new ScreenPoint(1, 1),
            BoxHueEnd = new ScreenPoint(1, 2),
            BoxTopLeft = new ScreenPoint(3, 3),
            BoxBottomRight = new ScreenPoint(4, 4)
        };

        var wheel = RecreateOptions.BuildCalibration(config, "Wheel");
        var box = RecreateOptions.BuildCalibration(config, "Box");

        Assert.True(wheel.HasAll(wheel.Points.Keys));
        Assert.Equal(4, wheel.Points.Count);
        Assert.Equal(4, box.Points.Count);
        Assert.Equal(new ScreenPoint(10, 10), wheel.Point(PickerPoints.WheelCenter));
        Assert.Equal(new ScreenPoint(1, 1), box.Point(PickerPoints.BoxHueStart));
        Assert.Null(wheel.Point(PickerPoints.BoxHueStart));
    }

    [Fact]
    public void ApplyCalibration_WritesThePointsBackAndTracksThePickerType()
    {
        var config = new AutoDrawConfig();
        var calibration = BoxPickerMathTests.CompleteBoxCalibration();

        RecreateOptions.ApplyCalibration(config, calibration);

        Assert.Equal("Box", config.PickerType);
        Assert.Equal("Box", config.ColorPickingMethod);
        Assert.Equal(new ScreenPoint(10, 370), config.BoxHueEnd);
        Assert.Equal(new ScreenPoint(300, 200), config.BoxBottomRight);
    }

    [Fact]
    public void FromCaptured_BuildsAnOrderedCalibration()
    {
        var points = new[] { new ScreenPoint(1, 2), new ScreenPoint(3, 4) };

        var calibration = PickerCalibration.FromCaptured("Box", new[] { "box.hue_start", "box.hue_end" }, points);

        Assert.Equal(new ScreenPoint(1, 2), calibration.Point("BOX.HUE_START"));
        Assert.Equal(new ScreenPoint(3, 4), calibration.Point("box.hue_end"));
        Assert.Equal(2, calibration.Points.Count);
    }
}
