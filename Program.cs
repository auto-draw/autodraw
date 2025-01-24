using System;
using Avalonia;

namespace Autodraw;

internal class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        catch (Exception e)
        {
            Utils.Log(e.ToString());
            Utils.Log(e.Message);
            throw;
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect().With(new Win32PlatformOptions{RenderingMode = new[] { Win32RenderingMode.Wgl }, CompositionMode = new[] { Win32CompositionMode.WinUIComposition }})
            .WithInterFont()
            .LogToTrace();
    }
}