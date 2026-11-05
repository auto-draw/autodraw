using System.Collections.Generic;
using System.Linq;
using Autodraw.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Autodraw.Tests;

public class RecreateConfigTests
{
    [Fact]
    public void Defaults_KeepLegacyDrawingAndSafeRecreateValues()
    {
        var config = new AutoDrawConfig();

        Assert.Equal(LayerMaskMode.LuminanceThreshold, config.MaskMode);
        Assert.Equal("coverage-desc", config.RecreateLayerOrder);
        Assert.Equal("serpentine", config.RecreateStrokeOrder);
        Assert.True(config.RecreateVerify);
        Assert.Equal(8, config.RecreateVerifyTolerance);
        Assert.Equal(2, config.RecreateVerifyRetries);
        Assert.Equal(2, config.RecreateSelectionAttempts);
        Assert.Equal(2, config.RecreateStrokeAttempts);
        Assert.False(config.RecreateDryRun);
        Assert.Equal(200, config.RecreateAlphaThreshold);
        Assert.Null(config.RecreateCurrentColorPoint);

        // The two pickers that ship: the wheel is the default because most games use one.
        Assert.Equal("Wheel", config.PickerType);
        Assert.Equal(string.Empty, config.PickerProfile);
        Assert.Equal("CCW", config.WheelDirection);
        Assert.Null(config.WheelCenter);
        Assert.Null(config.WheelEdge);
        Assert.Null(config.WheelBright);
        Assert.Null(config.WheelDark);
        Assert.Null(config.BoxHueStart);
        Assert.Null(config.BoxHueEnd);
        Assert.Null(config.BoxTopLeft);
        Assert.Null(config.BoxBottomRight);

        // Safety defaults: a run may not click into another window and must be resumable.
        Assert.True(config.RecreateFocusGuard);
        Assert.True(config.RecreateResume);
        Assert.Equal(25, config.RecreateCheckpointEvery);
    }

    [Fact]
    public void LegacyJson_WithoutRecreateKeys_UsesLegacySafeDefaults()
    {
        const string json = """{ "mode": "color", "k_clusters": 12 }""";

        var config = JsonConvert.DeserializeObject<AutoDrawConfig>(json)!;

        Assert.Equal(LayerMaskMode.LuminanceThreshold, config.MaskMode);
        Assert.Equal("Wheel", config.PickerType);
        Assert.Equal("coverage-desc", config.RecreateLayerOrder);
        Assert.Equal("serpentine", config.RecreateStrokeOrder);
        Assert.True(config.RecreateFocusGuard);
        Assert.True(config.RecreateResume);
        Assert.Equal(25, config.RecreateCheckpointEvery);
    }

