using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Autodraw;

/// <summary>
/// Always-on-top guide window shown while a color-picker calibration is being captured.
/// It tells the user exactly where to click inside the external color picker ("1 / 3 —
/// Click the R (red) field"), advances on every captured click, and can be cancelled
/// with its Cancel button or the ESC key.  It never steals focus from the target
/// application beyond its initial show.
/// </summary>
public partial class PickerCalibrationOverlay : Window
{
    /// <summary>True when the user cancelled; the owner checks this after Close().</summary>
    public bool Cancelled { get; private set; }

    public event EventHandler? CancelRequested;

    public PickerCalibrationOverlay()
    {
        InitializeComponent();
        CancelButton.Click += (_, _) => RequestCancel();
    }

    public void SetStep(int completed, int total, string nextStepDescription, string title)
    {
        TitleText.Text = title;
        if (completed >= total)
        {
            StepCounterText.Text = $"{total} / {total}";
            StepText.Text = nextStepDescription;
            StepProgress.Value = 100;
            return;
        }

        StepCounterText.Text = $"{completed + 1} / {total}";
        StepText.Text = nextStepDescription;
        StepProgress.Value = 100d * completed / total;
    }

    /// <summary>Show the guide immediately before the next click of the capture sequence.</summary>
    public void ShowFor(int completed, int total, string nextStepDescription, string title)
    {
        SetStep(completed, total, nextStepDescription, title);
        if (IsVisible) return;
        Show();
        Activate();
    }

    private void RequestCancel()
    {
        Cancelled = true;
        CancelRequested?.Invoke(this, EventArgs.Empty);
        Close();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        CancelRequested = null; // detach so the natural close-on-finish doesn't count as a cancellation
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) RequestCancel();
    }
}
