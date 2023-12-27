using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using AvaloniaEdit;
using AvaloniaEdit.TextMate;
using TextMateSharp.Grammars;

namespace Autodraw;

public partial class Settings : Window
{
    private readonly FilePickerFileType[] filetype =
    {
        new("All Theme Types") { Patterns = new[] { "*.axaml", "*.daxaml", "*.laxaml" }, MimeTypes = new[] { "*/*" } },
        new("Default Theme") { Patterns = new[] { "*.axaml" }, MimeTypes = new[] { "*/*" } },
        new("Dark Theme") { Patterns = new[] { "*.daxaml" }, MimeTypes = new[] { "*/*" } },
        new("Light Theme") { Patterns = new[] { "*.laxaml" }, MimeTypes = new[] { "*/*" } },
        FilePickerFileTypes.All
    };
    
    private string savedLocation = ""; 

    public Settings()
    {
        InitializeComponent();
        // Main Handle
        CloseAppButton.Click += CloseAppButton_Click;
        // Sidebar
        SettingsTabs.SelectionChanged += SettingsTabs_OnSelectionChanged;

        // General
        AltMouseControl.IsCheckedChanged += AltMouseControl_IsCheckedChanged;
        ShowPopup.IsCheckedChanged += ShowPopup_IsCheckedChanged;
        NoRescan.IsCheckedChanged += NoRescanOnIsCheckedChanged;
        LogFile.IsCheckedChanged += LogFile_IsCheckedChanged;

        ShowPopup.IsChecked = Drawing.ShowPopup;
        AltMouseControl.IsChecked = Input.forceUio;
        NoRescan.IsChecked = Drawing.NoRescan;
        LogFile.IsChecked = Config.getEntry("logsEnabled") == "True";

        // DALL-E API Keys
        SaveOpenAiKey.Click += (sender, e) => Config.setEntry("OpenAIKey", OpenAiKey.Text);
        RevealAiKey.Click += (sender, e) => OpenAiKey.RevealPassword = !OpenAiKey.RevealPassword;

        if (Config.getEntry("showPopup") == null) Config.setEntry("showPopup", Drawing.ShowPopup.ToString());
        if (Config.getEntry("OpenAIKey") != null) OpenAiKey.Text = Config.getEntry("OpenAIKey");

        // Themes
        ListThemes();

        //  TextEditor Input
        var _textEditor1 = this.FindControl<TextEditor>("ThemeInput");
        var _registryOptions1 = new RegistryOptions(ThemeName.DarkPlus);
        var _textMateInstallation1 = _textEditor1.InstallTextMate(_registryOptions1);
        _textMateInstallation1.SetGrammar(
            _registryOptions1.GetScopeByLanguageId(_registryOptions1.GetLanguageByExtension(".xml").Id));

        //  TextEditor Output
        var _textEditor2 = this.FindControl<TextEditor>("ThemeOutput");
        var _registryOptions2 = new RegistryOptions(ThemeName.DarkPlus);
        var _textMateInstallation2 = _textEditor2.InstallTextMate(_registryOptions2);
        _textMateInstallation2.SetGrammar(
            _registryOptions2.GetScopeByLanguageId(_registryOptions2.GetLanguageByExtension(".md").Id));

        //  Interactions

        DarkLightThemeToggle.Click += ToggleTheme_Click;
        NewTheme.Click += NewTheme_Click;
        SaveTheme.Click += SaveTheme_Click;
        OpenTheme.Click += OpenTheme_Click;
        LoadTheme.Click += LoadTheme_Click;
        // Developer
    }


    private void CloseAppButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private Grid? currentlyViewing;
    private void SettingsTabs_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        
        TreeViewItem select = (TreeViewItem)SettingsTabs.SelectedItem;
        string? selectionName = select.Name;
        if (selectionName == "" || selectionName is null || selectionName == " ") return;
        string selectionTabName = Regex.Replace(selectionName, "Selector$","")+"Tab";
        var selectionTab = this.FindControl<Grid>(selectionTabName);
        if (selectionTab is null) return;
        selectionTab.Opacity = 1;
        selectionTab.IsHitTestVisible = true;
        if (currentlyViewing is not null && currentlyViewing != selectionTab)
        {
            currentlyViewing.Opacity = 0;
            currentlyViewing.IsHitTestVisible = false;
        }

        currentlyViewing = selectionTab;
    }

    private void LoadTheme_Click(object? sender, RoutedEventArgs e)
    {
        var Output = App.LoadThemeFromString(ThemeInput.Text, true, savedLocation);
        ThemeOutput.Text = Output;
    }

    private void ListThemes()
    {
        string[] extensions = { ".axaml", ".laxaml", ".daxaml" };
        string[] dirFiles = Directory.GetFiles(Config.FolderPath + "/Themes");
        var files = dirFiles.Where(file =>
            extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase));

        foreach (var file in files)
        {
            // Make your code to list it, however you do it
            // Name is 'Path.GetFileNameWithoutExtension(file)'
            // Path is 'file'
        }
    }

    private async void OpenTheme_Click(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Theme",
            FileTypeFilter = filetype,
            AllowMultiple = false,
            SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(Config.ThemesPath)
        });

        if (file.Count == 1)
        {
            var fileType = Regex.Match(file[0].TryGetLocalPath(), "\\.[^.\\\\/:*?\"<>|\\r\\n]+$").Value;
            var stream = await file[0].OpenReadAsync();
            ThemeInput.Text = new StreamReader(stream).ReadToEnd();
            stream.Close();
            savedLocation = file[0].TryGetLocalPath();
        }
    }

    private async void SaveTheme_Click(object? sender, RoutedEventArgs e)
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

    private async void NewTheme_Click(object? sender, RoutedEventArgs e)
    {
        var themeText = await new StreamReader(AssetLoader.Open(new Uri("avares://Autodraw/Styles/DefaultTheme.txt")))
            .ReadToEndAsync();
        ThemeInput.Text = themeText;
        savedLocation = "";
    }

    private void ToggleTheme_Click(object? sender, RoutedEventArgs e)
    {
        if (App.CurrentTheme == "avares://Autodraw/Styles/dark.axaml")
            App.LoadTheme("avares://Autodraw/Styles/light.axaml", false);
        else
            App.LoadTheme("avares://Autodraw/Styles/dark.axaml");
    }
    
    private void ShowPopup_IsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (ShowPopup.IsChecked == null) return;
        Drawing.ShowPopup = (bool)ShowPopup.IsChecked;
        Config.setEntry("showPopup", ShowPopup.IsChecked.ToString() ?? "true");
    }

    private void AltMouseControl_IsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (AltMouseControl.IsChecked == null) return;
        Input.forceUio = (bool)AltMouseControl.IsChecked;
    }

    private void NoRescanOnIsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (NoRescan.IsChecked == null) return;
        Drawing.NoRescan = (bool)NoRescan.IsChecked;
    }

    private void LogFile_IsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (LogFile.IsChecked == null) return;
        Config.setEntry("logsEnabled", LogFile.IsChecked.ToString() ?? "True");
        Utils.LoggingEnabled = (bool)LogFile.IsChecked;
    }
}