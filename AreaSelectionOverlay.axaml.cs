using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform;
using Autodraw.Configuration;
using Autodraw.ImageInput;

namespace Autodraw;

public partial class AreaSelectionOverlay : Window
{
    private readonly MonitorInfo _monitor;
    private Point _start;
    private bool _dragging;

    public event EventHandler<ScreenRect?>? AreaSelected;

    public AreaSelectionOverlay(Screen screen)
    {
        _monitor = new MonitorInfo(
            0,
            screen.Bounds.X,
            screen.Bounds.Y,
            screen.Bounds.Width,
            screen.Bounds.Height,
            screen.Scaling,
            screen.IsPrimary);
        InitializeComponent();
        Title = LocalizationManager.T("region_title");
        Instructions.Text = LocalizationManager.T("region_hint");
        Position = new PixelPoint(screen.Bounds.X, screen.Bounds.Y);
        Width = Math.Max(1, screen.Bounds.Width / screen.Scaling);
        Height = Math.Max(1, screen.Bounds.Height / screen.Scaling);
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _start = e.GetPosition(this);
        _dragging = true;
        SelectionBorder.IsVisible = true;
        UpdateSelection(_start, _start);
        e.Pointer.Capture(Surface);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_dragging) return;
        UpdateSelection(_start, e.GetPosition(this));
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        e.Pointer.Capture(null);
        var end = e.GetPosition(this);
        var rect = ToScreenRect(_start, end);
        AreaSelected?.Invoke(this, rect);
        Close();
    }

    private void UpdateSelection(Point start, Point end)
    {
        var left = Math.Min(start.X, end.X);
        var top = Math.Min(start.Y, end.Y);
        var width = Math.Abs(end.X - start.X);
        var height = Math.Abs(end.Y - start.Y);
        Canvas.SetLeft(SelectionBorder, left);
        Canvas.SetTop(SelectionBorder, top);
        SelectionBorder.Width = width;
        SelectionBorder.Height = height;
        var rect = ToScreenRect(start, end);
        SizeText.Text = rect is null
            ? string.Empty
            : LocalizationManager.F("region_size", rect.Value.Left, rect.Value.Top, rect.Value.Width, rect.Value.Height);
    }

    private ScreenRect? ToScreenRect(Point start, Point end) =>
        RegionCaptureMath.FromLogicalDrag(_monitor, start.X, start.Y, end.X, end.Y);

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        AreaSelected?.Invoke(this, null);
        Close();
    }
}
