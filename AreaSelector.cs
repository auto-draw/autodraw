using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform;
using Autodraw.Configuration;

namespace Autodraw;

/// <summary>Shows borderless selection overlay(s) and returns the physical screen rectangle the
/// user dragged. Used by the F1 capture/area hotkey and the HexedDrawer Area button. When a target
/// screen is supplied only that display is dimmed, so capture coordinates stay in that monitor's
/// coordinate space.</summary>
public static class AreaSelector
{
    private static bool _selecting;

    public static Task<ScreenRect?> SelectAsync(Window owner) => SelectAsync(owner, null);

    public static async Task<ScreenRect?> SelectAsync(Window owner, Screen? targetScreen)
    {
        if (_selecting) return null;
        _selecting = true;
        var overlays = new List<AreaSelectionOverlay>();
        var completion = new TaskCompletionSource<ScreenRect?>();
        var previousState = owner.WindowState;
        try
        {
            if (previousState != WindowState.Minimized)
            {
                owner.WindowState = WindowState.Minimized;
                await Task.Delay(150);
            }

            var screens = targetScreen is null ? owner.Screens.All.ToArray() : new[] { targetScreen };
            foreach (var screen in screens)
            {
                AreaSelectionOverlay overlay;
                try
                {
                    overlay = new AreaSelectionOverlay(screen);
                }
                catch (Exception ex)
                {
                    Utils.Log("Area overlay could not be created for a display: " + ex.Message);
                    continue;
                }

                void OnAreaSelected(object? sender, ScreenRect? rect)
                {
                    if (!completion.TrySetResult(rect)) return;
                    foreach (var candidate in overlays) candidate.Close();
                }

                overlay.AreaSelected += OnAreaSelected;
                overlays.Add(overlay);
                overlay.Show();
            }

            if (overlays.Count == 0) return null;
            return await completion.Task;
        }
        finally
        {
            foreach (var overlay in overlays)
            {
                try { overlay.Close(); } catch { /* already closed */ }
            }
            _selecting = false;
            owner.WindowState = previousState;
            if (previousState != WindowState.Minimized) owner.Activate();
        }
    }
}
