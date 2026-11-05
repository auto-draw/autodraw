using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autodraw.Configuration;
using Autodraw.Painting.Recreate;
using SkiaSharp;
using Xunit;

namespace Autodraw.Tests;

public class CanvasMapperTests
{
    [Fact]
    public void Constructor_AspectFitsAndCentresTheImage()
    {
        var mapper = new CanvasMapper(10, 10, new ScreenRect(0, 0, 19, 9));

        Assert.Equal(1, mapper.Scale);
        Assert.Equal(5, mapper.OffsetX);
        Assert.Equal(0, mapper.OffsetY);
        Assert.Equal(new ScreenPoint(5, 0), mapper.Map(0, 0));
    }

    [Fact]
    public void Map_ClampsToTheSelectedCanvas()
    {
        var mapper = new CanvasMapper(100, 100, new ScreenRect(10, 20, 59, 69));

        var outside = mapper.Map(-10_000, 10_000);

        Assert.InRange(outside.X, 10, 59);
        Assert.InRange(outside.Y, 20, 69);
    }

    [Fact]
    public void MapRun_KeepsBothEndpointsInsideTheCanvas()
    {
        var mapper = new CanvasMapper(4, 3, new ScreenRect(100, 200, 109, 209));

        var (start, end) = mapper.MapRun(1, 0, 3);

        Assert.InRange(start.X, 100, 109);
        Assert.InRange(end.X, 100, 109);
        Assert.True(start.X <= end.X);
        Assert.True(start.Y == end.Y);
    }
}

public class StrokePlannerTests
{
    [Fact]
    public void Plan_BuildsOneRunPerOpaqueRowAndUsesSerpentineOrder()
    {
        using var mask = RecreateTestData.Mask(4, 3, (_, y) => y < 2);
        var mapper = new CanvasMapper(4, 3, new ScreenRect(0, 0, 3, 2));

        var strokes = StrokePlanner.Plan(mask, mapper, new StrokePlanOptions
        {
            AlphaThreshold = 200,
            BrushSize = 1,
            Order = StrokeOrder.Serpentine
        });

        Assert.Equal(2, strokes.Count);
        Assert.True(strokes[0].Start.X < strokes[0].End.X);
        Assert.True(strokes[1].Start.X > strokes[1].End.X);
        Assert.Equal(strokes[0].Start.Y, strokes[0].End.Y);
        Assert.Equal(strokes[1].Start.Y, strokes[1].End.Y);
    }

    [Fact]
    public void Plan_BrushSizeSkipsRowsThatWouldBeRepainted()
    {
        using var mask = RecreateTestData.Mask(4, 4, (_, _) => true);
        var mapper = new CanvasMapper(4, 4, new ScreenRect(0, 0, 3, 3));

        var strokes = StrokePlanner.Plan(mask, mapper, new StrokePlanOptions
        {
            AlphaThreshold = 200,
            BrushSize = 2,
            Order = StrokeOrder.Serpentine
        });

        Assert.Equal(2, strokes.Count);
    }

    [Fact]
    public void Plan_NearestNeighborCanReverseARunToStartAtTheCloserEnd()
    {
        using var mask = RecreateTestData.Mask(6, 2, (_, _) => true);
        var mapper = new CanvasMapper(6, 2, new ScreenRect(0, 0, 5, 1));

        var serpentine = StrokePlanner.Plan(mask, mapper, new StrokePlanOptions
        {
            AlphaThreshold = 200,
            BrushSize = 1,
            Order = StrokeOrder.Serpentine,
            Start = new ScreenPoint(0, 0)
        });
        var nearest = StrokePlanner.Plan(mask, mapper, new StrokePlanOptions
        {
            AlphaThreshold = 200,
            BrushSize = 1,
            Order = StrokeOrder.NearestNeighbor,
            Start = new ScreenPoint(5, 0)
        });

        Assert.Equal(2, serpentine.Count);
        Assert.Equal(2, nearest.Count);
        Assert.True(nearest[0].Start.X > nearest[0].End.X);
    }

    [Fact]
    public void Plan_WithoutOpaquePixelsReturnsNoStrokes()
    {
        using var mask = RecreateTestData.Mask(4, 4, (_, _) => false);
        var mapper = new CanvasMapper(4, 4, new ScreenRect(0, 0, 3, 3));

        var strokes = StrokePlanner.Plan(mask, mapper, new StrokePlanOptions());

        Assert.Empty(strokes);
    }
}

