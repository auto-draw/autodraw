using System;
using Autodraw.Configuration;
using Autodraw.ImageInput;
using Xunit;

namespace Autodraw.Tests;

public class RegionCaptureMathTests
{
    private static MonitorInfo Monitor(double scaling = 1.0, int left = 0, int top = 0) =>
        new(0, left, top, 1920, 1080, scaling, true);

    [Fact]
    public void FromLogicalDrag_ScaleOne_ReturnsInclusivePhysicalRect()
    {
        var rect = RegionCaptureMath.FromLogicalDrag(Monitor(), 10, 20, 110, 70);

        Assert.NotNull(rect);
        Assert.Equal(new ScreenRect(10, 20, 109, 69), rect!.Value);
        Assert.Equal(100, rect.Value.Width);
        Assert.Equal(50, rect.Value.Height);
    }

    [Fact]
    public void FromLogicalDrag_AppliesMonitorOffsetAndScaleExactlyOnce()
    {
        var monitor = Monitor(1.5, 1920, 0);

        var rect = RegionCaptureMath.FromLogicalDrag(monitor, 10, 10, 110, 60);

        Assert.NotNull(rect);
        // 10 * 1.5 = 15, 110 * 1.5 = 165 -> inclusive width 150, not 225.
        Assert.Equal(1935, rect!.Value.Left);
        Assert.Equal(15, rect.Value.Top);
        Assert.Equal(150, rect.Value.Width);
        Assert.Equal(75, rect.Value.Height);
    }

    [Fact]
    public void FromLogicalDrag_NegativeMonitorOrigin_StaysInVirtualScreenCoordinates()
    {
        var monitor = Monitor(2.0, -3840, -200);

        var rect = RegionCaptureMath.FromLogicalDrag(monitor, 5, 5, 25, 15);

        Assert.NotNull(rect);
        Assert.Equal(-3830, rect!.Value.Left);
        Assert.Equal(-190, rect.Value.Top);
        Assert.Equal(40, rect.Value.Width);
        Assert.Equal(20, rect.Value.Height);
    }

    [Fact]
    public void FromLogicalDrag_ReversedDrag_ProducesSameRect()
    {
        var forward = RegionCaptureMath.FromLogicalDrag(Monitor(), 10, 10, 110, 110);
        var reversed = RegionCaptureMath.FromLogicalDrag(Monitor(), 110, 110, 10, 10);

        Assert.Equal(forward, reversed);
    }

    [Theory]
    [InlineData(10, 10, 10, 10)]
    [InlineData(10, 10, 11, 10)]
    [InlineData(10, 10, 10, 11)]
    [InlineData(10, 10, 11.4, 11.4)]
    public void FromLogicalDrag_ZeroAreaOrTinyDrag_ReturnsNull(double x1, double y1, double x2, double y2)
    {
        Assert.Null(RegionCaptureMath.FromLogicalDrag(Monitor(), x1, y1, x2, y2));
    }

    [Fact]
    public void FromLogicalDrag_InvalidScale_FallsBackToOne()
    {
        var rect = RegionCaptureMath.FromLogicalDrag(Monitor(0), 10, 10, 30, 30);

        Assert.NotNull(rect);
        Assert.Equal(20, rect!.Value.Width);
    }

    [Fact]
    public void IsRegionInside_RejectsRegionOutsideMonitor()
    {
        // Monitor occupies physical pixels 100..2019 horizontally and 100..1179 vertically.
        var monitor = Monitor(1.0, 100, 100);

        Assert.True(RegionCaptureMath.IsRegionInside(new ScreenRect(100, 100, 2019, 1179), monitor));
        Assert.False(RegionCaptureMath.IsRegionInside(new ScreenRect(99, 100, 2019, 1179), monitor));
        Assert.False(RegionCaptureMath.IsRegionInside(new ScreenRect(100, 100, 2020, 1179), monitor));
        Assert.False(RegionCaptureMath.IsRegionInside(new ScreenRect(100, 100, 2019, 1180), monitor));
    }

    [Theory]
    [InlineData(100, 50, false)]
    [InlineData(101, 50, true)]
    [InlineData(100, 51, true)]
    public void ExceedsSurface_ReportsOversizedImagesWithoutCropping(int width, int height, bool expected)
    {
        Assert.Equal(expected, RegionCaptureMath.ExceedsSurface(width, height, new ScreenRect(0, 0, 99, 49)));
    }
}
