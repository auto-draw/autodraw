using System;
using Avalonia;

namespace Autodraw;

internal class Program
{
    private static bool compatability = false;

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            compatability = args?.Length > 0 && args[0].Equals("compat", StringComparison.OrdinalIgnoreCase);
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
        var app = AppBuilder.Configure<App>()
            .UsePlatformDetect();
             
        if (compatability)
        {
            app = app.With(new Win32PlatformOptions
            {
                RenderingMode = [Win32RenderingMode.Wgl,Win32RenderingMode.Vulkan,Win32RenderingMode.Software],
                CompositionMode = [Win32CompositionMode.WinUIComposition,Win32CompositionMode.DirectComposition,Win32CompositionMode.RedirectionSurface]
            });
        }

        app.WithInterFont()
            .LogToTrace();
        return app;
    }
}