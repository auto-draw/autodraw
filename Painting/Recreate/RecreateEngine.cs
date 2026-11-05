using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autodraw.Configuration;
using SkiaSharp;

namespace Autodraw.Painting.Recreate;

/// <summary>Cooperative pause gate used by F2 while a recreate run is active.</summary>
public sealed class RecreatePauseController
{
    private volatile bool _paused;

    public bool IsPaused
    {
        get => _paused;
        set => _paused = value;
    }

    public async Task WaitIfPausedAsync(CancellationToken cancellationToken)
    {
        while (_paused)
            await Task.Delay(30, cancellationToken).ConfigureAwait(false);
    }
}

public sealed record RecreateProgress(
    int LayerIndex,
    int LayerCount,
    SKColor Color,
    int CompletedStrokes,
    int TotalStrokes,
    string Phase,
    bool IsLayerBoundary)
{
    public double Percent => TotalStrokes <= 0 ? 0 : Math.Clamp(CompletedStrokes * 100d / TotalStrokes, 0, 100);
    public string LayerText => $"{LayerIndex}/{LayerCount}";
    public string ColorHex => $"#{Color.Red:X2}{Color.Green:X2}{Color.Blue:X2}";
}

public sealed class RecreateResult
{
    public int LayerCount { get; init; }
    public int TotalStrokes { get; init; }
    public int DrawnStrokes { get; init; }
    public int ResumedFromStroke { get; init; }
    public bool DryRun { get; init; }
    /// <summary>Caller-owned diagnostic preview produced only by a dry run.</summary>
    public SKBitmap? PreviewBitmap { get; init; }
}

/// <summary>Raised when the target window lost focus and the run stopped to stay safe.</summary>
public sealed class RecreateFocusLostException : FocusLostException
{
    public RecreateFocusLostException(string message) : base(message) { }
}

/// <summary>
/// Sequential colour-layer engine.  It plans every layer first, then for each layer selects the
/// colour, verifies it, paints the layer's runs, and reports progress.
///
/// Safety contract:
///  * the target window is checked for focus before every colour selection and every stroke;
///  * a colour is never drawn until the verifier confirms it is active;
///  * every selection and every stroke is retried, and both are idempotent;
///  * any exit path releases a held mouse button;
///  * progress is checkpointed so an abort or disconnect can resume.
///
/// The engine never blocks the UI thread itself; callers run it from a background task.
/// </summary>
public sealed class RecreateEngine
{
    private readonly IInputSimulator _input;
    private readonly IColorPicker _picker;
    private readonly IColorVerifier? _verifier;
    private readonly RecreateOptions _options;
    private readonly RecreatePauseController? _pause;
    private readonly IFocusGuard? _focusGuard;
    private readonly ICheckpointStore? _checkpoints;
    private readonly Action<string>? _log;

    private volatile bool _mouseDown;
    private volatile bool _abortRequested;
    private long _lastProgressTicks;

    private int _completedStrokes;
    private int _totalStrokes;
    private int _currentLayerIndex;
    private bool _runCompleted;

