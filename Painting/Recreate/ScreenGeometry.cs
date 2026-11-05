using System;
using System.Runtime.InteropServices;
using Autodraw.Configuration;

namespace Autodraw.Painting.Recreate;

/// <summary>Screen-space helpers shared by calibration validation and the focus guard.</summary>
public static class ScreenGeometry
{
    /// <summary>True when <paramref name="point"/> lies on or inside <paramref name="bounds"/>.</summary>
    public static bool Contains(ScreenRect bounds, ScreenPoint point)
        => point.X >= bounds.Left && point.X <= bounds.Right
        && point.Y >= bounds.Top && point.Y <= bounds.Bottom;

    /// <summary>
    /// The bounding box of every monitor, in the same absolute virtual-desktop coordinates that
    /// the input simulator uses.  Returns null when the platform cannot report it, which makes
    /// callers skip the bounds check rather than reject every calibration.
    /// </summary>
    public static ScreenRect? TryGetVirtualScreenBounds()
    {
#if WINDOWS
        if (OperatingSystem.IsWindows())
        {
            const int smXVirtualScreen = 76;
            const int smYVirtualScreen = 77;
            const int smCxVirtualScreen = 78;
            const int smCyVirtualScreen = 79;
            var left = GetSystemMetrics(smXVirtualScreen);
            var top = GetSystemMetrics(smYVirtualScreen);
            var width = GetSystemMetrics(smCxVirtualScreen);
            var height = GetSystemMetrics(smCyVirtualScreen);
            if (width <= 0 || height <= 0) return null;
            return new ScreenRect(left, top, left + width - 1, top + height - 1);
        }
#endif
        return null;
    }

#if WINDOWS
    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
#endif
}

/// <summary>Thrown when the target application lost focus and the run stopped to stay safe.</summary>
public class FocusLostException : Exception
{
    public FocusLostException(string message) : base(message) { }
}

/// <summary>
/// Confirms that the window the run was started against still owns the foreground.  A run must
/// never keep clicking after the user switches windows, because every click would land in
/// whatever application is now in front.
/// </summary>
public interface IFocusGuard
{
    /// <summary>Names the window being guarded, for logs and status messages.</summary>
    string Description { get; }

    /// <summary>Captures the target window.  False means nothing suitable was focused.</summary>
    System.Threading.Tasks.Task<bool> ArmAsync(System.Threading.CancellationToken cancellationToken);

    /// <summary>True while the armed window is still the foreground window.</summary>
    bool IsTargetActive { get; }
}

/// <summary>Guard used by dry runs and tests: the target never loses focus.</summary>
public sealed class NullFocusGuard : IFocusGuard
{
    public string Description => "not enforced";

    public System.Threading.Tasks.Task<bool> ArmAsync(System.Threading.CancellationToken cancellationToken)
        => System.Threading.Tasks.Task.FromResult(true);

    public bool IsTargetActive => true;
}
