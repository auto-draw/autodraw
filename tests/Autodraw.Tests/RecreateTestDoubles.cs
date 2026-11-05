using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Autodraw.Configuration;
using Autodraw.Painting.Recreate;
using SkiaSharp;

namespace Autodraw.Tests;

internal sealed class RecordingInputSimulator : IInputSimulator
{
    public List<ScreenPoint> Moves { get; } = new();
    public List<ScreenPoint> Clicks { get; } = new();
    public List<string> ReplacedTexts { get; } = new();
    public int LeftDownCalls { get; private set; }
    public int LeftUpCalls { get; private set; }
    public int EnterCalls { get; private set; }
    public Action? OnLeftDown { get; set; }

    public Task MoveToAsync(ScreenPoint point, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Moves.Add(point);
        return Task.CompletedTask;
    }

    public Task LeftDownAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LeftDownCalls++;
        OnLeftDown?.Invoke();
        return Task.CompletedTask;
    }

    public Task LeftUpAsync(CancellationToken cancellationToken)
    {
        LeftUpCalls++;
        return Task.CompletedTask;
    }

    public Task ClickAsync(ScreenPoint point, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Clicks.Add(point);
        return Task.CompletedTask;
    }

    public Task SelectAllAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task TypeTextAsync(string text, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ReplacedTexts.Add(text);
        return Task.CompletedTask;
    }

    public Task ReplaceTextAsync(string text, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ReplacedTexts.Add(text);
        return Task.CompletedTask;
    }

    public Task PressEnterAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnterCalls++;
        return Task.CompletedTask;
    }
}

internal sealed class RecordingColorPicker : IColorPicker
{
    public List<SKColor> Selected { get; } = new();
    public Action<SKColor>? OnSelect { get; set; }

    public Task SelectColorAsync(SKColor color, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Selected.Add(color);
        OnSelect?.Invoke(color);
        return Task.CompletedTask;
    }
}

internal sealed class RecordingCheckpointStore : ICheckpointStore
{
    public List<RecreateCheckpoint> Saved { get; } = new();
    public List<string> Cleared { get; } = new();
    public RecreateCheckpoint? Existing { get; set; }

    public RecreateCheckpoint? Load(string fingerprint)
        => Existing is not null && string.Equals(Existing.Fingerprint, fingerprint, StringComparison.Ordinal)
            ? Existing
            : null;

    public void Save(RecreateCheckpoint checkpoint) => Saved.Add(checkpoint);

    public void Clear(string fingerprint) => Cleared.Add(fingerprint);
}

/// <summary>
/// Input backend whose drag movement can fail on demand, so the engine's stroke retry and
/// release-on-failure paths can be exercised without a real mouse.
/// </summary>
internal sealed class FaultyInputSimulator : IInputSimulator
{
    public bool FailAllMoves { get; set; }
    public bool FailFirstDragMove { get; set; }
    public List<ScreenPoint> Moves { get; } = new();
    public List<ScreenPoint> Clicks { get; } = new();
    public int MoveCalls { get; private set; }
    public int FailedMoves { get; private set; }
    public int LeftDownCalls { get; private set; }
    public int LeftUpCalls { get; private set; }
    public Action? OnLeftDown { get; set; }
    public Action? OnMoveFailure { get; set; }

    public Task MoveToAsync(ScreenPoint point, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        MoveCalls++;

        if (FailAllMoves || (FailFirstDragMove && LeftDownCalls > LeftUpCalls))
        {
            FailFirstDragMove = false;
            FailedMoves++;
            OnMoveFailure?.Invoke();
            throw new System.IO.IOException("injected move failure");
        }

        Moves.Add(point);
        return Task.CompletedTask;
    }

    public Task LeftDownAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LeftDownCalls++;
        OnLeftDown?.Invoke();
        return Task.CompletedTask;
    }

    public Task LeftUpAsync(CancellationToken cancellationToken)
    {
        LeftUpCalls++;
        return Task.CompletedTask;
    }

    public Task ClickAsync(ScreenPoint point, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Clicks.Add(point);
        return Task.CompletedTask;
    }

    public Task SelectAllAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task TypeTextAsync(string text, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task ReplaceTextAsync(string text, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task PressEnterAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class FixedColorSampler : IScreenColorSampler
{
    private readonly SKColor? _color;

    public FixedColorSampler(SKColor? color) => _color = color;

    public SKColor? TrySample(ScreenPoint point) => _color;
}

internal static class RecreateTestData
{    public static SKBitmap Mask(int width, int height, Func<int, int, bool> opaque, SKColor? color = null)
    {
        var bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        var fill = color ?? new SKColor(255, 0, 0, 255);
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
            bitmap.SetPixel(x, y, opaque(x, y) ? fill : new SKColor(0, 0, 0, 0));
        return bitmap;
    }

    public static RecreateOptions Options(ScreenRect canvas) => new()
    {
        Canvas = canvas,
        BrushSize = 1,
        AlphaThreshold = 200,
        StrokeDelayMs = 0,
        MoveStepPixels = 1,
        PickerStepDelayMs = 0,
        VerifySettleDelayMs = 0,
        Verify = false,
        DisposeLayers = true
    };
}

/// <summary>Test/dry-run guard whose state can be flipped to simulate a focus loss.</summary>
public sealed class MutableFocusGuard : IFocusGuard
{
    public string Description => "test guard";

    public bool Armed { get; private set; } = true;

    public bool IsTargetActive { get; set; } = true;

    public Task<bool> ArmAsync(CancellationToken cancellationToken) => Task.FromResult(Armed = true);
}