    public RecreateEngine(
        IInputSimulator input,
        IColorPicker picker,
        IColorVerifier? verifier,
        RecreateOptions options,
        RecreatePauseController? pause = null,
        Action<string>? log = null,
        IFocusGuard? focusGuard = null,
        ICheckpointStore? checkpointStore = null)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _picker = picker ?? throw new ArgumentNullException(nameof(picker));
        _verifier = verifier;
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _pause = pause;
        _log = log;
        _focusGuard = focusGuard;
        _checkpoints = checkpointStore;
    }

    public event EventHandler<RecreateProgress>? ProgressChanged;

    /// <summary>
    /// Stops the run immediately and releases any held mouse button.  Safe to call from the UI
    /// thread while a stroke is in flight; the run loop notices within one input step.
    /// </summary>
    public void Abort()
    {
        _abortRequested = true;
        ReleaseHeldButton();
    }

    public async Task<RecreateResult> RunAsync(IReadOnlyList<Layer> layers, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(layers);
        if (layers.Count == 0)
            return new RecreateResult { DryRun = _options.DryRun };
        if (_options.Canvas.Width <= 0 || _options.Canvas.Height <= 0)
            throw new ArgumentException("The draw region must have positive width and height.", nameof(layers));
        if (!_options.DryRun && _options.Verify && _verifier is null)
            throw new ColorSelectionException(
                "Color verification is enabled but no current-colour indicator was calibrated.");

        _abortRequested = false;
        _runCompleted = false;
        _completedStrokes = 0;
        _totalStrokes = 0;
        _currentLayerIndex = 0;

        var orderedLayers = layers.OrderBy(layer => layer.Order).ToList();
        var resumeFrom = 0;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var plans = new List<LayerStrokePlan>(orderedLayers.Count);
            var totalStrokes = 0;

            for (var index = 0; index < orderedLayers.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var layer = orderedLayers[index];
                if (layer.IsDisposed)
                    throw new ObjectDisposedException(nameof(Layer), "A layer mask was disposed before the run started.");

                Report(index + 1, orderedLayers.Count, layer.Color, 0, totalStrokes, "planning", true, true);
                var mapper = new CanvasMapper(layer.Mask.Width, layer.Mask.Height, _options.Canvas);
                var strokes = StrokePlanner.Plan(
                    layer.Mask,
                    mapper,
                    _options.ToStrokePlanOptions(),
                    cancellationToken);
                totalStrokes += strokes.Count;
                plans.Add(new LayerStrokePlan(layer.Color, strokes));
                if (strokes.Count == 0)
                    _log?.Invoke($"Layer {index + 1}/{orderedLayers.Count} {RecreateOptions.ColorText(layer.Color)} has no strokes.");
            }

            _totalStrokes = totalStrokes;

            if (_options.DryRun)
            {
                Report(orderedLayers.Count, orderedLayers.Count, orderedLayers[^1].Color,
                    totalStrokes, totalStrokes, "dry-run", true, true);
                var preview = StrokePlanner.RenderPreview(plans, _options.Canvas, _options.BrushSize);
                _runCompleted = true;
                return new RecreateResult
                {
                    LayerCount = orderedLayers.Count,
                    TotalStrokes = totalStrokes,
                    DrawnStrokes = 0,
                    DryRun = true,
                    PreviewBitmap = preview
                };
            }

            if (totalStrokes == 0)
            {
                Report(orderedLayers.Count, orderedLayers.Count, orderedLayers[^1].Color,
                    0, 0, "complete", true, true);
                _runCompleted = true;
                ClearCheckpoint();
                return new RecreateResult
                {
                    LayerCount = orderedLayers.Count,
                    TotalStrokes = 0,
                    DrawnStrokes = 0
                };
            }

            // A checkpoint is only honoured when it describes this exact plan.
            resumeFrom = ResolveResumePoint(totalStrokes);
            _completedStrokes = resumeFrom;
            if (resumeFrom > 0)
            {
                _log?.Invoke($"Resuming recreate run at stroke {resumeFrom}/{totalStrokes}.");
                Report(1, orderedLayers.Count, orderedLayers[0].Color,
                    resumeFrom, totalStrokes, "resumed", true, true);
            }

            var globalIndex = 0;
            for (var layerIndex = 0; layerIndex < plans.Count; layerIndex++)
            {
                var plan = plans[layerIndex];
                var layerStrokeCount = plan.Strokes.Count;
                var layerHasWork = resumeFrom < globalIndex + layerStrokeCount;
                if (!layerHasWork)
                {
                    globalIndex += layerStrokeCount;
                    continue;
                }

                _currentLayerIndex = layerIndex;
                await WaitIfPausedAsync(cancellationToken).ConfigureAwait(false);
                ThrowIfAborted(cancellationToken);
                EnsureFocus();

                Report(layerIndex + 1, orderedLayers.Count, plan.Color,
                    _completedStrokes, totalStrokes, "selecting", true, true);
                await SelectColorWithVerificationAsync(plan.Color, cancellationToken).ConfigureAwait(false);

                for (var strokeIndex = 0; strokeIndex < layerStrokeCount; strokeIndex++, globalIndex++)
                {
                    if (globalIndex < resumeFrom) continue;
                    await WaitIfPausedAsync(cancellationToken).ConfigureAwait(false);
                    ThrowIfAborted(cancellationToken);
                    EnsureFocus();

                    await DrawStrokeWithRetryAsync(plan.Strokes[strokeIndex], cancellationToken).ConfigureAwait(false);
                    _completedStrokes++;
                    Report(layerIndex + 1, orderedLayers.Count, plan.Color,
                        _completedStrokes, totalStrokes, "drawing",
                        strokeIndex == 0, false);
                    SaveCheckpointIfDue();
                }

                SaveCheckpoint(layerIndex);
            }

            Report(orderedLayers.Count, orderedLayers.Count, orderedLayers[^1].Color,
                _completedStrokes, totalStrokes, "complete", true, true);
            _runCompleted = true;
            ClearCheckpoint();
            return new RecreateResult
            {
                LayerCount = orderedLayers.Count,
                TotalStrokes = totalStrokes,
                DrawnStrokes = _completedStrokes,
                ResumedFromStroke = resumeFrom
            };
        }
        finally
        {
            ReleaseHeldButton();

            if (!_runCompleted && _options.Resume)
                SaveCheckpoint(_currentLayerIndex);

            if (_options.DisposeLayers)
            {
                foreach (var layer in layers) layer.Dispose();
            }
        }
    }

    private int ResolveResumePoint(int totalStrokes)
    {
        if (!_options.Resume || _options.ResumeCheckpoint is not { } checkpoint) return 0;
        if (!checkpoint.IsUsable(totalStrokes)) return 0;
        if (!string.IsNullOrEmpty(_options.Fingerprint) &&
            !string.Equals(checkpoint.Fingerprint, _options.Fingerprint, StringComparison.Ordinal))
        {
            _log?.Invoke("Ignoring checkpoint: it belongs to a different image or draw region.");
            return 0;
        }

        return Math.Clamp(checkpoint.StrokeIndex, 0, totalStrokes);
    }

    private async Task SelectColorWithVerificationAsync(SKColor color, CancellationToken cancellationToken)
    {
        var attempts = _options.Verify
            ? Math.Max(1, _options.VerifyRetries + 1)
            : Math.Max(1, _options.SelectionAttempts);

        ColorVerificationResult last = default;
        Exception? lastError = null;

        for (var attempt = 0; attempt < attempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfAborted(cancellationToken);
            EnsureFocus();

            try
            {
                await _picker.SelectColorAsync(color, cancellationToken).ConfigureAwait(false);
                lastError = null;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (ColorSelectionException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;
                _log?.Invoke($"Colour selection attempt {attempt + 1}/{attempts} threw: {ex.Message}");
            }

            if (lastError is null && !_options.Verify)
                return;

            if (lastError is null)
            {
                if (_options.VerifySettleDelayMs > 0)
                    await _options.DelayAsync(_options.VerifySettleDelayMs, cancellationToken).ConfigureAwait(false);

                last = _verifier!.Verify(color);
                if (last.Matched)
                {
                    _log?.Invoke($"Verified {RecreateOptions.ColorText(color)}: {last.Detail}");
                    return;
                }

                _log?.Invoke($"Colour verification attempt {attempt + 1}/{attempts} failed: {last.Detail}");
            }

            if (attempt < attempts - 1)
                await _options.DelayAsync(80, cancellationToken).ConfigureAwait(false);
        }

        if (lastError is not null)
            throw new ColorSelectionException(
                $"Could not apply {RecreateOptions.ColorText(color)} after {attempts} attempt(s): {lastError.Message}",
                lastError);

        throw new ColorSelectionException(
            $"Could not select {RecreateOptions.ColorText(color)} after {attempts} attempt(s). {last.Detail}. " +
            "Check the picker calibration and the current-colour indicator point.");
    }

    private async Task DrawStrokeWithRetryAsync(PlannedStroke stroke, CancellationToken cancellationToken)
    {
        var attempts = Math.Max(1, _options.StrokeAttempts);
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            try
            {
                // Re-check focus for every attempt: a retry is still a click into the target app.
                EnsureFocus();
                await DrawStrokeAsync(stroke, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (RecreateFocusLostException)
            {
                throw;
            }
            catch (Exception ex) when (attempt < attempts - 1)
            {
                _log?.Invoke($"Stroke attempt {attempt + 1}/{attempts} failed: {ex.Message}");
                await _options.DelayAsync(40, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task DrawStrokeAsync(PlannedStroke stroke, CancellationToken cancellationToken)
    {
        try
        {
            var start = ToPoint(stroke.Start);
            await _input.MoveToAsync(start, cancellationToken).ConfigureAwait(false);
            ThrowIfAborted(cancellationToken);
            await _input.LeftDownAsync(cancellationToken).ConfigureAwait(false);
            _mouseDown = true;

            if (_options.StrokeDelayMs > 0)
                await _options.DelayAsync(_options.StrokeDelayMs, cancellationToken).ConfigureAwait(false);

            var length = stroke.Length;
            var step = Math.Max(1, _options.MoveStepPixels);
            var steps = Math.Max(1, (int)Math.Ceiling(length / step));
            for (var i = 1; i <= steps; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ThrowIfAborted(cancellationToken);
                var t = i / (double)steps;
                var point = new ScreenPoint(
                    (int)Math.Round(stroke.Start.X + (stroke.End.X - stroke.Start.X) * t),
                    (int)Math.Round(stroke.Start.Y + (stroke.End.Y - stroke.Start.Y) * t));
                await _input.MoveToAsync(point, cancellationToken).ConfigureAwait(false);
                if (_options.StrokeDelayMs > 0)
                    await _options.DelayAsync(_options.StrokeDelayMs, cancellationToken).ConfigureAwait(false);
            }

            await _input.LeftUpAsync(cancellationToken).ConfigureAwait(false);
            _mouseDown = false;
            if (_options.StrokeDelayMs > 0)
                await _options.DelayAsync(_options.StrokeDelayMs, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            ReleaseHeldButton();
            throw;
        }
    }

    private void ReleaseHeldButton()
    {
        if (!_mouseDown) return;
        try { _input.LeftUpAsync(CancellationToken.None).GetAwaiter().GetResult(); }
        catch { /* the original failure is more important than a release failure */ }
        _mouseDown = false;
    }

    private void EnsureFocus()
    {
        if (_focusGuard is null) return;
        if (_focusGuard.IsTargetActive) return;
        throw new RecreateFocusLostException(
            "The target window lost focus, so the draw was stopped and the mouse was released. " +
            "Click back into the game and run again to resume.");
    }

    private void ThrowIfAborted(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_abortRequested) throw new OperationCanceledException("The draw was stopped by the user.");
    }

    private void SaveCheckpointIfDue()
    {
        var every = Math.Max(1, _options.CheckpointEveryStrokes);
        if (_completedStrokes % every == 0)
            SaveCheckpoint(_currentLayerIndex);
    }

    private void SaveCheckpoint(int layerIndex)
    {
        if (!_options.Resume) return;
        if (_checkpoints is null || string.IsNullOrWhiteSpace(_options.Fingerprint)) return;
        if (_totalStrokes <= 0 || _completedStrokes <= 0 || _completedStrokes >= _totalStrokes) return;

        _checkpoints.Save(new RecreateCheckpoint
        {
            Fingerprint = _options.Fingerprint!,
            LayerIndex = layerIndex,
            StrokeIndex = _completedStrokes,
            TotalStrokes = _totalStrokes
        });
    }

    private void ClearCheckpoint()
    {
        if (_checkpoints is null || string.IsNullOrWhiteSpace(_options.Fingerprint)) return;
        _checkpoints.Clear(_options.Fingerprint!);
    }

    private async Task WaitIfPausedAsync(CancellationToken cancellationToken)
    {
        if (_pause is not null)
            await _pause.WaitIfPausedAsync(cancellationToken).ConfigureAwait(false);
    }

    private void Report(
        int layerIndex,
        int layerCount,
        SKColor color,
        int completed,
        int total,
        string phase,
        bool layerBoundary,
        bool force)
    {
        var now = Environment.TickCount64;
        if (!force && now - _lastProgressTicks < 50) return;
        _lastProgressTicks = now;
        ProgressChanged?.Invoke(this, new RecreateProgress(
            layerIndex, layerCount, color, completed, total, phase, layerBoundary));
    }

    private static ScreenPoint ToPoint(SKPoint point)
        => new((int)Math.Round(point.X), (int)Math.Round(point.Y));
}
