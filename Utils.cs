﻿using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SharpHook.Native;
using SkiaSharp;

namespace Autodraw;

public static class ImageExtensions
{
    public static Bitmap ConvertToAvaloniaBitmap(this SKBitmap bitmap)
    {
        return new Bitmap
        (PixelFormat.Bgra8888, AlphaFormat.Premul,
            bitmap.GetPixels(),
            new PixelSize(bitmap.Width, bitmap.Height),
            new Vector(96, 96),
            bitmap.GetPixelSpan().Length
        );
    }
}

public class Config
{
    public static string FolderPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutoDraw");

    public static KeyCode Keybind_StartDrawing = KeyCode.VcLeftShift;
    public static KeyCode Keybind_StopDrawing = KeyCode.VcLeftAlt;
    public static KeyCode Keybind_PauseDrawing = KeyCode.VcBackslash;
    public static KeyCode Keybind_SkipRescan = KeyCode.VcBackspace;
    public static KeyCode Keybind_LockPreview = KeyCode.VcLeftControl;

    public static string ConfigPath = Path.Combine(FolderPath, "config.json");
    public static string ThemesPath = Path.Combine(FolderPath, "Themes");
    public static string CachePath = Path.Combine(FolderPath, "Cache");

    public static void init()
    {
        Directory.CreateDirectory(FolderPath);
        if (!File.Exists(ConfigPath))
        {
            JObject obj = new();
            // Migrates old directory list path (from autodrawer v1) to the new config file
            var OldPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AutoDraw");
            if (File.Exists(Path.Combine(OldPath, "dir.txt")) &&
                File.ReadAllText(Path.Combine(OldPath, "dir.txt")).Length != 0)
                obj.Add("ConfigFolder", File.ReadAllText(Path.Combine(OldPath, "dir.txt")));
            var emptyJObject = JsonConvert.SerializeObject(obj);
            File.WriteAllText(ConfigPath, emptyJObject);
            
        }

        Utils.Copy(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Styles"), ThemesPath);
        
        // Start of Compatability Code, please remove 1 month after release of v2.1, aka during 1st of February.
        if (getEntry("SavedPath") is not null)
        {
            setEntry("SavedThemesPath", getEntry("SavedPath"));
            setEntry("SavedPath", "");
        }
        // End of Compatability Code
        
        // Check Configuration Path for Themes.
        if (getEntry("SavedThemesPath") is null || !Directory.Exists(getEntry("SavedPath")))
        {
            Directory.CreateDirectory(ThemesPath);
            setEntry("SavedThemesPath", ThemesPath);
        }
        else
        {
            ThemesPath = getEntry("SavedThemesPath");
        }
        
        // Check Configuration Path for Cache.
        if (getEntry("SavedCachePath") is null || !Directory.Exists(getEntry("SavedPath")))
        {
            Directory.CreateDirectory(CachePath);
            setEntry("SavedCachePath", CachePath);
        }
        else
        {
            CachePath = getEntry("SavedCachePath");
        }
        
        // Get the Keybinds :P
        if (getEntry("Keybind_StartDrawing") is not null)
        {
            Keybind_StartDrawing = (KeyCode)Enum.Parse(typeof(KeyCode), getEntry("Keybind_StartDrawing"));
        }
        if (getEntry("Keybind_StopDrawing") is not null)
        {
            Keybind_StopDrawing = (KeyCode)Enum.Parse(typeof(KeyCode), getEntry("Keybind_StopDrawing"));
        }
        if (getEntry("Keybind_PauseDrawing") is not null)
        {
            Keybind_PauseDrawing = (KeyCode)Enum.Parse(typeof(KeyCode), getEntry("Keybind_PauseDrawing"));
        }
        if (getEntry("Keybind_SkipRescan") is not null)
        {
            Keybind_SkipRescan = (KeyCode)Enum.Parse(typeof(KeyCode), getEntry("Keybind_SkipRescan"));
        }
        if (getEntry("Keybind_LockPreview") is not null)
        {
            Keybind_LockPreview = (KeyCode)Enum.Parse(typeof(KeyCode), getEntry("Keybind_LockPreview"));
        }
        
    }

    public static string? getEntry(string entry)
    {
        if (!File.Exists(ConfigPath)) return null;
        var json = File.ReadAllText(ConfigPath);
        var parse = JObject.Parse(json);
        return (string?)parse[entry];
    }

    public static bool setEntry(string entry, string data)
    {
        if (!File.Exists(ConfigPath)) return false;
        var json = File.ReadAllText(ConfigPath);
        var jsonFile = JObject.Parse(json);
        jsonFile[entry] = data;
        File.WriteAllText(ConfigPath, JsonConvert.SerializeObject(jsonFile, Formatting.Indented));
        return true;
    }
}

public class Utils
{
    public static string LogFolder = Path.Combine(Config.FolderPath, "logs");
    public static string LogsPath = Path.Combine(LogFolder, $"{DateTime.Now:dd.MM.yyyy}.txt");
    public static bool LoggingEnabled = Config.getEntry("logsEnabled") == "True";
    public static StreamWriter? LogObject;

    public static void Log(string text)
    {
        Debug.WriteLine(text);
        if (!LoggingEnabled) return;
        if (!Directory.Exists(LogFolder)) Directory.CreateDirectory(LogFolder);
        if (LogObject == null) LogObject = new StreamWriter(LogsPath);
        LogObject.WriteLine(text);
        LogObject.Flush();
    }

    public static void Copy(string sourceDirectory, string targetDirectory)
    {
        var diSource = new DirectoryInfo(sourceDirectory);
        var diTarget = new DirectoryInfo(targetDirectory);
        CopyAll(diSource, diTarget);
    }

    public static void CopyAll(DirectoryInfo source, DirectoryInfo target)
    {
        Directory.CreateDirectory(target.FullName);

        // Copy each file into the new directory.
        foreach (var fi in source.GetFiles())
        {
            Console.WriteLine(@"Copying {0}\{1}", target.FullName, fi.Name);
            fi.CopyTo(Path.Combine(target.FullName, fi.Name), true);
        }

        // Copy each subdirectory using recursion.
        foreach (var diSourceSubDir in source.GetDirectories())
        {
            var nextTargetSubDir =
                target.CreateSubdirectory(diSourceSubDir.Name);
            CopyAll(diSourceSubDir, nextTargetSubDir);
        }
    }
}


public class Marketplace
{
    public static string API = "https://auto-draw.com/api/";
    private static readonly HttpClient client = new HttpClient();
    
    public async static Task<JArray> List(string type) // 'type' can be either "theme" or "config"
    {
        HttpResponseMessage response = await client.GetAsync($"{API}list?page=1&filter={type}");
        if (!response.IsSuccessStatusCode) return null;
        var JSONResponse = await response.Content.ReadAsStringAsync();
        dynamic Response = (JObject)JsonConvert.DeserializeObject(JSONResponse);
        return Response.items;
    }
    
    public async static Task<String> Download(int id) // filename should be in the format of "{theme name}.{file extension}"
    {
        HttpResponseMessage response = await client.GetAsync($"{API}download?id={id}");
        if (!response.IsSuccessStatusCode) return null;
        var FileResponse = await response.Content.ReadAsStringAsync();
        File.WriteAllText(Path.Combine(Config.ThemesPath, response.Content.Headers.ContentDisposition?.FileName.Trim('"')), FileResponse);
        return Path.Combine(Config.ThemesPath, response.Content.Headers.ContentDisposition?.FileName.Trim('"'));
    }
}