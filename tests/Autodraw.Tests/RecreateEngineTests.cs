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

public class RecreateEngineTests
{
    [Fact]
    public async Task RunAsync_DrawsEachLayerAndReleasesMouse()
    {
        var input = new RecordingInputSimulator();
        var picker = new RecordingColorPicker();
        var options = RecreateTestData.Options(new ScreenRect(0, 0, 2, 0));
        var layer = new Layer(
            new SKColor(255, 0, 0),
            RecreateTestData.Mask(3, 1, (_, _) => true),
            0,
            3);

        var engine = new RecreateEngine(input, picker, null, options);
        var result = await engine.RunAsync(new[] { layer }, CancellationToken.None);

        Assert.Equal(1, result.LayerCount);
        Assert.True(result.DrawnStrokes > 0);
        Assert.Equal(new SKColor(255, 0, 0), Assert.Single(picker.Selected));
        Assert.Equal(input.LeftDownCalls, input.LeftUpCalls);
        Assert.NotEmpty(input.Moves);
        Assert.True(layer.IsDisposed);
    }

    [Fact]
    public async Task RunAsync_CancellationAlwaysReleasesAHeldMouseButton()
    {
        using var cts = new CancellationTokenSource();
        var input = new RecordingInputSimulator { OnLeftDown = () => cts.Cancel() };
        var options = RecreateTestData.Options(new ScreenRect(0, 0, 2, 0));
        var layer = new Layer(
            new SKColor(0, 255, 0),
            RecreateTestData.Mask(3, 1, (_, _) => true),
            0,
            3);

        var engine = new RecreateEngine(input, new RecordingColorPicker(), null, options);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => engine.RunAsync(new[] { layer }, cts.Token));
        Assert.True(input.LeftDownCalls > 0);
        Assert.Equal(input.LeftDownCalls, input.LeftUpCalls);
        Assert.True(layer.IsDisposed);
    }

    [Fact]
    public async Task RunAsync_DryRunReturnsPreviewWithoutClicking()
    {
        var input = new RecordingInputSimulator();
        var options = RecreateTestData.Options(new ScreenRect(0, 0, 2, 1));
        options.DryRun = true;
        var layer = new Layer(
            new SKColor(0, 0, 255),
            RecreateTestData.Mask(3, 2, (_, y) => y == 0),
            0,
            3);

        var engine = new RecreateEngine(input, new RecordingColorPicker(), null, options);
        var result = await engine.RunAsync(new[] { layer }, CancellationToken.None);

        Assert.True(result.DryRun);
        Assert.Equal(1, result.TotalStrokes);
        Assert.NotNull(result.PreviewBitmap);
        Assert.Equal(3, result.PreviewBitmap!.Width);
        Assert.Equal(2, result.PreviewBitmap.Height);
        Assert.Empty(input.Moves);
        Assert.Empty(input.Clicks);
        result.PreviewBitmap.Dispose();
    }

    [Fact]
    public async Task RunAsync_VerifyRetriesAndThenSucceeds()
    {
        var input = new RecordingInputSimulator();
        var picker = new RecordingColorPicker();
        var options = RecreateTestData.Options(new ScreenRect(0, 0, 2, 0));
        options.Verify = true;
        options.VerifyRetries = 1;
        // The first sample is wrong, the second matches the requested colour.
        var verifier = new CountingVerifier(new SKColor(255, 0, 0), failuresBeforeMatch: 1);
        var layer = new Layer(
            new SKColor(255, 0, 0),
            RecreateTestData.Mask(3, 1, (_, _) => true),
            0,
            3);

        var engine = new RecreateEngine(input, picker, verifier, options);
        var result = await engine.RunAsync(new[] { layer }, CancellationToken.None);

        Assert.Equal(1, result.DrawnStrokes);
        Assert.Equal(2, picker.Selected.Count);
        Assert.Equal(2, verifier.Calls);
    }

    [Fact]
    public async Task RunAsync_VerificationFailureRaisesUserFacingColorSelectionError()
    {
        var input = new RecordingInputSimulator();
        var options = RecreateTestData.Options(new ScreenRect(0, 0, 2, 0));
        options.Verify = true;
        options.VerifyRetries = 0;
        var verifier = new CountingVerifier(new SKColor(0, 0, 0), failuresBeforeMatch: int.MaxValue);
        var layer = new Layer(
            new SKColor(255, 0, 0),
            RecreateTestData.Mask(3, 1, (_, _) => true),
            0,
            3);

        var engine = new RecreateEngine(input, new RecordingColorPicker(), verifier, options);
        var error = await Assert.ThrowsAsync<ColorSelectionException>(
            () => engine.RunAsync(new[] { layer }, CancellationToken.None));

        Assert.Contains("Could not select", error.Message);
        Assert.True(layer.IsDisposed);
    }

    [Fact]
    public async Task RunAsync_VerifyWithoutVerifierFailsBeforeAnyInput()
    {
        var input = new RecordingInputSimulator();
        var options = RecreateTestData.Options(new ScreenRect(0, 0, 2, 0));
        options.Verify = true;
        var layer = new Layer(
            new SKColor(255, 0, 0),
            RecreateTestData.Mask(3, 1, (_, _) => true),
            0,
            3);

        var engine = new RecreateEngine(input, new RecordingColorPicker(), null, options);

        await Assert.ThrowsAsync<ColorSelectionException>(
            () => engine.RunAsync(new[] { layer }, CancellationToken.None));
        Assert.Empty(input.Moves);
        Assert.Empty(input.Clicks);
    }

    [Fact]
    public async Task RunAsync_RetriesAFailedStrokeAndReleasesTheMouseBetweenAttempts()
    {
        var input = new FaultyInputSimulator { FailFirstDragMove = true };
        var options = RecreateTestData.Options(new ScreenRect(0, 0, 3, 0));
        options.StrokeAttempts = 2;
        var logs = new List<string>();
        var layer = new Layer(
            new SKColor(255, 0, 0),
            RecreateTestData.Mask(4, 1, (_, _) => true),
            0,
            4);

        var engine = new RecreateEngine(input, new RecordingColorPicker(), null, options, log: logs.Add);
        var result = await engine.RunAsync(new[] { layer }, CancellationToken.None);

        Assert.Equal(1, result.DrawnStrokes);
        Assert.Equal(1, input.FailedMoves);
        Assert.Equal(2, input.LeftDownCalls);
        Assert.Equal(2, input.LeftUpCalls);
        Assert.Contains(logs, line => line.Contains("Stroke attempt 1/2 failed", StringComparison.Ordinal));
        Assert.True(layer.IsDisposed);
    }

    [Fact]
    public async Task RunAsync_GivesUpAfterTheConfiguredStrokeAttempts()
    {
        var input = new FaultyInputSimulator { FailAllMoves = true };
        var options = RecreateTestData.Options(new ScreenRect(0, 0, 3, 0));
        options.StrokeAttempts = 3;
        var layer = new Layer(
            new SKColor(255, 0, 0),
            RecreateTestData.Mask(4, 1, (_, _) => true),
            0,
            4);

        var engine = new RecreateEngine(input, new RecordingColorPicker(), null, options);

        await Assert.ThrowsAsync<System.IO.IOException>(
            () => engine.RunAsync(new[] { layer }, CancellationToken.None));

        Assert.Equal(3, input.FailedMoves);
        Assert.Equal(0, input.LeftDownCalls);
        Assert.Equal(0, input.LeftUpCalls);
        Assert.True(layer.IsDisposed);
    }

    [Fact]
    public async Task RunAsync_AbortDuringAStrokeReleasesTheMouseAndStops()
    {
        var input = new RecordingInputSimulator();
        var options = RecreateTestData.Options(new ScreenRect(0, 0, 5, 1));
        var layer = new Layer(
            new SKColor(255, 0, 0),
            RecreateTestData.Mask(6, 2, (_, _) => true),
            0,
            12);
        var engine = new RecreateEngine(input, new RecordingColorPicker(), null, options);
        var downs = 0;
        input.OnLeftDown = () =>
        {
            if (++downs == 2) engine.Abort();
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => engine.RunAsync(new[] { layer }, CancellationToken.None));

        Assert.Equal(2, input.LeftDownCalls);
        Assert.Equal(2, input.LeftUpCalls);
        Assert.True(layer.IsDisposed);
    }

    [Fact]
    public async Task RunAsync_FocusLostBeforeAStrokeRetryDoesNotClickAgain()
    {
        var input = new FaultyInputSimulator { FailFirstDragMove = true };
        var guard = new MutableFocusGuard();
        input.OnMoveFailure = () => guard.IsTargetActive = false;
        var options = RecreateTestData.Options(new ScreenRect(0, 0, 3, 0));
        options.StrokeAttempts = 3;
        var layer = new Layer(
            new SKColor(255, 0, 0),
            RecreateTestData.Mask(4, 1, (_, _) => true),
            0,
            4);

        var engine = new RecreateEngine(
            input, new RecordingColorPicker(), null, options, focusGuard: guard);

        await Assert.ThrowsAsync<RecreateFocusLostException>(
            () => engine.RunAsync(new[] { layer }, CancellationToken.None));

        Assert.Equal(1, input.FailedMoves);
        // The failed attempt released the button and the retry never pressed it again.
        Assert.Equal(1, input.LeftDownCalls);
        Assert.Equal(1, input.LeftUpCalls);
        Assert.True(layer.IsDisposed);
    }

    [Fact]
    public async Task RunAsync_FocusLostBeforeTheRunStopsWithoutAnyInput()
    {
        var input = new RecordingInputSimulator();
        var options = RecreateTestData.Options(new ScreenRect(0, 0, 3, 0));
        var guard = new MutableFocusGuard { IsTargetActive = false };
        var layer = new Layer(
            new SKColor(255, 0, 0),
            RecreateTestData.Mask(4, 1, (_, _) => true),
            0,
            4);

        var engine = new RecreateEngine(
            input, new RecordingColorPicker(), null, options, focusGuard: guard);

        await Assert.ThrowsAsync<RecreateFocusLostException>(
            () => engine.RunAsync(new[] { layer }, CancellationToken.None));

        Assert.Empty(input.Moves);
        Assert.Empty(input.Clicks);
        Assert.True(layer.IsDisposed);
    }

    [Fact]
    public async Task RunAsync_FocusLostBetweenLayersStopsAndDisposesBothMasks()
    {
        var input = new RecordingInputSimulator();
        var options = RecreateTestData.Options(new ScreenRect(0, 0, 3, 0));
        var guard = new MutableFocusGuard();
        var picker = new RecordingColorPicker();
        var first = new Layer(
            new SKColor(255, 0, 0),
            RecreateTestData.Mask(4, 1, (_, _) => true),
            0,
            4);
        var second = new Layer(
            new SKColor(0, 255, 0),
            RecreateTestData.Mask(4, 1, (_, _) => true),
            1,
            4);
        input.OnLeftDown = () => guard.IsTargetActive = false;

        var engine = new RecreateEngine(input, picker, null, options, focusGuard: guard);

        await Assert.ThrowsAsync<RecreateFocusLostException>(
            () => engine.RunAsync(new[] { first, second }, CancellationToken.None));

        Assert.Single(picker.Selected);
        Assert.True(first.IsDisposed);
        Assert.True(second.IsDisposed);
    }

    [Fact]
    public async Task RunAsync_WithoutAGuardDoesNotEnforceFocus()
    {
        // The engine only enforces focus when it is handed a guard; the option is read by the
        // caller that builds the guard, so a null guard means "not enforced".
        var input = new RecordingInputSimulator();
        var options = RecreateTestData.Options(new ScreenRect(0, 0, 3, 0));
        var layer = new Layer(
            new SKColor(255, 0, 0),
            RecreateTestData.Mask(4, 1, (_, _) => true),
            0,
            4);

        var engine = new RecreateEngine(input, new RecordingColorPicker(), null, options);

        var result = await engine.RunAsync(new[] { layer }, CancellationToken.None);

        Assert.Equal(1, result.DrawnStrokes);
    }

    [Fact]
    public async Task RunAsync_WithoutLayersReturnsAnEmptyResult()
    {
        var options = RecreateTestData.Options(new ScreenRect(0, 0, 3, 0));
        var engine = new RecreateEngine(new RecordingInputSimulator(), new RecordingColorPicker(), null, options);

        var result = await engine.RunAsync(Array.Empty<Layer>(), CancellationToken.None);

        Assert.Equal(0, result.LayerCount);
        Assert.Equal(0, result.TotalStrokes);
        Assert.Equal(0, result.DrawnStrokes);
    }

    private sealed class CountingVerifier : IColorVerifier
    {
        private readonly SKColor _sampled;
        private readonly int _failuresBeforeMatch;

        public CountingVerifier(SKColor sampled, int failuresBeforeMatch)
        {
            _sampled = sampled;
            _failuresBeforeMatch = failuresBeforeMatch;
        }

        public int Calls { get; private set; }

        public ColorVerificationResult Verify(SKColor expected)
        {
            Calls++;
            var matched = Calls > _failuresBeforeMatch;
            return new ColorVerificationResult(matched, _sampled, matched ? 0 : 100, "test");
        }
    }
}
