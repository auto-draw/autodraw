using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using System;
using System.Diagnostics;
using System.Collections.Generic;

namespace Autodraw;

public partial class Settings : Window
{
    // AssetLoader.Open(new System.Uri("avares://Autodraw/Styles/dark.xaml"))

    public Settings()
    {
        InitializeComponent();

        ToggleTheme.Click += ToggleTheme_Click;

        GeneralMenuButton.Click += (sender, e) => OpenMenu("General");
        ThemeMenuButton.Click += (sender, e) => OpenMenu("Themes");
        MarketplaceButton.Click += (sender, e) => OpenMenu("Marketplace");
        DevButton.Click += (sender, e) => OpenMenu("Developers");

        AltMouseControl.IsCheckedChanged += AltMouseControl_IsCheckedChanged;
        ShowPopup.IsCheckedChanged += ShowPopup_IsCheckedChanged;

        BlueTheme.Click += BlueTheme_Click;
        AnimeTheme.Click += AnimeTheme_Click;
        LandscapeTheme.Click += LandscapeTheme_Click;

        CloseAppButton.Click += CloseAppButton_Click;

        ShowPopup.IsChecked = Drawing.ShowPopup;
        AltMouseControl.IsChecked = Input.forceUio;

        if (Config.getEntry("showPopup") == null)
        {
            System.Diagnostics.Debug.WriteLine("Hi3");
            Config.setEntry("showPopup", Drawing.ShowPopup.ToString());
        }
    }

    private void ShowPopup_IsCheckedChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (ShowPopup.IsChecked == null) return;
        Drawing.ShowPopup = (bool)ShowPopup.IsChecked;
        Config.setEntry("showPopup", ShowPopup.IsChecked.ToString() ?? "true");
    }

    private void AltMouseControl_IsCheckedChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (AltMouseControl.IsChecked == null) return;
        Input.forceUio = (bool)AltMouseControl.IsChecked;
    }

    private void LandscapeTheme_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        App.LoadTheme("avares://Autodraw/Styles/landscape.axaml");
    }

    private void AnimeTheme_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        App.LoadTheme("avares://Autodraw/Styles/anime.axaml");
    }

    private void BlueTheme_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        App.LoadTheme("avares://Autodraw/Styles/blue.axaml");
    }


    private void ToggleTheme_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (App.CurrentTheme == "avares://Autodraw/Styles/dark.axaml")
        {
            App.LoadTheme("avares://Autodraw/Styles/light.axaml", false);
        }
        else
        {
            App.LoadTheme("avares://Autodraw/Styles/dark.axaml");
        }
    }


    private void CloseAppButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close();
    }

    private void DeactivateItem(List<string> menus)
    {
        foreach (var menu in menus)
        {
            var myControl = this.FindControl<Control>(menu);
            if (myControl == null) continue;
            myControl.IsVisible = false;
        }
    }

    private void OpenMenu(string menu)
    {
        var myControl = this.FindControl<Control>(menu);
        DeactivateItem(new List<string>() { "General", "Themes", "Marketplace", "Developers" });
        if (myControl == null) return;
        myControl.IsVisible = true;
    }
}