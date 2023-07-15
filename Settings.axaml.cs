using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using System;

namespace Autodraw;

public partial class Settings : Window
{
    // AssetLoader.Open(new System.Uri("avares://Autodraw/Styles/dark.xaml"))

    public Settings()
    {
        InitializeComponent();
        ToggleTheme.Click += ToggleTheme_Click;
        BlueTheme.Click += BlueTheme_Click;
        AnimeTheme.Click += AnimeTheme_Click;
        CloseAppButton.Click += CloseAppButton_Click;
    }

    private void CloseAppButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        this.Close();
    }

    private void AnimeTheme_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        App.LoadTheme(new Uri("avares://Autodraw/Styles/anime.axaml"));
        App.Current.RequestedThemeVariant = ThemeVariant.Dark;
    }

    private void BlueTheme_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        App.LoadTheme(new Uri("avares://Autodraw/Styles/blue.axaml"));
        App.Current.RequestedThemeVariant = ThemeVariant.Dark;
    }

    private void ToggleTheme_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (App.CurrentTheme == new Uri("avares://Autodraw/Styles/dark.axaml"))
        {
            App.LoadTheme(new Uri("avares://Autodraw/Styles/light.axaml"));
            App.Current.RequestedThemeVariant = ThemeVariant.Light; // I hate that I have to do this but as of Avalonia 11, you can't override some things... :(
        }
        else
        {
            App.LoadTheme(new Uri("avares://Autodraw/Styles/dark.axaml"));
            App.Current.RequestedThemeVariant = ThemeVariant.Dark;
        }
    }
}