using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Autodraw;

/// <summary>Code-only dry-run window.  Kept out of XAML so the preview does not depend on the
/// current theme's image styles.</summary>
public sealed class RecreatePreviewWindow : Window
{
    public RecreatePreviewWindow(Bitmap image, string title)
    {
        Title = title;
        Width = 900;
        Height = 700;
        MinWidth = 320;
        MinHeight = 240;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.Parse("#10141A"));

        var picture = new Image
        {
            Source = image,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12)
        };

        Content = new ScrollViewer { Content = picture };
        Closed += (_, _) => image.Dispose();
    }
}
