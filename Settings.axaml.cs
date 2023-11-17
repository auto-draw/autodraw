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
using AvaloniaEdit;
using TextMateSharp.Grammars;
using AvaloniaEdit.TextMate;

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
        LogFile.IsCheckedChanged += LogFile_IsCheckedChanged;

        ShowPopup.IsChecked = Drawing.ShowPopup;
        AltMouseControl.IsChecked = Input.forceUio;

        // DALL-E API Keys
        SaveOpenAIKey.Click += (sender, e) => Config.setEntry("OpenAIKey", OpenAIKey.Text);

        if (Config.getEntry("showPopup") == null)
        {
            Config.setEntry("showPopup", Drawing.ShowPopup.ToString());
        }
        if (Config.getEntry("OpenAIKey") != null)
        {
            OpenAIKey.Text = Config.getEntry("OpenAIKey");
        }

        // Themes

            //  TextEditor Input
        var _textEditor1 = this.FindControl<TextEditor>("ThemeInput");
        _textEditor1.Text = @"<!-- Template Theme -->
<!--
Useful Information:
Style Selectors: https://docs.avaloniaui.net/docs/next/guides/styles-and-resources/selectors
Property Setters: https://docs.avaloniaui.net/docs/next/guides/styles-and-resources/property-setters

Troubleshooting, very useful: https://docs.avaloniaui.net/docs/next/guides/styles-and-resources/troubleshooting
-->

<Styles xmlns=""https://github.com/avaloniaui""
        xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml""
	xmlns:asyncImageLoader=""clr-namespace:AsyncImageLoader;assembly=AsyncImageLoader.Avalonia"">
		
    <!-- CLASS : Window -->
    <Style Selector=""Window"">
        <Setter Property=""Background"" Value=""#97af""/>
        <Setter Property=""TransparencyLevelHint"" Value=""AcrylicBlur""/>
    </Style>

    <!-- CLASS : Button -->
	<Style Selector=""Button"">
		<Setter Property=""Background"" Value=""#3fff""></Setter>
		<Setter Property=""Foreground"" Value=""#fff""></Setter>
	</Style>
	
    <!-- CLASS : Label -->
    <Style Selector=""Label"">
        <Setter Property=""Foreground"" Value=""#7cf""></Setter>
        <Setter Property=""FontSize"" Value=""12""/>
    </Style>

    <!-- CLASS : ListBox -->
    <Style Selector=""ListBox"">
        <Setter Property=""Background"" Value=""#c357""/>
    </Style>

    <!-- Toolbar -->
    <Style Selector=""Canvas.Toolbar"">
        <Setter Property=""Background"" Value=""#357""/>
    </Style>
    
    <!-- Image Preview -->
	<Style Selector=""Canvas.ImagePreview"">
		<Setter Property=""Background"" Value=""#6000""/>
	</Style>
	<Style Selector=""Border.ImagePreview"">
		<Setter Property=""BoxShadow"" Value=""0 0 64 -4 #caf""/>
	</Style>
	
    <!-- ComboBox Popup Background -->
    <Style Selector=""ComboBox /template/ Border#PopupBorder"">
        <Setter Property=""Background"" Value=""#357""/>
    </Style>
</Styles>";
        var _registryOptions1 = new RegistryOptions(ThemeName.DarkPlus);
        var _textMateInstallation1 = _textEditor1.InstallTextMate(_registryOptions1);
        _textMateInstallation1.SetGrammar(_registryOptions1.GetScopeByLanguageId(_registryOptions1.GetLanguageByExtension(".xml").Id));

        //  TextEditor Output
        var _textEditor2 = this.FindControl<TextEditor>("ThemeOutput");
        var _registryOptions2 = new RegistryOptions(ThemeName.DarkPlus);
        var _textMateInstallation2 = _textEditor2.InstallTextMate(_registryOptions2);
        _textMateInstallation2.SetGrammar(_registryOptions2.GetScopeByLanguageId(_registryOptions2.GetLanguageByExtension(".md").Id));

        //  Interactions

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
            Title = "Open Theme",
            FileTypeFilter = filetype,
            AllowMultiple = false,
            SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(Config.ThemesPath)
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
            Title = "Save Theme",
            FileTypeChoices = filetype,
            SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(Config.ThemesPath)
        });

        if (file is not null)
        {
            await using var stream = await file.OpenWriteAsync();
            using var streamWriter = new StreamWriter(stream);

            streamWriter.Write(ThemeInput.Text);
            savedLocation = file.TryGetLocalPath(); // set to saved file location
        }
    }

    private async void NewTheme_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        string themeText = await new StreamReader(AssetLoader.Open(new Uri("avares://Autodraw/Styles/DefaultTheme.txt"))).ReadToEndAsync();
        ThemeInput.Text = themeText;
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

    private void LogFile_IsCheckedChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (LogFile.IsChecked == null) return;
        Config.setEntry("logsEnabled", LogFile.IsChecked.ToString() ?? "True");
        Utils.LoggingEnabled = (bool)LogFile.IsChecked;
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
            myControl.Opacity = 0;
            myControl.IsHitTestVisible = false;
        }
    }

    private void OpenMenu(string menu)
    {
        var myControl = this.FindControl<Control>(menu);
        DeactivateItem(new List<string>() { "General", "Themes", "Marketplace", "Developers" });
        if (myControl == null) return;
        myControl.Opacity = 1;
        myControl.IsHitTestVisible = true;
    }

}