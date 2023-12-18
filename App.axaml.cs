using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace Autodraw;

public class App : Application
{
    public static string CurrentTheme = Config.getEntry("theme") ?? "avares://Autodraw/Styles/dark.axaml";

    public static bool SavedIsDark =
        Config.getEntry("isDarkTheme") == null || bool.Parse(Config.getEntry("isDarkTheme") ?? "true");

    private static void ThemeFailed()
    {
        // This function is for if it failed to load a theme, will revert to previous, or will decide to use darkmode if all else fails.
        try
        {
            // Tries loading back to our original loaded theme.
            var Resource = (IStyle)AvaloniaXamlLoader.Load(
                new Uri(CurrentTheme)
            );
            Current.RequestedThemeVariant = SavedIsDark ? ThemeVariant.Dark : ThemeVariant.Light;
            if (Current.Styles.Count > 4)
                Current.Styles.Remove(Current.Styles[4]);
            Current.Styles.Add(Resource);
            Config.setEntry("theme", CurrentTheme);
            Config.setEntry("isDarkTheme", SavedIsDark.ToString());
        }
        catch
        {
            // Tries loading our default theme. Purpose of this is if a theme somehow vanished.
            var Resource = (IStyle)AvaloniaXamlLoader.Load(
                new Uri("avares://Autodraw/Styles/dark.axaml")
            );
            Current.RequestedThemeVariant = ThemeVariant.Dark;
            if (Current.Styles.Count > 4)
                Current.Styles.Remove(Current.Styles[4]);
            Current.Styles.Add(Resource);
            CurrentTheme = "avares://Autodraw/Styles/dark.axaml";
            SavedIsDark = true;
            Config.setEntry("theme", "avares://Autodraw/Styles/dark.axaml");
            Config.setEntry("isDarkTheme", true.ToString());
        }
    }

    public static string? LoadThemeFromString(string themeText, bool isDark = true, string themeUri = "")
    {
        var OutputMessage = "";
        try
        {
            // Tries loading as runtime uncompiled.
            var TextInput = themeText;
            TextInput = Regex.Replace(TextInput, @"file:./", AppDomain.CurrentDomain.BaseDirectory);
            if (themeUri != "")
            {
                Debug.WriteLine(themeUri);
                TextInput = Regex.Replace(TextInput, @"style:./",
                    Regex.Replace(themeUri, @"\\(?:.(?!\\))+$", "") + "\\");
                Debug.WriteLine(TextInput);
            }
            else
            {
                OutputMessage += "- You have not saved this theme, so it won't parse style:./.\n\n";
            }

            var Resource = AvaloniaRuntimeXamlLoader.Parse<Styles>(
                TextInput
            );
            Current.RequestedThemeVariant = isDark ? ThemeVariant.Dark : ThemeVariant.Light;
            Current.Styles.Remove(Current.Styles[4]);
            Current.Styles.Add(Resource);
            if (themeUri != "")
            {
                CurrentTheme = themeUri;
                SavedIsDark = isDark;
                Config.setEntry("theme", themeUri);
                Config.setEntry("isDarkTheme", isDark.ToString());
            }
        }
        catch (Exception ex)
        {
            ThemeFailed();
            OutputMessage += "# Theme has failed to load successfully due to an error.\n" + ex.Message;
            return OutputMessage;
        }

        OutputMessage += "# Theme loaded successfully!\n";
        return OutputMessage;
    }

    public static string? LoadTheme(string themeUri, bool isDark = true)
    {
        // Behold, terrible bruteforce-ey code! Performance be damned!
        try
        {
            // Tries loading as Compiled.
            var Resource = (IStyle)AvaloniaXamlLoader.Load(
                new Uri(themeUri)
            );
            Current.RequestedThemeVariant = isDark ? ThemeVariant.Dark : ThemeVariant.Light;
            Current.Styles.Remove(Current.Styles[4]);
            Current.Styles.Add(Resource);
            CurrentTheme = themeUri;
            SavedIsDark = isDark;
            Config.setEntry("theme", themeUri);
            Config.setEntry("isDarkTheme", isDark.ToString());
        }
        catch
        {
            try
            {
                // Tries loading as runtime uncompiled.
                var TextInput = File.ReadAllText(themeUri);
                TextInput = Regex.Replace(TextInput, @"file:./", AppDomain.CurrentDomain.BaseDirectory);
                TextInput = Regex.Replace(TextInput, @"style:./", Regex.Replace(themeUri, @"\\(?:.(?!\\))+$", ""));

                var Resource = AvaloniaRuntimeXamlLoader.Parse<Styles>(
                    TextInput
                ); 
                Current.RequestedThemeVariant = isDark ? ThemeVariant.Dark : ThemeVariant.Light;
                Current.Styles.Remove(Current.Styles[4]);
                Current.Styles.Add(Resource);
                CurrentTheme = themeUri;
                SavedIsDark = isDark;
                Config.setEntry("theme", themeUri);
                Config.setEntry("isDarkTheme", isDark.ToString());
            }
            catch (Exception ex)
            {
                ThemeFailed();
                return ex.Message;
            }
        }

        return null;
    }

    public override void Initialize()
    {
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
        AvaloniaXamlLoader.Load(this);
        LoadTheme(CurrentTheme, SavedIsDark);
    }

    private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Utils.Log(e.Exception.ToString());
        Utils.Log(e.Exception.Message);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow();

        base.OnFrameworkInitializationCompleted();
    }
}