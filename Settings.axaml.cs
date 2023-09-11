using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using Avalonia.Platform.Storage;
using SkiaSharp;
using System.Text.RegularExpressions;

namespace Autodraw;

public partial class Settings : Window
{
    // AssetLoader.Open(new System.Uri("avares://Autodraw/Styles/dark.xaml"))

    string savedLocation = "";

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

        // Themes

        DarkLightThemeToggle.Click += ToggleTheme_Click;
        NewTheme.Click += NewTheme_Click;
        SaveTheme.Click += SaveTheme_Click;
        OpenTheme.Click += OpenTheme_Click;
        LoadTheme.Click += LoadTheme_Click;

        // Developer
    }
    FilePickerFileType[] filetype = new FilePickerFileType[] {
        new("All Theme Types") { Patterns = new[] { "*.axaml", "*.daxaml", "*.laxaml" }, MimeTypes = new[] { "*/*" } },
        new("Default Theme") { Patterns = new[] { "*.axaml" }, MimeTypes = new[] { "*/*" } },
        new("Dark Theme") { Patterns = new[] { "*.daxaml" }, MimeTypes = new[] { "*/*" } },
        new("Light Theme") { Patterns = new[] { "*.laxaml" }, MimeTypes = new[] { "*/*" } },
        FilePickerFileTypes.All
    };

    private void LoadTheme_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        string Output = App.LoadThemeFromString(ThemeInput.Text, ThemeIsDark.IsChecked == true, savedLocation);
        ThemeOutput.Text = Output;
    }

    private async void OpenTheme_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var file = await this.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Image",
            FileTypeFilter = filetype,
            AllowMultiple = false
        });

        if (file.Count == 1)
        {
            string fileType = Regex.Match(file[0].TryGetLocalPath(), "\\.[^.\\\\/:*?\"<>|\\r\\n]+$").Value;
            if(fileType == ".daxaml") { ThemeIsDark.IsChecked = true; } else
            if(fileType == ".laxaml") { ThemeIsDark.IsChecked = false; }
            Stream stream = await file[0].OpenReadAsync();
            ThemeInput.Text = new StreamReader(stream).ReadToEnd();
            stream.Close();
            savedLocation = file[0].TryGetLocalPath();
        }
    }

    private async void SaveTheme_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Config",
            FileTypeChoices = filetype
        });

        if (file is not null)
        {
            await using var stream = await file.OpenWriteAsync();
            using var streamWriter = new StreamWriter(stream);

            streamWriter.Write(ThemeInput.Text);
            savedLocation = file.TryGetLocalPath(); // set to saved file location
        }
    }

    private void NewTheme_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ThemeInput.Text = new StreamReader(AssetLoader.Open(new Uri("avares://Autodraw/Styles/DefaultTheme.txt"))).ReadToEnd();
        savedLocation = "";
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
            App.LoadTheme("avares://Autodraw/Styles/dark.axaml", true);
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