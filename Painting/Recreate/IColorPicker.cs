using System;
using System.Threading;
using System.Threading.Tasks;
using SkiaSharp;

namespace Autodraw.Painting.Recreate;

/// <summary>Selects a colour in the target application before a layer is drawn.</summary>
public interface IColorPicker
{
    Task SelectColorAsync(SKColor color, CancellationToken cancellationToken);
}

/// <summary>Raised when a colour cannot be selected or verified.  The message is user-facing.</summary>
public sealed class ColorSelectionException : Exception
{
    public ColorSelectionException(string message) : base(message) { }
    public ColorSelectionException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>Picker used by a dry run, which plans strokes but never touches the target application.</summary>
public sealed class NoOpColorPicker : IColorPicker
{
    public Task SelectColorAsync(SKColor color, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
