using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Autodraw.Configuration;
using Autodraw.Painting.Profiles;
using Autodraw.Painting.Recreate;
using SkiaSharp;
using Xunit;

namespace Autodraw.Tests;

internal sealed class TempFolder : IDisposable
{
    public TempFolder()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "autodraw-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
        catch
        {
            // A leftover temp directory must never fail a test.
        }
    }
}

public class GameProfileTests : IDisposable
{
    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    private static AutoDrawConfig WheelConfig() => new()
    {
        PickerType = "Wheel",
        WheelCenter = new ScreenPoint(100, 100),
        WheelEdge = new ScreenPoint(200, 100),
        WheelBright = new ScreenPoint(300, 150),
        WheelDark = new ScreenPoint(300, 250),
        WheelDirection = "CW",
        RecreateCurrentColorPoint = new ScreenPoint(150, 150)
    };

    [Fact]
    public void Capture_RecordsThePickerPointsDirectionAndIndicator()
    {
        var profile = GameProfile.Capture("Roblox", WheelConfig());

        Assert.Equal("Roblox", profile.Name);
        Assert.Equal("Wheel", profile.PickerType);
        Assert.Equal("CW", profile.WheelDirection);
        Assert.Equal(new ScreenPoint(150, 150), profile.VerifyPoint);
        Assert.Equal(4, profile.Points.Count);
        Assert.Equal(new ScreenPoint(100, 100), profile.Points[PickerPoints.WheelCenter]);
        Assert.Equal(new ScreenPoint(300, 250), profile.Points[PickerPoints.WheelDark]);
    }

    [Fact]
    public void Capture_DefaultsBlankNamesToDefault()
    {
        Assert.Equal("Default", GameProfile.Capture("   ", WheelConfig()).Name);
        Assert.Equal("Default", GameProfile.Capture(null!, WheelConfig()).Name);
    }

    [Fact]
    public void CaptureThenApply_RestoresTheWholeCalibration()
    {
        var profile = GameProfile.Capture("Roblox", WheelConfig());
        var target = new AutoDrawConfig { PickerType = "Box" };

        Assert.True(profile.TryApply(target, out var error), error);

        Assert.Equal("Wheel", target.PickerType);
        Assert.Equal("Circle", target.ColorPickingMethod);
        Assert.Equal(new ScreenPoint(100, 100), target.WheelCenter);
        Assert.Equal(new ScreenPoint(200, 100), target.WheelEdge);
        Assert.Equal(new ScreenPoint(300, 150), target.WheelBright);
        Assert.Equal(new ScreenPoint(300, 250), target.WheelDark);
        Assert.Equal("CW", target.WheelDirection);
        Assert.Equal(new ScreenPoint(150, 150), target.RecreateCurrentColorPoint);
    }

