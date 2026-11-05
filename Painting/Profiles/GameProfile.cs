using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodraw.Configuration;
using Autodraw.Painting.Recreate;
using Newtonsoft.Json;

namespace Autodraw.Painting.Profiles;

/// <summary>
/// A named, game-specific snapshot of everything the picker needs: which picker the game uses,
/// its calibrated anchors, the wheel rotation direction and the current-colour indicator.
/// Stored as one JSON file per profile; the profile name is the game key.
/// </summary>
public sealed class GameProfile
{
    [JsonProperty("name")] public string Name { get; set; } = string.Empty;
    [JsonProperty("picker_type")] public string PickerType { get; set; } = "Wheel";
    [JsonProperty("points")] public Dictionary<string, ScreenPoint> Points { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    [JsonProperty("wheel_direction")] public string WheelDirection { get; set; } = "CCW";
    [JsonProperty("verify_point"), JsonConverter(typeof(NullableScreenPointConverter))]
    public ScreenPoint? VerifyPoint { get; set; }
    [JsonProperty("updated_utc")] public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Snapshot of the picker-relevant fields of a configuration.</summary>
    public static GameProfile Capture(string name, AutoDrawConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var pickerType = PickerRegistry.Require(config.PickerType).Id;
        var calibration = RecreateOptions.BuildCalibration(config, pickerType);

        return new GameProfile
        {
            Name = string.IsNullOrWhiteSpace(name) ? "Default" : name.Trim(),
            PickerType = pickerType,
            Points = calibration.Points.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase),
            WheelDirection = string.Equals(config.WheelDirection, "CW", StringComparison.OrdinalIgnoreCase) ? "CW" : "CCW",
            VerifyPoint = config.RecreateCurrentColorPoint,
            UpdatedUtc = DateTime.UtcNow
        };
    }

    /// <summary>Writes this profile back into a configuration snapshot.</summary>
    public bool TryApply(AutoDrawConfig config, out string error)
    {
        ArgumentNullException.ThrowIfNull(config);
        error = string.Empty;

        var definition = PickerRegistry.Find(PickerType);
        if (definition is null)
        {
            error = $"Profile '{Name}' uses picker '{PickerType}', which this build does not support.";
            return false;
        }

        var calibration = new PickerCalibration(definition.Id);
        foreach (var pair in Points) calibration.Set(pair.Key, pair.Value);

        var validation = definition.Validate(calibration, ScreenGeometry.TryGetVirtualScreenBounds());
        if (validation is not null)
        {
            error = $"Profile '{Name}' is not usable: {validation}";
            return false;
        }

        RecreateOptions.ApplyCalibration(config, calibration);
        config.WheelDirection = string.Equals(WheelDirection, "CW", StringComparison.OrdinalIgnoreCase) ? "CW" : "CCW";
        config.RecreateCurrentColorPoint = VerifyPoint;
        return true;
    }
}

/// <summary>
/// File-backed profile store.  One file per profile under <c>%LOCALAPPDATA%\AutoDraw\profiles</c>.
/// A missing directory, a corrupt file or an unreadable entry is skipped instead of throwing,
/// so a bad profile can never prevent the app from starting.
/// </summary>
public sealed class GameProfileStore
{
    public const string Extension = ".json";

    private static readonly JsonSerializerSettings Settings = new()
    {
        Formatting = Formatting.Indented,
        NullValueHandling = NullValueHandling.Ignore
    };

    private readonly Action<string>? _log;

    public GameProfileStore(string? directory = null, Action<string>? log = null)
    {
        DirectoryPath = directory ?? Path.Combine(Config.FolderPath, "profiles");
        _log = log;
    }

    public string DirectoryPath { get; }

    /// <summary>All readable profiles, sorted by name.</summary>
    public IReadOnlyList<GameProfile> LoadAll()
    {
        if (!Directory.Exists(DirectoryPath)) return Array.Empty<GameProfile>();

        var result = new List<GameProfile>();
        foreach (var path in Directory.EnumerateFiles(DirectoryPath, "*" + Extension))
        {
            var profile = TryRead(path);
            if (profile is not null) result.Add(profile);
        }

        return result.OrderBy(profile => profile.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public GameProfile? Load(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var path = PathFor(name);
        if (!File.Exists(path)) return null;
        return TryRead(path);
    }

    public bool Save(GameProfile profile, out string error)
    {
        ArgumentNullException.ThrowIfNull(profile);
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            error = "A profile needs a name.";
            return false;
        }

        try
        {
            Directory.CreateDirectory(DirectoryPath);
            profile.UpdatedUtc = DateTime.UtcNow;
            File.WriteAllText(PathFor(profile.Name), JsonConvert.SerializeObject(profile, Settings), Encoding.UTF8);
            return true;
        }
        catch (Exception ex)
        {
            error = "Could not save the profile: " + ex.Message;
            _log?.Invoke(error);
            return false;
        }
    }

    public bool Delete(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        try
        {
            var path = PathFor(name);
            if (!File.Exists(path)) return false;
            File.Delete(path);
            return true;
        }
        catch (Exception ex)
        {
            _log?.Invoke("Could not delete the profile: " + ex.Message);
            return false;
        }
    }

    private GameProfile? TryRead(string path)
    {
        try
        {
            var profile = JsonConvert.DeserializeObject<GameProfile>(File.ReadAllText(path), Settings);
            if (profile is null || string.IsNullOrWhiteSpace(profile.Name)) return null;
            if (profile.Points is null) profile.Points = new Dictionary<string, ScreenPoint>(StringComparer.OrdinalIgnoreCase);
            return profile;
        }
        catch (Exception ex)
        {
            _log?.Invoke($"Ignoring unreadable profile {Path.GetFileName(path)}: {ex.Message}");
            return null;
        }
    }

    private string PathFor(string name) => Path.Combine(DirectoryPath, Slug(name) + Extension);

    /// <summary>Filesystem-safe key derived from the user's profile name.</summary>
    public static string Slug(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "default";

        var builder = new StringBuilder(name.Length);
        var lastWasDash = false;
        foreach (var character in name.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                lastWasDash = false;
            }
            else if (!lastWasDash)
            {
                builder.Append('-');
                lastWasDash = true;
            }
        }

        var slug = builder.ToString().Trim('-');
        return slug.Length == 0 ? "default" : slug;
    }
}
