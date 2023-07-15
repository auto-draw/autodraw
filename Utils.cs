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

        public static bool init()
        {
            if (File.Exists(ConfigPath)) return true;
            Directory.CreateDirectory(FolderPath);
            JObject obj = new();
            // Migrates old directory list path (from autodrawer v1) to the new config file
            string OldPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AutoDraw");
            if (File.Exists(Path.Combine(OldPath, "dir.txt")) && File.ReadAllText(Path.Combine(OldPath, "dir.txt")).Length != 0)
            {
                obj.Add("ConfigFolder", File.ReadAllText(Path.Combine(OldPath, "dir.txt")));
            }
            string emptyJObject = JsonConvert.SerializeObject(obj);
            File.WriteAllText(ConfigPath, emptyJObject);
            return true;
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
            File.WriteAllText(ConfigPath, JsonConvert.SerializeObject(jsonFile));
            return true;
        }
    }

    public class Utils
    {
        public static string LogsPath = Path.Combine(Config.FolderPath, "logs");

        public static void Log(string text)
        {
            Console.WriteLine(text);
            if (Config.getEntry("logsEnabled") != "true") return;
            Directory.CreateDirectory(LogsPath);
            File.AppendAllText(Path.Combine(LogsPath, $"{DateTime.Now:dd.MM.yyyy}.txt"), $"{text}\r\n");
        }
    }
}
