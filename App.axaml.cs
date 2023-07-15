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
    public static string CurrentTheme = Config.getEntry("theme") ?? "avares://Autodraw/Styles/dark.axaml";
    public static bool SavedIsDark = Config.getEntry("isDarkTheme") != null ? bool.Parse(Config.getEntry("isDarkTheme")) : true;

    public static void LoadTheme(string themeUri, bool isDark = true)
    {
        App.Current.RequestedThemeVariant = isDark ? ThemeVariant.Dark : ThemeVariant.Light;
        Current.Styles.Remove(Current.Styles[2]);
        Current.Styles.Add((IStyle)AvaloniaXamlLoader.Load(
            new Uri(themeUri)
            ));
        CurrentTheme = themeUri;
        Config.setEntry("theme", themeUri);
        Config.setEntry("isDarkTheme", isDark.ToString());
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        LoadTheme(CurrentTheme, SavedIsDark);
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