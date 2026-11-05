using System.Collections.Generic;
using System.Linq;
using Autodraw.Configuration;
using Autodraw.ImageInput;
using Xunit;

namespace Autodraw.Tests;

public class MonitorCatalogTests
{
    private static MonitorInfo Monitor(int index, int left, int top, int width, int height, double scaling, bool primary = false) =>
        new(index, left, top, width, height, scaling, primary);

    [Fact]
    public void Order_PutsPrimaryFirstAndRenumbers()
    {
        var ordered = MonitorCatalog.Order(new[]
        {
            Monitor(9, -1920, 0, 1920, 1080, 1.0),
            Monitor(4, 0, 0, 2560, 1440, 1.5, primary: true),
            Monitor(7, 2560, 0, 1920, 1080, 1.0)
        });

        Assert.Equal(new[] { 0, 1, 2 }, ordered.Select(m => m.Index));
        Assert.True(ordered[0].IsPrimary);
        Assert.Equal(0, ordered[0].Left);
        Assert.Equal(-1920, ordered[1].Left);
        Assert.Equal(2560, ordered[2].Left);
    }

    [Fact]
    public void Resolve_KnownIndex_DoesNotFallBack()
    {
        var monitors = MonitorCatalog.Order(new[]
        {
            Monitor(0, 0, 0, 1920, 1080, 1.0, primary: true),
            Monitor(1, 1920, 0, 1920, 1080, 1.0)
        });

        var resolved = MonitorCatalog.Resolve(monitors, 1, out var fellBack);

        Assert.False(fellBack);
        Assert.Equal(1920, resolved!.Value.Left);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    [InlineData(99)]
    public void Resolve_DisconnectedIndex_FallsBackToPrimaryZero(int requested)
    {
        var monitors = MonitorCatalog.Order(new[]
        {
            Monitor(0, 0, 0, 1920, 1080, 1.0, primary: true),
            Monitor(1, 1920, 0, 1920, 1080, 1.0)
        });

        var resolved = MonitorCatalog.Resolve(monitors, requested, out var fellBack);

        Assert.True(fellBack);
        Assert.Equal(0, resolved!.Value.Index);
    }

    [Fact]
    public void Resolve_NoMonitors_ReturnsNull()
    {
        var resolved = MonitorCatalog.Resolve(new List<MonitorInfo>(), 0, out var fellBack);

        Assert.Null(resolved);
        Assert.False(fellBack);
    }

    [Fact]
    public void ContainsRegion_IsInclusiveOfBothEdges()
    {
        var monitor = Monitor(0, 100, 50, 800, 600, 1.0);

        Assert.True(monitor.ContainsRegion(new ScreenRect(100, 50, 899, 649)));
        Assert.False(monitor.ContainsRegion(new ScreenRect(99, 50, 899, 649)));
        Assert.False(monitor.ContainsRegion(new ScreenRect(100, 50, 900, 649)));
    }
}
