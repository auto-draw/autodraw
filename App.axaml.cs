using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using System;
using System.Linq;

namespace Autodraw;

public partial class App : Application
{
    public static Uri CurrentTheme = new Uri(Config.getEntry("theme") == null ? "avares://Autodraw/Styles/dark.axaml" : Config.getEntry("theme"));

    public static void LoadTheme(Uri themeUri)
    {
        Current.Styles.Remove(Current.Styles[2]);
        Current.Styles.Add((IStyle)AvaloniaXamlLoader.Load(
            themeUri
            ));
        CurrentTheme = themeUri;
        Config.setEntry("theme", themeUri.LocalPath);
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        LoadTheme(CurrentTheme);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}