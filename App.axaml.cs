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
    public static Uri CurrentTheme = new Uri("avares://Autodraw/Styles/dark.axaml");

    public static void LoadTheme(Uri themeUri)
    {
        App.Current.Styles.Remove(App.Current.Styles[2]);
        App.Current.Styles.Add((Avalonia.Styling.IStyle)AvaloniaXamlLoader.Load(
            themeUri
            ));
        CurrentTheme = themeUri;
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