    [Fact]
    public void RecreateSettings_RoundTripThroughJson()
    {
        var original = new AutoDrawConfig
        {
            MaskMode = LayerMaskMode.ColorLayers,
            PickerType = "Box",
            PickerProfile = "Roblox Studio",
            RecreateLayerOrder = "luminance-asc",
            RecreateStrokeOrder = "nearest",
            RecreateStrokeDelayMs = 25,
            RecreateMoveStepPixels = 4,
            RecreateVerify = false,
            RecreateVerifyTolerance = 12.5,
            RecreateVerifyRetries = 4,
            RecreateSelectionAttempts = 3,
            RecreateStrokeAttempts = 5,
            RecreateDryRun = true,
            RecreateAlphaThreshold = 120,
            RecreateFocusGuard = false,
            RecreateResume = false,
            RecreateCheckpointEvery = 40,
            RecreateCurrentColorPoint = new ScreenPoint(11, 22),
            BoxHueStart = new ScreenPoint(1, 2),
            BoxHueEnd = new ScreenPoint(3, 4),
            BoxTopLeft = new ScreenPoint(5, 6),
            BoxBottomRight = new ScreenPoint(7, 8),
            WheelDirection = "CW"
        };

        var json = JsonConvert.SerializeObject(original);
        var loaded = JsonConvert.DeserializeObject<AutoDrawConfig>(json)!;

        Assert.Equal(LayerMaskMode.ColorLayers, loaded.MaskMode);
        Assert.Equal("Box", loaded.PickerType);
        Assert.Equal("Roblox Studio", loaded.PickerProfile);
        Assert.Equal("luminance-asc", loaded.RecreateLayerOrder);
        Assert.Equal("nearest", loaded.RecreateStrokeOrder);
        Assert.Equal(25, loaded.RecreateStrokeDelayMs);
        Assert.Equal(4, loaded.RecreateMoveStepPixels);
        Assert.False(loaded.RecreateVerify);
        Assert.Equal(12.5, loaded.RecreateVerifyTolerance);
        Assert.Equal(4, loaded.RecreateVerifyRetries);
        Assert.Equal(3, loaded.RecreateSelectionAttempts);
        Assert.Equal(5, loaded.RecreateStrokeAttempts);
        Assert.True(loaded.RecreateDryRun);
        Assert.Equal(120, loaded.RecreateAlphaThreshold);
        Assert.False(loaded.RecreateFocusGuard);
        Assert.False(loaded.RecreateResume);
        Assert.Equal(40, loaded.RecreateCheckpointEvery);
        Assert.Equal(new ScreenPoint(11, 22), loaded.RecreateCurrentColorPoint);
        Assert.Equal(new ScreenPoint(1, 2), loaded.BoxHueStart);
        Assert.Equal(new ScreenPoint(3, 4), loaded.BoxHueEnd);
        Assert.Equal(new ScreenPoint(5, 6), loaded.BoxTopLeft);
        Assert.Equal(new ScreenPoint(7, 8), loaded.BoxBottomRight);
        Assert.Equal("CW", loaded.WheelDirection);
    }

    [Fact]
    public void WheelPoints_RoundTripThroughJson()
    {
        var original = new AutoDrawConfig
        {
            PickerType = "Wheel",
            WheelCenter = new ScreenPoint(400, 300),
            WheelEdge = new ScreenPoint(500, 300),
            WheelBright = new ScreenPoint(600, 320),
            WheelDark = new ScreenPoint(600, 420)
        };

        var loaded = JsonConvert.DeserializeObject<AutoDrawConfig>(JsonConvert.SerializeObject(original))!;

        Assert.Equal(new ScreenPoint(400, 300), loaded.WheelCenter);
        Assert.Equal(new ScreenPoint(500, 300), loaded.WheelEdge);
        Assert.Equal(new ScreenPoint(600, 320), loaded.WheelBright);
        Assert.Equal(new ScreenPoint(600, 420), loaded.WheelDark);
    }

    [Fact]
    public void MaskMode_AcceptsBooleanAndStringAliases()
    {
        var boolean = JsonConvert.DeserializeObject<AutoDrawConfig>("""{ "mask_mode": true }""")!;
        var text = JsonConvert.DeserializeObject<AutoDrawConfig>("""{ "mask_mode": "colour-layers" }""")!;
        var legacy = JsonConvert.DeserializeObject<AutoDrawConfig>("""{ "mask_mode": false }""")!;

        Assert.Equal(LayerMaskMode.ColorLayers, boolean.MaskMode);
        Assert.Equal(LayerMaskMode.ColorLayers, text.MaskMode);
        Assert.Equal(LayerMaskMode.LuminanceThreshold, legacy.MaskMode);
    }

    [Fact]
    public void Pickers_MigrateLegacyValuesAndKeepBox()
    {
        var legacyHex = JsonConvert.DeserializeObject<AutoDrawConfig>("""{ "picker_type": "Hex field" }""")!;
        var legacyRgb = JsonConvert.DeserializeObject<AutoDrawConfig>("""{ "picker_type": "RGB inputs" }""")!;
        var box = JsonConvert.DeserializeObject<AutoDrawConfig>("""{ "picker_type": "Box" }""")!;
        var missing = JsonConvert.DeserializeObject<AutoDrawConfig>("""{ }""")!;

        DdConfigService.Normalize(legacyHex);
        DdConfigService.Normalize(legacyRgb);
        DdConfigService.Normalize(box);
        DdConfigService.Normalize(missing);

        Assert.Equal("Wheel", legacyHex.PickerType);
        Assert.Equal("Wheel", legacyRgb.PickerType);
        Assert.Equal("Box", box.PickerType);
        Assert.Equal("Box", box.ColorPickingMethod);
        Assert.Equal("Wheel", missing.PickerType);
        Assert.Equal("Circle", missing.ColorPickingMethod);
    }

