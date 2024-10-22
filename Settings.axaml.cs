using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using AvaloniaEdit;
using AvaloniaEdit.TextMate;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SharpHook;
using SharpHook.Native;
using TextMateSharp.Grammars;

namespace Autodraw;

public partial class Settings : Window
{
    private readonly FilePickerFileType[] filetype =
    {
        new("All Theme Types") { Patterns = new[] { "*.axaml", "*.daxaml", "*.laxaml" }, MimeTypes = new[] { "*/*" } },
        new("Default Theme") { Patterns = new[] { "*.axaml" }, MimeTypes = new[] { "*/*" } },
        FilePickerFileTypes.All
    };
    
    private string _savedLocation = "";
    private bool _textmateLoaded;
    private bool _currentlyAwaitingKeypress;

    public Settings()
    {
        InitializeComponent();
        
        LoadLocalThemeItems();
        
        // Main Handle
        CloseAppButton.Click += CloseAppButton_Click;
        // Sidebar
        SettingsTabs.PropertyChanged += SettingsTabsOnPropertyChanged;

        // General
        AltMouseControl.IsCheckedChanged += AltMouseControl_IsCheckedChanged;
        ShowPopup.IsCheckedChanged += ShowPopup_IsCheckedChanged;
        NoRescan.IsCheckedChanged += NoRescanOnIsCheckedChanged;
        LogFile.IsCheckedChanged += LogFile_IsCheckedChanged;

        ShowPopup.IsChecked = Drawing.ShowPopup;
        AltMouseControl.IsChecked = Input.forceUio;
        NoRescan.IsChecked = Drawing.NoRescan;
        LogFile.IsChecked = Config.GetEntry("logsEnabled") == "True";
        
        //  Keybinds
        ChangeKeybind_StartDrawing.Content = Config.Keybind_StartDrawing;
        ChangeKeybind_StartDrawing.Click += async (sender, args) =>
        {
            if (_currentlyAwaitingKeypress) return;
            ChangeKeybind_StartDrawing.Content = "Waiting...";
            var keybind = await ChangeKeybind_OnClick();
            Config.Keybind_StartDrawing = keybind;
            Config.SetEntry("Keybind_StartDrawing", keybind.ToString());
            ChangeKeybind_StartDrawing.Content = Config.Keybind_StartDrawing;
        };
        
        ChangeKeybind_StopDrawing.Content = Config.Keybind_StopDrawing;
        ChangeKeybind_StopDrawing.Click += async (sender, args) =>
        {
            if (_currentlyAwaitingKeypress) return;
            ChangeKeybind_StopDrawing.Content = "Waiting...";
            var keybind = await ChangeKeybind_OnClick();
            Config.Keybind_StopDrawing = keybind;
            Config.SetEntry("Keybind_StopDrawing", keybind.ToString());
            ChangeKeybind_StopDrawing.Content = Config.Keybind_StopDrawing;
        };
        
        ChangeKeybind_PauseDrawing.Content = Config.Keybind_PauseDrawing;
        ChangeKeybind_PauseDrawing.Click += async (sender, args) =>
        {
            if (_currentlyAwaitingKeypress) return;
            ChangeKeybind_PauseDrawing.Content = "Waiting...";
            var keybind = await ChangeKeybind_OnClick();
            Config.Keybind_PauseDrawing = keybind;
            Config.SetEntry("Keybind_PauseDrawing", keybind.ToString());
            ChangeKeybind_PauseDrawing.Content = Config.Keybind_PauseDrawing;
        };
        
        ChangeKeybind_LockPreview.Content = Config.Keybind_LockPreview;
        ChangeKeybind_LockPreview.Click += async (sender, args) =>
        {
            if (_currentlyAwaitingKeypress) return;
            ChangeKeybind_LockPreview.Content = "Waiting...";
            var keybind = await ChangeKeybind_OnClick();
            Config.Keybind_LockPreview = keybind;
            Config.SetEntry("Keybind_LockPreview", keybind.ToString());
            ChangeKeybind_LockPreview.Content = Config.Keybind_LockPreview;
        };
        
        ChangeKeybind_SkipBacktrace.Content = Config.Keybind_SkipRescan;
        ChangeKeybind_SkipBacktrace.Click += async (sender, args) =>
        {
            if (_currentlyAwaitingKeypress) return;
            ChangeKeybind_SkipBacktrace.Content = "Waiting...";
            var keybind = await ChangeKeybind_OnClick();
            Config.Keybind_SkipRescan = keybind;
            Config.SetEntry("Keybind_SkipRescan", keybind.ToString());
            ChangeKeybind_SkipBacktrace.Content = Config.Keybind_SkipRescan;
        };
        
        // Marketplace
        MarketplaceTabs.PropertyChanged += MarketplaceTabsOnPropertyChanged;

        // DALL-E API Keys
        SaveOpenAiKey.Click += (sender, e) => Config.SetEntry("OpenAIKey", OpenAiKey.Text);
        RevealAiKey.Click += (sender, e) => OpenAiKey.RevealPassword = !OpenAiKey.RevealPassword;

        if (Config.GetEntry("showPopup") == null) Config.SetEntry("showPopup", Drawing.ShowPopup.ToString());
        if (Config.GetEntry("OpenAIKey") != null) OpenAiKey.Text = Config.GetEntry("OpenAIKey");

        //  Interactions

        DarkLightThemeToggle.Click += ToggleTheme_Click;
        NewTheme.Click += NewTheme_Click;
        SaveTheme.Click += SaveTheme_Click;
        OpenTheme.Click += OpenTheme_Click;
        LoadTheme.Click += LoadTheme_Click;
        
        //  Configuration
        ThemesLocationTextBox.Text = Config.ThemesPath;
        ThemesLocationFolderButton.Click += ThemesLocationFolderButtonOnClick;
        ThemesLocationSaveButton.Click += ThemesLocationSaveButtonOnClick;

        ImageCacheLocationTextBox.Text = Config.CachePath;
        ImageCacheLocationFolderButton.Click += ImageCacheLocationFolderButtonOnClick;
        ImageCacheLocationSaveButton.Click += ImageCacheLocationSaveButtonOnClick;
        ImageCacheLocationClearButton.Click += ImageCacheLocationClearButtonOnClick;
    }