public class ImageAnalyzerTests
{
    [Fact]
    public void Analyze_BuildsOneMaskPerPaletteColourAndOwnsThem()
    {
        using var source = new SKBitmap(4, 2, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        for (var y = 0; y < 2; y++)
        {
            source.SetPixel(0, y, new SKColor(255, 0, 0));
            source.SetPixel(1, y, new SKColor(255, 0, 0));
            source.SetPixel(2, y, new SKColor(0, 0, 255));
            source.SetPixel(3, y, new SKColor(0, 0, 255));
        }

        var options = RecreateTestData.Options(new ScreenRect(0, 0, 7, 3));
        options.PaletteSize = 2;
        options.LayerOrder = "coverage-desc";
        var config = new AutoDrawConfig { KClusters = 2, Mode = "color" };

        var result = ImageAnalyzer.Analyze(source, options, config, CancellationToken.None);

        Assert.Equal(2, result.Layers.Count);
        Assert.Equal(8, result.Layers.Sum(layer => layer.PixelCount));
        Assert.All(result.Layers, layer => Assert.False(layer.IsDisposed));
        Assert.Equal("coverage-desc", LayerOrdering.Normalize(options.LayerOrder));
        result.Dispose();
        Assert.All(result.Layers, layer => Assert.True(layer.IsDisposed));
    }

    [Fact]
    public void Analyze_AllTransparentImageReturnsEmptyResult()
    {
        using var source = new SKBitmap(2, 2, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        var options = RecreateTestData.Options(new ScreenRect(0, 0, 1, 1));
        options.AlphaThreshold = 200;
        var config = new AutoDrawConfig();

        var result = ImageAnalyzer.Analyze(source, options, config);

        Assert.Empty(result.Layers);
        result.Dispose();
    }
}

public class LayerOrderingTests
{
    private static Layer MakeLayer(SKColor color, int pixels, int order = 0)
        => new(color, new SKBitmap(1, 1), order, pixels);

    [Fact]
    public void Apply_DefaultsToCoverageDescendingAndStableTies()
    {
        var small = MakeLayer(new SKColor(1, 1, 1), 5);
        var large = MakeLayer(new SKColor(2, 2, 2), 50);
        var tie = MakeLayer(new SKColor(3, 3, 3), 50);

        var ordered = LayerOrdering.Apply(new[] { small, large, tie }, "unknown");

        Assert.Same(large, ordered[0]);
        Assert.Same(tie, ordered[1]);
        Assert.Same(small, ordered[2]);
        Assert.Equal(new[] { 0, 1, 2 }, ordered.Select(layer => layer.Order));
        foreach (var layer in ordered) layer.Dispose();
    }

    [Fact]
    public void Normalize_AcceptsCommonAliases()
    {
        Assert.Equal("coverage-desc", LayerOrdering.Normalize("Largest area first"));
        Assert.Equal("luminance-asc", LayerOrdering.Normalize("darkness-to-brightness"));
        Assert.Equal("manual", LayerOrdering.Normalize("as-is"));
        Assert.Equal("coverage-desc", LayerOrdering.Normalize("nonsense"));
    }
}

public class ColorVerifierTests
{
    [Fact]
    public void Verify_ExactMatchPassesEvenAtZeroTolerance()
    {
        var verifier = new ColorVerifier(new FixedColorSampler(new SKColor(10, 20, 30)), new ScreenPoint(4, 5), 0);

        var result = verifier.Verify(new SKColor(10, 20, 30));

        Assert.True(result.Matched);
        Assert.Equal(0, result.DeltaE, 6);
    }

    [Fact]
    public void Verify_UsesDeltaE2000Tolerance()
    {
        var verifier = new ColorVerifier(new FixedColorSampler(new SKColor(250, 0, 0)), new ScreenPoint(4, 5), 5);

        Assert.True(verifier.Verify(new SKColor(255, 0, 0)).Matched);
        Assert.False(verifier.Verify(new SKColor(0, 255, 0)).Matched);
    }

    [Fact]
    public void Verify_CaptureFailureIsReportedAsMismatch()
    {
        var verifier = new ColorVerifier(new FixedColorSampler(null), new ScreenPoint(4, 5), 10);

        var result = verifier.Verify(new SKColor(1, 2, 3));

        Assert.False(result.Matched);
        Assert.Contains("could not capture", result.Detail);
    }
}
