using System;
using System.IO;

namespace Autodraw.Models.Utils;

public abstract class Logs
{
    public static string LogFolder = Path.Combine(Config.FolderPath, "logs");
    public static string LogsPath = Path.Combine(LogFolder, $"{DateTime.Now:dd.MM.yyyy}.txt");
    public static bool LoggingEnabled = Config.GetEntry("logsEnabled") == "True";
    public static StreamWriter? LogObject;

    public static void Log(object data)
    {
        string text = data.ToString() ?? "null";
        Console.WriteLine(text);
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