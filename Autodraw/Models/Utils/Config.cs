using System;
using System.IO;
using System.Numerics;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SharpHook.Data;

namespace Autodraw.Models.Utils;

public abstract class Config
{
    public static string FolderPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutoDraw");

    public static object Keybind_StartDrawing = KeyCode.VcLeftShift;
    public static object Keybind_StopDrawing = KeyCode.VcLeftAlt;
    public static object Keybind_PauseDrawing = KeyCode.VcBackslash;
    public static object Keybind_SkipRescan = KeyCode.VcBackspace;
    public static object Keybind_LockPreview = KeyCode.VcLeftControl;
    public static KeyCode Keybind_ClearLock = KeyCode.VcBackQuote;
    
    public static Vector2 Preview_LastLockPos = new(0,0);

    public static string ConfigPath = Path.Combine(FolderPath, "config.json");
    public static string ThemesPath = Path.Combine(FolderPath, "Themes");
    public static string CachePath = Path.Combine(FolderPath, "Cache");

    public static void Initialize()
    {
        Directory.CreateDirectory(FolderPath);
        if (!File.Exists(ConfigPath))
        {
            JObject obj = new();
            // Migrates old directory list path (from AutoDraw v1) to the new config file
            var OldPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AutoDraw");
            if (File.Exists(Path.Combine(OldPath, "dir.txt")) &&
                File.ReadAllText(Path.Combine(OldPath, "dir.txt")).Length != 0)
                obj.Add("ConfigFolder", File.ReadAllText(Path.Combine(OldPath, "dir.txt")));
            var emptyJObject = JsonConvert.SerializeObject(obj);
            File.WriteAllText(ConfigPath, emptyJObject);
            
        }

        Logs.Copy(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Styles"), ThemesPath);
        
        // Check Configuration Path for Themes
        if (GetEntry("SavedThemesPath") is null || !Directory.Exists(GetEntry("SavedPath")))
        {
            Directory.CreateDirectory(ThemesPath);
            SetEntry("SavedThemesPath", ThemesPath);
        }
        else
        {
            ThemesPath = GetEntry("SavedThemesPath")!;
        }
        
        // Check Configuration Path for Cache
        if (GetEntry("SavedCachePath") is null || !Directory.Exists(GetEntry("SavedPath")))
        {
            Directory.CreateDirectory(CachePath);
            SetEntry("SavedCachePath", CachePath);
        }
        else
        {
            CachePath = GetEntry("SavedCachePath")!;
        }
        
        // Get Keybinds
        if (GetEntry("Keybind_StartDrawing") is not null)
        {
            Keybind_StartDrawing = (KeyCode)Enum.Parse(typeof(KeyCode), GetEntry("Keybind_StartDrawing")!);
        }
        if (GetEntry("Keybind_StopDrawing") is not null)
        {
            Keybind_StopDrawing = (KeyCode)Enum.Parse(typeof(KeyCode), GetEntry("Keybind_StopDrawing")!);
        }
        if (GetEntry("Keybind_PauseDrawing") is not null)
        {
            Keybind_PauseDrawing = (KeyCode)Enum.Parse(typeof(KeyCode), GetEntry("Keybind_PauseDrawing")!);
        }
        if (GetEntry("Keybind_SkipRescan") is not null)
        {
            Keybind_SkipRescan = (KeyCode)Enum.Parse(typeof(KeyCode), GetEntry("Keybind_SkipRescan")!);
        }
        if (GetEntry("Keybind_LockPreview") is not null)
        {
            Keybind_LockPreview = (KeyCode)Enum.Parse(typeof(KeyCode), GetEntry("Keybind_LockPreview")!);
        }
        if (GetEntry("Keybind_ClearLock") is not null)
        {
            Keybind_ClearLock = (KeyCode)Enum.Parse(typeof(KeyCode), GetEntry("Keybind_ClearLock")!);
        }
        
        if (GetEntry("Preview_LastLockedX") is not null && GetEntry("Preview_LastLockedY") is not null )
        {
            Preview_LastLockPos = new Vector2(int.Parse(GetEntry("Preview_LastLockedX")!),int.Parse(GetEntry("Preview_LastLockedY")!));
        }
        
    }

    public static string? GetEntry(string entry)
    {
        if (!File.Exists(ConfigPath)) return null;
        var json = File.ReadAllText(ConfigPath);
        var parse = JObject.Parse(json);
        return (string?)parse[entry];
    }

    public static bool SetEntry(string entry, string data)
    {
        if (!File.Exists(ConfigPath)) return false;
        var json = File.ReadAllText(ConfigPath);
        var jsonFile = JObject.Parse(json);
        jsonFile[entry] = data;
        File.WriteAllText(ConfigPath, JsonConvert.SerializeObject(jsonFile, Formatting.Indented));
        return true;
    }
}