    private void MarketplaceTabsOnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property.ToString() != "SelectedIndex") return;
        if (MarketplaceTabs.SelectedIndex == 0)
        {
            LoadLocalThemeItems();
        }

        if (MarketplaceTabs.SelectedIndex == 1)
        {
            LoadOnlineThemeItems();
        }
    }
    
    private void SettingsTabsOnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property.ToString() != "SelectedItem") return;
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
        
        // Marketplace Loading Stuff:
        if (selectionName == "MarketplaceSelector")
        {
            if (MarketplaceTabs.SelectedIndex == 0)
            {
                LoadLocalThemeItems();
            }

            if (MarketplaceTabs.SelectedIndex == 1)
            {
                LoadOnlineThemeItems();
            }
        }
        
        // Theme Editor Loading Stuff:
        if (selectionName == "ThemeEditorSelector")
        {
            // Moved this here for performance reasons :P

            if (!_textmateLoaded)
            {
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
            
                _textmateLoaded = true;
            }
        }

        currentlyViewing = selectionTab;
    }

    private Task<KeyCode> ChangeKeybind_OnClick()
    {
        _currentlyAwaitingKeypress = true;

        var tcs = new TaskCompletionSource<KeyCode>();

        void handler(object? sender, KeyboardHookEventArgs e)
        {
            Input.taskHook.KeyPressed -= handler;
            _currentlyAwaitingKeypress = false;
            tcs.SetResult(e.Data.KeyCode);
        };
    
        Input.taskHook.KeyPressed += handler;

        return tcs.Task;
    }

    // Image Cache

    private void ImageCacheLocationClearButtonOnClick(object? sender, RoutedEventArgs e)
    {
        string[] cachedImages = Directory.GetFiles(Config.CachePath, "*.jpeg", SearchOption.AllDirectories);
        foreach (string cachedImage in cachedImages)
        {
            try
            {
                File.Delete(cachedImage);
            }
            catch (UnauthorizedAccessException)
            {
                new MessageBox().ShowMessageBox("Error!",
                    "Appears the location provided may be a protected folder. Unable to clear cache automatically.");
                break;
            }
            catch (Exception ex)
            {
                Utils.Log(ex.ToString());
                return;
            }
        }
    }

    private void ImageCacheLocationSaveButtonOnClick(object? sender, RoutedEventArgs e)
    {
        if (ImageCacheLocationTextBox.Text == Config.CachePath) return;
        if (Directory.Exists(ImageCacheLocationTextBox.Text) is false)
        {
            new MessageBox().ShowMessageBox("Error!", "Please provide a valid location!");
            return;
        }
        if (Directory.GetFiles(ImageCacheLocationTextBox.Text,"*",SearchOption.AllDirectories).Length != 0)
        {
            // Already KNOW someone's going to put the Cache location in somewhere unsafe like C:/ or System32.
            new MessageBox().ShowMessageBox("Error!", "Please ensure the folder is empty!");
            return;
        }

        Config.CachePath = Path.GetFullPath(ImageCacheLocationTextBox.Text);
        Config.SetEntry("SavedCachePath", Config.CachePath);
        ImageCacheLocationTextBox.Text = Config.CachePath;
    }

    private async void ImageCacheLocationFolderButtonOnClick(object? sender, RoutedEventArgs e)
    {
        var folder = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { AllowMultiple = false });
        if (folder.Count != 1) return;
        ImageCacheLocationTextBox.Text = folder[0].TryGetLocalPath();
    }
    
    // Themes Location

    private void ThemesLocationSaveButtonOnClick(object? sender, RoutedEventArgs e)
    {
        if (Directory.Exists(ThemesLocationTextBox.Text) is false)
        {
            new MessageBox().ShowMessageBox("Error!", "Please provide a valid location!");
            return;
        }

        Config.ThemesPath = Path.GetFullPath(ThemesLocationTextBox.Text);
        Config.SetEntry("SavedThemesPath", Config.ThemesPath);
        ThemesLocationTextBox.Text = Config.ThemesPath;
    }

    private async void ThemesLocationFolderButtonOnClick(object? sender, RoutedEventArgs e)
    {
        var folder = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { AllowMultiple = false });
        if (folder.Count != 1) return;
        ThemesLocationTextBox.Text = folder[0].TryGetLocalPath();
    }

    // Main Stuff
    
    private void CloseAppButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private Grid? currentlyViewing;
    
    public class listedTheme
    {
        public string Title { get; set; }
        public string Author { get; set; }
        public string Description { get; set; }
        public string Image { get; set; }
        
        public string ButtonParameter { get; set; }
    }

    public async void LoadOnlineTheme(object? data, RoutedEventArgs routedEventArgs)
    {
        // This is such a stupid way of doing this but I really am out of ideas :P
        string rawJsonData = (string)((Button)data).CommandParameter;
        JObject JsonData = JObject.Parse(rawJsonData);
        string FileLocation = await Marketplace.Download(Convert.ToInt32(JsonData.GetValue("id").ToString()));
        string fileName = Path.GetFileNameWithoutExtension(FileLocation);
        File.WriteAllText(Path.Combine(Config.ThemesPath, fileName + "-Data.json"),rawJsonData);
    }

    private async void LoadOnlineThemeItems()
    {
        MarketplacePleaseWait.Opacity = 1;
        OnlineThemes.Items.Clear();
        var MarketplaceList = await Marketplace.List("theme");
        MarketplacePleaseWait.Opacity = 0;
        foreach (JObject themeData in MarketplaceList)
        {
            string title = (string)themeData.GetValue("name") ?? "Title";
            string description = (string)themeData.GetValue("description")  ?? "";
            string image = $"https://auto-draw.com/ugc/{themeData.GetValue("author")}/{themeData.GetValue("id")}.png";
            string author = (string)themeData.GetValue("username") ?? "Unknown";
            
            var data = new Dictionary<string, string>()
            {
                { "title", title },
                { "desc", description },
                { "image", image },
                { "author", author },
                { "id", themeData.GetValue("id").ToString() }
            };

            string json = JsonConvert.SerializeObject(data);
            
            listedTheme listData = new listedTheme();
            listData.Title = title;
            listData.Description = description;
            listData.Image = image;
            listData.Author = "Theme by "+author;
            listData.ButtonParameter = json ?? "";
            OnlineThemes.Items.Add(listData);
            
        }
    }

    public void LoadLocalTheme(object? data, RoutedEventArgs routedEventArgs)
    {
        // This is such a stupid way of doing this but I really am out of ideas :P
        string location = (string)((Button)data).CommandParameter;
        App.LoadTheme(location);
        
    }
    
    private void LoadLocalThemeItems()
    {
        InstalledThemes.Items.Clear();
        string[] extensions = { ".axaml", ".laxaml", ".daxaml" };
        string[] dirFiles = Directory.GetFiles(Config.ThemesPath,"*",SearchOption.AllDirectories);
        var themes = dirFiles.Where(file =>
            extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase));
        foreach (string theme in themes)
        {
            string fileName = Path.GetFileName(theme);
            string title = fileName;
            string description = "";
            string image = "";
            string author = "Theme is Locally Stored";

            string parent = Directory.GetParent(theme).FullName;

            if (File.Exists(Path.Combine(parent, Path.GetFileNameWithoutExtension(theme) + "-Image.jpeg")))
            {
                image = Path.Combine(parent, Path.GetFileNameWithoutExtension(theme) + "-Image.jpeg");
            }
            if (File.Exists(Path.Combine(parent, Path.GetFileNameWithoutExtension(theme) + "-Data.json")))
            {
                string rawJsonData = File.ReadAllText(Path.Combine(parent, Path.GetFileNameWithoutExtension(theme) + "-Data.json"));
                JObject JsonData = JObject.Parse(rawJsonData);
                if (JsonData.ContainsKey("title"))
                {
                    title = (string)JsonData.GetValue("title");
                }
                if (JsonData.ContainsKey("desc"))
                {
                    description = (string)JsonData.GetValue("desc");
                }
                if (JsonData.ContainsKey("author"))
                {
                    author = (string)JsonData.GetValue("author");
                }
                if (JsonData.ContainsKey("image"))
                {
                    image = (string)JsonData.GetValue("image");
                }
            }
                    
            listedTheme listData = new listedTheme();
            listData.Title = title;
            listData.Description = description;
            listData.Image = image;
            listData.Author = author;
            listData.ButtonParameter = Path.GetFullPath(theme);
            InstalledThemes.Items.Add(listData);
        }
    }

    private void LoadTheme_Click(object? sender, RoutedEventArgs e)
    {
        var Output = App.LoadThemeFromString(ThemeInput.Text, true, _savedLocation);
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
            _savedLocation = file[0].TryGetLocalPath();
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
            _savedLocation = file.TryGetLocalPath(); // set to saved file location
        }
    }

    private async void NewTheme_Click(object? sender, RoutedEventArgs e)
    {
        var themeText = await new StreamReader(AssetLoader.Open(new Uri("avares://Autodraw/Styles/DefaultTheme.txt")))
            .ReadToEndAsync();
        ThemeInput.Text = themeText;
        _savedLocation = "";
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
        Config.SetEntry("showPopup", ShowPopup.IsChecked.ToString() ?? "true");
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
        Config.SetEntry("logsEnabled", LogFile.IsChecked.ToString() ?? "True");
        Utils.LoggingEnabled = (bool)LogFile.IsChecked;
    }

    private void LocalThemeOnClick(object? sender, RoutedEventArgs e)
    {
        throw new NotImplementedException();
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && e.GetPosition(this).Y <= 20)
            BeginMoveDrag(e);
    }

}