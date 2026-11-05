using System;
using System.Threading;
using System.Threading.Tasks;
#if WINDOWS
using System.Runtime.InteropServices;
#endif

namespace Autodraw.Painting.Recreate;

/// <summary>
/// Windows implementation of <see cref="IFocusGuard"/>.  It arms on the current foreground
/// window (after AutoDraw has minimised itself) and reports a loss as soon as any other window
/// becomes foreground.  The comparison is by window handle, so it survives DPI changes, window
/// renames and multiple Roblox experiences.
/// </summary>
public sealed class ForegroundWindowFocusGuard : IFocusGuard
{
    private readonly int _pollMilliseconds;
    private readonly TimeSpan _armTimeout;
    private nint _target;

    public ForegroundWindowFocusGuard(int armTimeoutMilliseconds = 3000, int pollMilliseconds = 50)
    {
        _armTimeout = TimeSpan.FromMilliseconds(Math.Max(250, armTimeoutMilliseconds));
        _pollMilliseconds = Math.Max(10, pollMilliseconds);
    }

    /// <summary>Handle of the armed window; 0 until <see cref="ArmAsync"/> succeeds.</summary>
    public nint Target => _target;

    public string Description => _target == 0
        ? "not armed"
        : $"window 0x{_target.ToInt64():X}";

    public async Task<bool> ArmAsync(CancellationToken cancellationToken)
    {
#if WINDOWS
        if (!OperatingSystem.IsWindows()) return true;

        var ownProcess = Environment.ProcessId;
        var deadline = DateTime.UtcNow + _armTimeout;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var handle = GetForegroundWindow();
            if (handle != 0)
            {
                GetWindowThreadProcessId(handle, out var processId);
                // AutoDraw minimises itself before arming; ignore it if focus has not moved yet.
                if (processId != ownProcess)
                {
                    _target = handle;
                    return true;
                }
            }

            await Task.Delay(_pollMilliseconds, cancellationToken).ConfigureAwait(false);
        }

        _target = 0;
        return false;
#else
        await Task.CompletedTask.ConfigureAwait(false);
        return true;
#endif
    }

    public bool IsTargetActive
    {
        get
        {
#if WINDOWS
            if (!OperatingSystem.IsWindows()) return true;
            if (_target == 0) return false;
            return GetForegroundWindow() == _target;
#else
            return true;
#endif
        }
    }

#if WINDOWS
    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out int processId);
#endif
}
