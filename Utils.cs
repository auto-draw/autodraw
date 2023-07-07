using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Autodraw
{
    public class Config
    {
        public static string FolderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AutoDraw");
        public static string ConfigPath = Path.Combine(FolderPath, "config.json");

        public static bool init()
        {
            if (File.Exists(ConfigPath)) return true;
            Directory.CreateDirectory(FolderPath);
            string emptyJObject = JsonConvert.SerializeObject(new JObject());
            File.WriteAllText(ConfigPath, emptyJObject);
            return true;
        }

        public static string getEntry(string entry)
        {
            if (!File.Exists(ConfigPath)) return "";
            string json = File.ReadAllText(ConfigPath);
            JObject parse = JObject.Parse(json);
            return (string)parse[entry];
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
            File.AppendAllText(Path.Combine(LogsPath, $"{DateTime.Now.ToString("dd.MM.yyyy")}.txt"), $"{text}\r\n");
        }
    }
}