    [Fact]
    public void Apply_RejectsAnUnsupportedPicker()
    {
        var profile = GameProfile.Capture("Roblox", WheelConfig());
        profile.PickerType = "Hex";

        Assert.False(profile.TryApply(new AutoDrawConfig(), out var error));
        Assert.Contains("does not support", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Apply_RejectsAnIncompleteCalibration()
    {
        var profile = GameProfile.Capture("Roblox", WheelConfig());
        profile.Points.Remove(PickerPoints.WheelDark);

        Assert.False(profile.TryApply(new AutoDrawConfig(), out var error));
        Assert.Contains("not usable", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Store_RoundTripsAProfileThroughDisk()
    {
        var store = new GameProfileStore(_folder.Path);
        var profile = GameProfile.Capture("Roblox Studio", WheelConfig());

        Assert.True(store.Save(profile, out var error), error);
        Assert.True(File.Exists(System.IO.Path.Combine(_folder.Path, "roblox-studio.json")));

        var loaded = store.Load("Roblox Studio");

        Assert.NotNull(loaded);
        Assert.Equal("Roblox Studio", loaded!.Name);
        Assert.Equal("Wheel", loaded.PickerType);
        Assert.Equal("CW", loaded.WheelDirection);
        Assert.Equal(new ScreenPoint(200, 100), loaded.Points[PickerPoints.WheelEdge]);
        Assert.Equal(new ScreenPoint(150, 150), loaded.VerifyPoint);
    }

    [Fact]
    public void Store_ListsProfilesSortedByName()
    {
        var store = new GameProfileStore(_folder.Path);
        Assert.True(store.Save(GameProfile.Capture("Zeta", WheelConfig()), out _));
        Assert.True(store.Save(GameProfile.Capture("Alpha", WheelConfig()), out _));

        var names = store.LoadAll().Select(profile => profile.Name).ToArray();

        Assert.Equal(new[] { "Alpha", "Zeta" }, names);
    }

    [Fact]
    public void Store_DeletesOnlyTheNamedProfile()
    {
        var store = new GameProfileStore(_folder.Path);
        store.Save(GameProfile.Capture("Alpha", WheelConfig()), out _);
        store.Save(GameProfile.Capture("Beta", WheelConfig()), out _);

        Assert.True(store.Delete("Alpha"));
        Assert.Null(store.Load("Alpha"));
        Assert.NotNull(store.Load("Beta"));
        Assert.False(store.Delete("Alpha"));
    }

    [Fact]
    public void Store_RejectsNamelessProfilesAndMissingDirectories()
    {
        var store = new GameProfileStore(_folder.Path);
        var nameless = GameProfile.Capture("Alpha", WheelConfig());
        nameless.Name = " ";

        Assert.False(store.Save(nameless, out var error));
        Assert.Contains("name", error, StringComparison.OrdinalIgnoreCase);
        Assert.Null(store.Load("Missing"));
        Assert.Empty(new GameProfileStore(System.IO.Path.Combine(_folder.Path, "absent")).LoadAll());
    }

    [Fact]
    public void Store_SkipsCorruptAndNamelessFiles()
    {
        var store = new GameProfileStore(_folder.Path);
        store.Save(GameProfile.Capture("Alpha", WheelConfig()), out _);
        File.WriteAllText(System.IO.Path.Combine(_folder.Path, "broken.json"), "{ not json");
        File.WriteAllText(System.IO.Path.Combine(_folder.Path, "nameless.json"), """{ "picker_type": "Wheel" }""");

        var loaded = store.LoadAll();

        Assert.Single(loaded);
        Assert.Equal("Alpha", loaded[0].Name);
    }

    [Theory]
    [InlineData("Roblox Studio", "roblox-studio")]
    [InlineData("  My Game!! ", "my-game")]
    [InlineData("", "default")]
    [InlineData("___", "default")]
    [InlineData("Café 2", "café-2")]
    public void Slug_ProducesFilesystemSafeKeys(string name, string expected)
    {
        Assert.Equal(expected, GameProfileStore.Slug(name));
    }

    [Fact]
    public void LoadAll_ReturnsProfilesWithMissingPointsAsAnEmptyDictionary()
    {
        File.WriteAllText(
            System.IO.Path.Combine(_folder.Path, "partial.json"),
            """{ "name": "Partial", "picker_type": "Wheel", "points": null }""");

        var profile = Assert.Single(new GameProfileStore(_folder.Path).LoadAll());

        Assert.NotNull(profile.Points);
        Assert.Empty(profile.Points);
    }
}

public class CheckpointTests : IDisposable
{
    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    private static RecreateCheckpoint Checkpoint(string fingerprint, int strokeIndex, int total) => new()
    {
        Fingerprint = fingerprint,
        LayerIndex = 0,
        StrokeIndex = strokeIndex,
        TotalStrokes = total
    };

    [Theory]
    [InlineData(0, 4, false)]
    [InlineData(1, 4, true)]
    [InlineData(3, 4, true)]
    [InlineData(4, 4, false)]
    [InlineData(5, 4, false)]
    public void IsUsable_RequiresAnInteriorStrokeIndex(int strokeIndex, int total, bool expected)
    {
        Assert.Equal(expected, Checkpoint("fp", strokeIndex, total).IsUsable(total));
    }

    [Fact]
    public void IsUsable_RejectsAnUnknownVersion()
    {
        var checkpoint = Checkpoint("fp", 1, 4);
        checkpoint.Version = 99;

        Assert.False(checkpoint.IsUsable(4));
    }

    [Fact]
    public void FileStore_SaveLoadAndClearRoundTrip()
    {
        var store = new FileCheckpointStore(_folder.Path);

        Assert.Null(store.Load("abc"));

        store.Save(Checkpoint("abc", 2, 10));
        var loaded = store.Load("abc");

        Assert.NotNull(loaded);
        Assert.Equal("abc", loaded!.Fingerprint);
        Assert.Equal(2, loaded.StrokeIndex);
        Assert.Equal(10, loaded.TotalStrokes);

        store.Clear("abc");
        Assert.Null(store.Load("abc"));
    }

    [Fact]
    public void FileStore_IgnoresACheckpointWhoseStoredFingerprintDiffers()
    {
        var store = new FileCheckpointStore(_folder.Path);
        Directory.CreateDirectory(_folder.Path);
        File.WriteAllText(
            System.IO.Path.Combine(_folder.Path, "abc.json"),
            """{ "version": 1, "fingerprint": "other", "stroke_index": 2, "total_strokes": 10 }""");

        Assert.Null(store.Load("abc"));
    }

    [Fact]
    public void FileStore_TreatsCorruptFilesAsMissing()
    {
        var store = new FileCheckpointStore(_folder.Path);
        Directory.CreateDirectory(_folder.Path);
        File.WriteAllText(System.IO.Path.Combine(_folder.Path, "abc.json"), "garbage");

        Assert.Null(store.Load("abc"));
    }

    [Fact]
    public void FileStore_IgnoresBlankFingerprints()
    {
        var store = new FileCheckpointStore(_folder.Path);

        Assert.Null(store.Load(" "));
        store.Save(Checkpoint(" ", 1, 2));
        store.Clear(" ");

        Assert.Empty(Directory.EnumerateFiles(_folder.Path));
    }

    [Fact]
    public void Fingerprint_IsStableForTheSameInputs()
    {
        using var image = RecreateTestData.Mask(4, 2, (_, _) => true);
        var options = RecreateTestData.Options(new ScreenRect(0, 0, 7, 3));
        var palette = new[] { new SKColor(255, 0, 0) };

        var first = RecreateFingerprint.Compute(image, options.Canvas, palette, options);
        var second = RecreateFingerprint.Compute(image, options.Canvas, palette, options);

        Assert.Equal(first, second);
        Assert.Equal(24, first.Length);
    }

    [Fact]
    public void Fingerprint_ChangesWhenTheImageCanvasOrPaletteChanges()
    {
        using var image = RecreateTestData.Mask(4, 2, (_, _) => true);
        using var other = RecreateTestData.Mask(4, 2, (x, _) => x < 2);
        var options = RecreateTestData.Options(new ScreenRect(0, 0, 7, 3));
        var palette = new[] { new SKColor(255, 0, 0) };

        var baseline = RecreateFingerprint.Compute(image, options.Canvas, palette, options);
        var differentImage = RecreateFingerprint.Compute(other, options.Canvas, palette, options);
        var differentCanvas = RecreateFingerprint.Compute(image, new ScreenRect(0, 0, 8, 3), palette, options);
        var differentPalette = RecreateFingerprint.Compute(image, options.Canvas, new[] { new SKColor(0, 255, 0) }, options);

        Assert.NotEqual(baseline, differentImage);
        Assert.NotEqual(baseline, differentCanvas);
        Assert.NotEqual(baseline, differentPalette);
    }

    [Fact]
    public async Task Engine_ResumesAfterTheCheckpointedStroke()
    {
        var input = new RecordingInputSimulator();
        var picker = new RecordingColorPicker();
        var options = RecreateTestData.Options(new ScreenRect(0, 0, 3, 1));
        options.Fingerprint = "fp";
        options.Resume = true;
        options.ResumeCheckpoint = Checkpoint("fp", 1, 2);
        var first = new Layer(
            new SKColor(255, 0, 0),
            RecreateTestData.Mask(4, 2, (_, y) => y == 0),
            0,
            4);
        var second = new Layer(
            new SKColor(0, 255, 0),
            RecreateTestData.Mask(4, 2, (_, y) => y == 1),
            1,
            4);

        var engine = new RecreateEngine(input, picker, null, options);
        var result = await engine.RunAsync(new[] { first, second }, CancellationToken.None);

        Assert.Equal(1, result.ResumedFromStroke);
        // DrawnStrokes is cumulative: one already painted plus the one drawn now.
        Assert.Equal(2, result.DrawnStrokes);
        Assert.Equal(new SKColor(0, 255, 0), Assert.Single(picker.Selected));
        Assert.NotEmpty(input.Moves);
        Assert.All(input.Moves, point => Assert.Equal(1, point.Y));
    }

    [Fact]
    public async Task Engine_IgnoresACheckpointWithADifferentFingerprint()
    {
        var input = new RecordingInputSimulator();
        var options = RecreateTestData.Options(new ScreenRect(0, 0, 5, 1));
        options.Fingerprint = "new-plan";
        options.Resume = true;
        options.ResumeCheckpoint = Checkpoint("old-plan", 1, 2);
        var layer = new Layer(new SKColor(255, 0, 0), RecreateTestData.Mask(6, 2, (_, _) => true), 0, 12);

        var engine = new RecreateEngine(input, new RecordingColorPicker(), null, options);
        var result = await engine.RunAsync(new[] { layer }, CancellationToken.None);

        Assert.Equal(0, result.ResumedFromStroke);
        Assert.Equal(2, result.DrawnStrokes);
        Assert.Equal(new ScreenPoint(0, 0), input.Moves[0]);
    }

    [Fact]
    public async Task Engine_WritesCheckpointsEveryNStrokesAndClearsOnCompletion()
    {
        var store = new RecordingCheckpointStore();
        var options = RecreateTestData.Options(new ScreenRect(0, 0, 11, 11));
        options.Fingerprint = "fp";
        options.Resume = true;
        options.CheckpointEveryStrokes = 1;
        var layer = new Layer(new SKColor(255, 0, 0), RecreateTestData.Mask(6, 3, (_, _) => true), 0, 18);

        var engine = new RecreateEngine(
            new RecordingInputSimulator(), new RecordingColorPicker(), null, options,
            checkpointStore: store);
        var result = await engine.RunAsync(new[] { layer }, CancellationToken.None);

        Assert.Equal(3, result.DrawnStrokes);
        Assert.Equal(new[] { 1, 2 }, store.Saved.Select(checkpoint => checkpoint.StrokeIndex).ToArray());
        Assert.All(store.Saved, checkpoint => Assert.Equal("fp", checkpoint.Fingerprint));
        Assert.All(store.Saved, checkpoint => Assert.Equal(3, checkpoint.TotalStrokes));
        Assert.Contains("fp", store.Cleared);
    }

    [Fact]
    public async Task Engine_DoesNotWriteCheckpointsWhenResumeIsOff()
    {
        var store = new RecordingCheckpointStore();
        var options = RecreateTestData.Options(new ScreenRect(0, 0, 11, 11));
        options.Fingerprint = "fp";
        options.Resume = false;
        options.CheckpointEveryStrokes = 1;
        var layer = new Layer(new SKColor(255, 0, 0), RecreateTestData.Mask(6, 3, (_, _) => true), 0, 18);

        var engine = new RecreateEngine(
            new RecordingInputSimulator(), new RecordingColorPicker(), null, options,
            checkpointStore: store);
        var result = await engine.RunAsync(new[] { layer }, CancellationToken.None);

        Assert.Equal(3, result.DrawnStrokes);
        Assert.Empty(store.Saved);
        Assert.Contains("fp", store.Cleared);
    }

    [Fact]
    public async Task Engine_AbortSavesARevivableCheckpoint()
    {
        var store = new RecordingCheckpointStore();
        var input = new RecordingInputSimulator();
        var options = RecreateTestData.Options(new ScreenRect(0, 0, 5, 1));
        options.Fingerprint = "fp";
        options.Resume = true;
        var layer = new Layer(new SKColor(255, 0, 0), RecreateTestData.Mask(6, 2, (_, _) => true), 0, 12);
        var engine = new RecreateEngine(
            input, new RecordingColorPicker(), null, options,
            checkpointStore: store);
        var downs = 0;
        input.OnLeftDown = () =>
        {
            if (++downs == 2) engine.Abort();
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => engine.RunAsync(new[] { layer }, CancellationToken.None));

        var saved = Assert.Single(store.Saved);
        Assert.Equal(1, saved.StrokeIndex);
        Assert.Equal("fp", saved.Fingerprint);
        Assert.Empty(store.Cleared);
        Assert.Equal(2, input.LeftUpCalls);
    }
}
