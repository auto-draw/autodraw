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

        // Main Handle
        CloseAppButton.Click += CloseAppButton_Click;

        // Sidebar
        GeneralMenuButton.Click += (sender, e) => OpenMenu("General");
        ThemeMenuButton.Click += (sender, e) => OpenMenu("Themes");
        MarketplaceButton.Click += (sender, e) => OpenMenu("Marketplace");
        DevButton.Click += (sender, e) => OpenMenu("Developers");

        // General
        AltMouseControl.IsCheckedChanged += AltMouseControl_IsCheckedChanged;
        ShowPopup.IsCheckedChanged += ShowPopup_IsCheckedChanged;

        ShowPopup.IsChecked = Drawing.ShowPopup;
        AltMouseControl.IsChecked = Input.forceUio;

        if (Config.getEntry("showPopup") == null)
        {
            Config.setEntry("showPopup", Drawing.ShowPopup.ToString());
        }

        // Developer
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