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
            var compatability = args?.Length > 0 && args[0].Equals("compat", StringComparison.OrdinalIgnoreCase);
            BuildAvaloniaApp(compatability)
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
    public static AppBuilder BuildAvaloniaApp(bool compatability = false)
    {
        var app = AppBuilder.Configure<App>()
            .UsePlatformDetect();
             
        if (compatability)
        {
            app = app.With(new Win32PlatformOptions
            {
                RenderingMode = [Win32RenderingMode.Wgl],
                CompositionMode = [Win32CompositionMode.WinUIComposition]
            });
        }

        app.WithInterFont()
            .LogToTrace();
        return app;
    }
}