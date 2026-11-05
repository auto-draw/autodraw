using System;
using System.IO;
using Autodraw.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Autodraw.Tests;

public class ConfigRoundTripTests
{
    [Fact]
    public void NewConfig_DefaultsMatchReferenceValuesForImageInput()
    {
        var config = new AutoDrawConfig();

        Assert.Equal(0, config.MonitorIndex);
        Assert.Equal(new ScreenRect(607, 92, 706, 709), config.DrawRegion);
        Assert.False(config.AreaSelected);
    }

    [Fact]
    public void DefaultConfig_SerializesImageInputKeysVerbatim()
    {
        var json = JObject.Parse(JsonConvert.SerializeObject(new AutoDrawConfig()));

        Assert.Equal(0, json["monitor_index"]!.Value<int>());
        var region = Assert.IsType<JArray>(json["draw_region"]);
        Assert.Equal(new[] { 607, 92, 706, 709 }, region.Values<int>());
    }

    [Fact]
    public void DrawRegionAndMonitor_RoundTripThroughJson()
    {
        var original = new AutoDrawConfig
        {
            MonitorIndex = 2,
            DrawRegion = new ScreenRect(-100, 20, 300, 400),
            AreaSelected = true
        };

        var json = JsonConvert.SerializeObject(original);
        var loaded = JsonConvert.DeserializeObject<AutoDrawConfig>(json)!;

        Assert.Equal(2, loaded.MonitorIndex);
        Assert.Equal(new ScreenRect(-100, 20, 300, 400), loaded.DrawRegion);
        Assert.True(loaded.AreaSelected);
    }

    [Fact]
    public void MissingImageInputKeys_FallBackToDefaults()
    {
        const string json = """{ "language": "English", "mode": "color" }""";

        var loaded = JsonConvert.DeserializeObject<AutoDrawConfig>(json)!;

        Assert.Equal(0, loaded.MonitorIndex);
        Assert.Equal(new ScreenRect(607, 92, 706, 709), loaded.DrawRegion);
    }

    [Fact]
    public void UnknownKeys_AreIgnoredSilently()
    {
        const string json = """{ "monitor_index": 1, "draw_region": [1, 2, 30, 40], "not_a_real_key": true }""";

        var loaded = JsonConvert.DeserializeObject<AutoDrawConfig>(json)!;

        Assert.Equal(1, loaded.MonitorIndex);
        Assert.Equal(new ScreenRect(1, 2, 30, 40), loaded.DrawRegion);
    }

    [Fact]
    public void MalformedDrawRegion_MakesTryLoadFailWithoutThrowing()
    {
        var path = Path.Combine(Path.GetTempPath(), $"autodraw-test-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """{ "draw_region": [1, 2, 3] }""");
        try
        {
            var ok = DdConfigService.TryLoad(path, out var config, out var error);

            Assert.False(ok);
            Assert.Null(config);
            Assert.False(string.IsNullOrWhiteSpace(error));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Validate_AcceptsDefaultsAndRejectsInvalidRegionOrMonitor()
    {
        var config = new AutoDrawConfig();
        Assert.True(DdConfigService.Validate(config, out _));

        config.MonitorIndex = -1;
        Assert.False(DdConfigService.Validate(config, out _));

        config.MonitorIndex = 0;
        config.DrawRegion = new ScreenRect(10, 10, 9, 10);
        Assert.False(DdConfigService.Validate(config, out _));
    }
}
