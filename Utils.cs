using Bitmap = Avalonia.Media.Imaging.Bitmap;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using SharpHook;
using SharpHook.Native;

using SkiaSharp;

using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Drawing;
using System.Runtime.Versioning;
using Avalonia.Controls;
using Avalonia.Platform;
using System.Diagnostics;

namespace Autodraw
{
    public static class ImageExtensions
    {
        public static Bitmap ConvertToAvaloniaBitmap(this SKBitmap bitmap)
        {
            return new Bitmap
                (Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Premul,
                bitmap.GetPixels(),
                new Avalonia.PixelSize(bitmap.Width, bitmap.Height),
                new Avalonia.Vector(96, 96),
                bitmap.GetPixelSpan().Length
            );
        }
    }

    public class Config
    {
        public static string FolderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutoDraw");
        public static string ConfigPath = Path.Combine(FolderPath, "config.json");
        public static string ThemesPath = Path.Combine(FolderPath, "Themes");

        public static void init()
        {
            Directory.CreateDirectory(FolderPath);
            if (!File.Exists(ConfigPath))
            {
                JObject obj = new();
                // Migrates old directory list path (from autodrawer v1) to the new config file
                string OldPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AutoDraw");
                if (File.Exists(Path.Combine(OldPath, "dir.txt")) && File.ReadAllText(Path.Combine(OldPath, "dir.txt")).Length != 0)
                {
                    obj.Add("ConfigFolder", File.ReadAllText(Path.Combine(OldPath, "dir.txt")));
                }
                string emptyJObject = JsonConvert.SerializeObject(obj);
                File.WriteAllText(ConfigPath, emptyJObject);
            }
            Utils.Copy(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Styles"), ThemesPath);
            if(getEntry("SavedPath") is null || !Directory.Exists(getEntry("SavedPath")))
            {
                Directory.CreateDirectory(ThemesPath);
                setEntry("SavedPath", ThemesPath);
            }
            else
            {
                ThemesPath = getEntry("SavedPath");
            }
        }

        public static string? getEntry(string entry)
        {
            if (!File.Exists(ConfigPath)) return null;
            string json = File.ReadAllText(ConfigPath);
            JObject parse = JObject.Parse(json);
            return (string?)parse[entry];
        }

        public static bool setEntry(string entry, string data)
        {
            if (!File.Exists(ConfigPath)) return false;
            string json = File.ReadAllText(ConfigPath);
            JObject jsonFile = JObject.Parse(json);
            jsonFile[entry] = data;
            File.WriteAllText(ConfigPath, JsonConvert.SerializeObject(jsonFile, Formatting.Indented));
            return true;
        }
    }

    public class Utils
    {
        public static string LogsPath = Path.Combine(Config.FolderPath, "logs");
        public static bool LoggingEnabled = Config.getEntry("logsEnabled") != "True";

        public static void Log(string text)
        {
            Debug.WriteLine(text);
            if (LoggingEnabled) return;
            Directory.CreateDirectory(LogsPath);
            File.AppendAllText(Path.Combine(LogsPath, $"{DateTime.Now:dd.MM.yyyy}.txt"), $"{text}\r\n");
        }

        public static void Copy(string sourceDirectory, string targetDirectory)
        {
            DirectoryInfo diSource = new DirectoryInfo(sourceDirectory);
            DirectoryInfo diTarget = new DirectoryInfo(targetDirectory);

            CopyAll(diSource, diTarget);
        }

        public static void CopyAll(DirectoryInfo source, DirectoryInfo target)
        {
            Directory.CreateDirectory(target.FullName);

            // Copy each file into the new directory.
            foreach (FileInfo fi in source.GetFiles())
            {
                Console.WriteLine(@"Copying {0}\{1}", target.FullName, fi.Name);
                fi.CopyTo(Path.Combine(target.FullName, fi.Name), true);
            }

            // Copy each subdirectory using recursion.
            foreach (DirectoryInfo diSourceSubDir in source.GetDirectories())
            {
                DirectoryInfo nextTargetSubDir =
                    target.CreateSubdirectory(diSourceSubDir.Name);
                CopyAll(diSourceSubDir, nextTargetSubDir);
            }
        }
    }
}
