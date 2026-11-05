using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkiaSharp;

namespace Autodraw;

/// <summary>Runs the Real-ESRGAN ncnn Vulkan binary that ships with HexedDraw to upscale an
/// image 2x before quantization.  The binary and model files are optional; callers receive a
/// clear exception when they are not present so the UI can show a useful message.</summary>
public static class ImageEnhancer
{
    private const string ExecutableName = "realesrgan-ncnn-vulkan.exe";

    public static string? FindExecutable()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "HexedDraw"),
            Path.Combine(AppContext.BaseDirectory, "realesrgan"),
            AppContext.BaseDirectory,
            Path.Combine(Environment.CurrentDirectory, "HexedDraw"),
            Environment.CurrentDirectory,
            Path.Combine(Config.FolderPath, "HexedDraw")
        };
        foreach (var directory in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var path = Path.Combine(directory, ExecutableName);
            if (File.Exists(path) && Directory.Exists(Path.Combine(directory, "models"))) return path;
        }
        return null;
    }

    public static async Task<SKBitmap> EnhanceAsync(SKBitmap source, CancellationToken cancellationToken = default)
    {
        var executable = FindExecutable() ?? throw new FileNotFoundException(
            $"Real-ESRGAN was not found. Place {ExecutableName} and its models folder next to AutoDraw or in a HexedDraw folder.");
        var workingDirectory = Path.GetDirectoryName(executable)!;
        var tempDirectory = Path.Combine(Path.GetTempPath(), "AutoDrawEnhance", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        var inputPath = Path.Combine(tempDirectory, "input.png");
        var outputPath = Path.Combine(tempDirectory, "output.png");

        try
        {
            using (var data = source.Encode(SKEncodedImageFormat.Png, 100))
            await using (var stream = File.Create(inputPath))
                data.SaveTo(stream);

            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add("-i");
            startInfo.ArgumentList.Add(inputPath);
            startInfo.ArgumentList.Add("-o");
            startInfo.ArgumentList.Add(outputPath);
            startInfo.ArgumentList.Add("-s");
            startInfo.ArgumentList.Add("2");
            startInfo.ArgumentList.Add("-n");
            startInfo.ArgumentList.Add("realesr-animevideov3");
            startInfo.ArgumentList.Add("-m");
            startInfo.ArgumentList.Add(Path.Combine(workingDirectory, "models"));

            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start Real-ESRGAN.");
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(5));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(true); } catch { }
                throw new TimeoutException("Image enhancement timed out.");
            }

            await outputTask;
            var error = await errorTask;
            if (process.ExitCode != 0 || !File.Exists(outputPath))
                throw new InvalidOperationException($"Real-ESRGAN failed (exit {process.ExitCode}). {error}".Trim());

            var result = SKBitmap.Decode(outputPath);
            if (result is null) throw new InvalidOperationException("Real-ESRGAN produced an unreadable image.");
            return result;
        }
        finally
        {
            try { Directory.Delete(tempDirectory, true); } catch { }
        }
    }
}
