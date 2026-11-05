using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace Autodraw;

public partial class StencilOverlay : Window
{
    private Bitmap? _bitmap;
    public StencilOverlay() => InitializeComponent();
    public void ShowStencil(SKBitmap source, Configuration.ScreenRect region, int opacity)
    {
        _bitmap?.Dispose(); _bitmap = source.ConvertToAvaloniaBitmap();
        StencilImage.Source = _bitmap; Opacity = System.Math.Clamp(opacity, 0, 100) / 100d;
        Position = new PixelPoint(region.Left, region.Top); Width = region.Width; Height = region.Height;
        Show();
    }
    protected override void OnClosed(System.EventArgs e) { _bitmap?.Dispose(); _bitmap = null; base.OnClosed(e); }
}