    [Fact]
    public void StrokeOrder_AcceptsAliases()
    {
        var config = JsonConvert.DeserializeObject<AutoDrawConfig>(
            """{ "recreate_stroke_order": "nearest-neighbor" }""")!;

        Assert.Equal("nearest-neighbor", config.RecreateStrokeOrder);
        Assert.Equal(
            Autodraw.Painting.Recreate.StrokeOrder.NearestNeighbor,
            Autodraw.Painting.Recreate.RecreateOptions.ParseStrokeOrder(config.RecreateStrokeOrder));
    }

    [Fact]
    public void Normalize_CanonicalizesRecreateAliases()
    {
        var config = new AutoDrawConfig
        {
            RecreateLayerOrder = "Largest area first",
            RecreateStrokeOrder = "nearest-neighbour",
            PickerType = " box ",
            WheelDirection = "cw",
            PickerProfile = null!
        };

        DdConfigService.Normalize(config);

        Assert.Equal("coverage-desc", config.RecreateLayerOrder);
        Assert.Equal("nearest", config.RecreateStrokeOrder);
        Assert.Equal("Box", config.PickerType);
        Assert.Equal("CW", config.WheelDirection);
        Assert.Equal(string.Empty, config.PickerProfile);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(513)]
    public void Validate_RejectsOutOfRangeMoveStep(int step)
    {
        var config = new AutoDrawConfig { RecreateMoveStepPixels = step };

        Assert.False(DdConfigService.Validate(config, out var error));
        Assert.Contains("move step", error, System.StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(501)]
    public void Validate_RejectsOutOfRangeCheckpointInterval(int every)
    {
        var config = new AutoDrawConfig { RecreateCheckpointEvery = every };

        Assert.False(DdConfigService.Validate(config, out var error));
        Assert.Contains("checkpoint", error, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_RejectsUnknownRecreateOrdersAndModes()
    {
        var order = new AutoDrawConfig { RecreateLayerOrder = "sideways" };
        var stroke = new AutoDrawConfig { RecreateStrokeOrder = "diagonal" };
        var mode = new AutoDrawConfig { MaskMode = (LayerMaskMode)99 };
        var picker = new AutoDrawConfig { PickerType = "Eyedropper" };
        var colourSpace = new AutoDrawConfig { ColorDistanceMethod = "XYZ" };

        Assert.False(DdConfigService.Validate(order, out _));
        Assert.False(DdConfigService.Validate(stroke, out _));
        Assert.False(DdConfigService.Validate(mode, out _));
        Assert.False(DdConfigService.Validate(picker, out _));
        Assert.False(DdConfigService.Validate(colourSpace, out _));
    }

    [Fact]
    public void SerializedMaskMode_IsReadableAndDefaultsToLegacy()
    {
        var json = JObject.Parse(JsonConvert.SerializeObject(new AutoDrawConfig()));

        Assert.Equal("luminance-threshold", json["mask_mode"]!.Value<string>());
        Assert.Equal("Wheel", json["picker_type"]!.Value<string>());
        Assert.Equal(string.Empty, json["picker_profile"]!.Value<string>());
        Assert.Equal("coverage-desc", json["recreate_layer_order"]!.Value<string>());
        Assert.Equal("serpentine", json["recreate_stroke_order"]!.Value<string>());
        Assert.True(json["recreate_focus_guard"]!.Value<bool>());
        Assert.True(json["recreate_resume"]!.Value<bool>());
        Assert.Equal(25, json["recreate_checkpoint_every"]!.Value<int>());
    }

    [Fact]
    public void LegacyColourModeKeys_AreIgnoredInsteadOfFailing()
    {
        const string json = """
        {
          "recreate_color_mode": "custom",
          "recreate_custom_confirm": [10, 20],
          "palette_slots": [ { "point": [1, 2], "color": "#AABBCC" } ],
          "captured_palette": [ "#AABBCC" ]
        }
        """;

        var config = JsonConvert.DeserializeObject<AutoDrawConfig>(json)!;

        Assert.NotNull(config);
        Assert.Equal("Wheel", config.PickerType);
        Assert.Null(config.RecreateCurrentColorPoint);
    }

    [Fact]
    public void LegacyDdJson_WithRetiredPickerType_StillLoads()
    {
        // dd.json written by a build that still shipped the HEX / RGB / HSV pickers.  Loading it
        // must migrate the picker to the wheel instead of discarding the whole file, otherwise
        // every existing user loses their region, delays and palette settings on upgrade.
        const string json = """
        {
          "picker_type": "hex",
          "interval": 12300000,
          "k_clusters": 32
        }
        """;

        var path = Path.Combine(Path.GetTempPath(), $"dd-legacy-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json, new System.Text.UTF8Encoding(false));
        try
        {
            Assert.True(DdConfigService.TryLoad(path, out var config, out var error), error);
            Assert.NotNull(config);
            Assert.Equal("Wheel", config!.PickerType);
            Assert.Equal(12300000, config.Interval);
            Assert.Equal(32, config.KClusters);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Profiles_DoNotLeakBetweenConfigInstances()
    {
        var first = new AutoDrawConfig { PickerProfile = "Alpha" };
        var second = new AutoDrawConfig();

        first.PickerProfile = "Beta";

        Assert.Equal("Beta", first.PickerProfile);
        Assert.Equal(string.Empty, second.PickerProfile);
    }

    [Fact]
    public void CalibrationPoints_AreIndependentPerPicker()
    {
        var config = new AutoDrawConfig
        {
            WheelCenter = new ScreenPoint(10, 10),
            BoxHueStart = new ScreenPoint(20, 20)
        };
        var calibration = Autodraw.Painting.Recreate.RecreateOptions.BuildCalibration(config, "Wheel");

        Assert.Equal(new ScreenPoint(10, 10), calibration.Point("wheel.center"));
        Assert.Null(calibration.Point("box.hue_start"));
        Assert.Single(calibration.Points, pair => pair.Key == "wheel.center");
    }

    [Fact]
    public void PaletteColorSpace_DefaultsToCielabAndNormalizesAliases()
    {
        Assert.Equal("CIELAB", Autodraw.Painting.Recreate.RecreateOptions.NormalizeColorSpace(null));
        Assert.Equal("CIELAB", Autodraw.Painting.Recreate.RecreateOptions.NormalizeColorSpace("lab"));
        Assert.Equal("RGB", Autodraw.Painting.Recreate.RecreateOptions.NormalizeColorSpace("sRGB"));
        Assert.Equal("OKLAB", Autodraw.Painting.Recreate.RecreateOptions.NormalizeColorSpace("oklab"));
    }

    [Fact]
    public void FromConfig_SnapshotsTheNewSafetyFields()
    {
        var config = new AutoDrawConfig
        {
            PickerType = "Box",
            PickerProfile = "Some Game",
            RecreateFocusGuard = false,
            RecreateResume = false,
            RecreateCheckpointEvery = 7,
            RecreateSelectionAttempts = 4,
            RecreateStrokeAttempts = 3,
            RecreateMoveStepPixels = 6,
            RecreateStrokeDelayMs = 12,
            RecreateAlphaThreshold = 99,
            ColorDistanceMethod = "OKLAB"
        };

        var options = Autodraw.Painting.Recreate.RecreateOptions.FromConfig(config);

        Assert.Equal("Box", options.PickerType);
        Assert.False(options.FocusGuard);
        Assert.False(options.Resume);
        Assert.Equal(7, options.CheckpointEveryStrokes);
        Assert.Equal(4, options.SelectionAttempts);
        Assert.Equal(3, options.StrokeAttempts);
        Assert.Equal(6, options.MoveStepPixels);
        Assert.Equal(12, options.StrokeDelayMs);
        Assert.Equal(99, options.AlphaThreshold);
        Assert.Equal("OKLAB", options.PaletteColorSpace);
    }

    [Fact]
    public void FromConfig_ClampsOutOfRangeValues()
    {
        var config = new AutoDrawConfig
        {
            KClusters = 500,
            BrushSize = 0,
            RecreateMoveStepPixels = 0,
            RecreateCheckpointEvery = 0,
            RecreateSelectionAttempts = 0,
            RecreateStrokeAttempts = 99,
            RecreateVerifyRetries = 99,
            RecreateVerifyTolerance = 400
        };

        var options = Autodraw.Painting.Recreate.RecreateOptions.FromConfig(config);

        Assert.Equal(64, options.PaletteSize);
        Assert.Equal(1, options.BrushSize);
        Assert.Equal(1, options.MoveStepPixels);
        Assert.Equal(1, options.CheckpointEveryStrokes);
        Assert.Equal(1, options.SelectionAttempts);
        Assert.Equal(10, options.StrokeAttempts);
        Assert.Equal(10, options.VerifyRetries);
        Assert.Equal(100, options.VerifyTolerance);
    }

    [Fact]
    public void ConfigKeys_StaySnakeCase()
    {
        var json = JObject.Parse(JsonConvert.SerializeObject(new AutoDrawConfig()));

        Assert.NotNull(json["picker_type"]);
        Assert.NotNull(json["picker_profile"]);
        Assert.NotNull(json["wheel_center"]);
        Assert.NotNull(json["wheel_edge"]);
        Assert.NotNull(json["wheel_bright"]);
        Assert.NotNull(json["wheel_dark"]);
        Assert.NotNull(json["wheel_dir"]);
        Assert.NotNull(json["box_hue_start"]);
        Assert.NotNull(json["box_hue_end"]);
        Assert.NotNull(json["box_tl"]);
        Assert.NotNull(json["box_br"]);
        Assert.NotNull(json["recreate_current_color"]);
        Assert.NotNull(json["recreate_focus_guard"]);
        Assert.NotNull(json["recreate_resume"]);
        Assert.NotNull(json["recreate_checkpoint_every"]);
        Assert.NotNull(json["color_distance_method"]);
    }

    [Fact]
    public void LegacyProfilesList_IsNotRequiredForValidation()
    {
        var config = new AutoDrawConfig { PickerProfile = "Anything" };

        Assert.True(DdConfigService.Validate(config, out var error), error);
    }

    [Fact]
    public void RecreateOverrides_DoNotMutateTheOriginalConfig()
    {
        var config = new AutoDrawConfig { RecreateFocusGuard = true, PickerType = "Wheel" };
        var options = Autodraw.Painting.Recreate.RecreateOptions.FromConfig(config);

        options.FocusGuard = false;
        options.PickerType = "Box";

        Assert.True(config.RecreateFocusGuard);
        Assert.Equal("Wheel", config.PickerType);
    }

    [Fact]
    public void PickerRegistry_ContainsExactlyTheTwoSupportedPickers()
    {
        var ids = Autodraw.Painting.Recreate.PickerRegistry.All
            .Select(definition => definition.Id)
            .OrderBy(id => id)
            .ToArray();

        Assert.Equal(new[] { "Box", "Wheel" }, ids);
        Assert.True(Autodraw.Painting.Recreate.PickerRegistry.Supports("Wheel"));
        Assert.True(Autodraw.Painting.Recreate.PickerRegistry.Supports("box"));
        Assert.False(Autodraw.Painting.Recreate.PickerRegistry.Supports("Hex"));
    }

    [Fact]
    public void PickerCalibration_FromCapturedRejectsCountMismatch()
    {
        var keys = new[] { "wheel.center", "wheel.edge" };
        var points = new List<ScreenPoint> { new(1, 2) };

        var error = Assert.Throws<System.ArgumentException>(
            () => Autodraw.Painting.Recreate.PickerCalibration.FromCaptured("Wheel", keys, points));

        Assert.Contains("2", error.Message);
    }
}
