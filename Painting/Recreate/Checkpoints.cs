using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Autodraw.Configuration;
using Newtonsoft.Json;
using SkiaSharp;

namespace Autodraw.Painting.Recreate;

/// <summary>
/// A resumable position inside one recreate run.  A checkpoint is only ever applied to a run
/// whose <see cref="Fingerprint"/> matches, so a stale checkpoint can never draw another
/// image's strokes.
/// </summary>
public sealed class RecreateCheckpoint
{
    public const int CurrentVersion = 1;

    [JsonProperty("version")] public int Version { get; set; } = CurrentVersion;
    [JsonProperty("fingerprint")] public string Fingerprint { get; set; } = string.Empty;
    [JsonProperty("layer_index")] public int LayerIndex { get; set; }
    [JsonProperty("stroke_index")] public int StrokeIndex { get; set; }
    [JsonProperty("total_strokes")] public int TotalStrokes { get; set; }
    [JsonProperty("updated_utc")] public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    public bool IsUsable(int totalStrokes)
        => Version == CurrentVersion && StrokeIndex > 0 && StrokeIndex < totalStrokes;
}

/// <summary>Persistence seam for checkpoints; the engine only talks to this interface.</summary>
public interface ICheckpointStore
{
    RecreateCheckpoint? Load(string fingerprint);
    void Save(RecreateCheckpoint checkpoint);
    void Clear(string fingerprint);
}

/// <summary>Used by dry runs and by callers that explicitly do not want resume.</summary>
public sealed class NullCheckpointStore : ICheckpointStore
{
    public RecreateCheckpoint? Load(string fingerprint) => null;
    public void Save(RecreateCheckpoint checkpoint) { }
    public void Clear(string fingerprint) { }
}

/// <summary>
/// One JSON file per fingerprint under <c>%LOCALAPPDATA%\AutoDraw\checkpoints</c>.  Corrupt or
/// unreadable files are treated as "no checkpoint" so a bad write can never block a run.
/// </summary>
public sealed class FileCheckpointStore : ICheckpointStore
{
    private static readonly JsonSerializerSettings Settings = new()
    {
        Formatting = Formatting.Indented,
        NullValueHandling = NullValueHandling.Ignore
    };

    private readonly Action<string>? _log;

    public FileCheckpointStore(string? directory = null, Action<string>? log = null)
    {
        DirectoryPath = directory ?? Path.Combine(Config.FolderPath, "checkpoints");
        _log = log;
    }

    public string DirectoryPath { get; }

    private string PathFor(string fingerprint)
        => Path.Combine(DirectoryPath, Sanitize(fingerprint) + ".json");

    public RecreateCheckpoint? Load(string fingerprint)
    {
        if (string.IsNullOrWhiteSpace(fingerprint)) return null;
        var path = PathFor(fingerprint);
        try
        {
            if (!File.Exists(path)) return null;
            var checkpoint = JsonConvert.DeserializeObject<RecreateCheckpoint>(File.ReadAllText(path), Settings);
            if (checkpoint is null || checkpoint.Version != RecreateCheckpoint.CurrentVersion) return null;
            if (!string.Equals(checkpoint.Fingerprint, fingerprint, StringComparison.Ordinal))
            {
                _log?.Invoke($"Ignoring checkpoint {path}: fingerprint mismatch.");
                return null;
            }
            return checkpoint;
        }
        catch (Exception ex)
        {
            _log?.Invoke($"Ignoring unreadable checkpoint {path}: {ex.Message}");
            return null;
        }
    }

    public void Save(RecreateCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        if (string.IsNullOrWhiteSpace(checkpoint.Fingerprint)) return;
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            checkpoint.UpdatedUtc = DateTime.UtcNow;
            File.WriteAllText(PathFor(checkpoint.Fingerprint), JsonConvert.SerializeObject(checkpoint, Settings));
        }
        catch (Exception ex)
        {
            _log?.Invoke("Could not save the recreate checkpoint: " + ex.Message);
        }
    }

    public void Clear(string fingerprint)
    {
        if (string.IsNullOrWhiteSpace(fingerprint)) return;
        try
        {
            var path = PathFor(fingerprint);
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            _log?.Invoke("Could not delete the recreate checkpoint: " + ex.Message);
        }
    }

    private static string Sanitize(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
            builder.Append(char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '-');
        return builder.Length == 0 ? "run" : builder.ToString();
    }
}

/// <summary>
/// Identity of everything that determines a stroke plan.  If any input changes, the old
/// checkpoint no longer describes the new plan and must not be used.
/// </summary>
public static class RecreateFingerprint
{
    public static string Compute(
        SKBitmap image,
        ScreenRect canvas,
        IEnumerable<SKColor> palette,
        RecreateOptions options)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(palette);
        ArgumentNullException.ThrowIfNull(options);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Text(string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            hash.AppendData(bytes);
            hash.AppendData(new byte[] { 0x1F });
        }

        Text("autodraw-recreate-v1");
        Text(string.Create(CultureInfo.InvariantCulture, $"{image.Width}x{image.Height}"));
        Text(string.Create(CultureInfo.InvariantCulture,
            $"{canvas.Left},{canvas.Top},{canvas.Right},{canvas.Bottom}"));
        Text(string.Create(CultureInfo.InvariantCulture,
            $"{options.BrushSize}|{options.MoveStepPixels}|{options.AlphaThreshold}|{options.StrokeOrder}|{options.LayerOrder}"));

        foreach (var color in palette)
            Text(string.Create(CultureInfo.InvariantCulture, $"{color.Red},{color.Green},{color.Blue}"));

        // Include the pixels themselves so two different images with the same palette and the
        // same dimensions still get different checkpoints.
        var pixels = image.GetPixelSpan();
        if (!pixels.IsEmpty)
        {
            hash.AppendData(new byte[] { 0x1E });
            hash.AppendData(pixels);
        }

        var digest = hash.GetHashAndReset();
        return Convert.ToHexString(digest).ToLowerInvariant()[..24];
    }
}
