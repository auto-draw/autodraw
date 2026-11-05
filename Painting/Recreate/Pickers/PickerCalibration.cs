using System;
using System.Collections.Generic;
using System.Linq;
using Autodraw.Configuration;

namespace Autodraw.Painting.Recreate;

/// <summary>Named calibration point keys shared by profiles, config and the picker definitions.</summary>
public static class PickerPoints
{
    public const string WheelCenter = "wheel.center";
    public const string WheelEdge = "wheel.edge";
    public const string WheelBright = "wheel.bright";
    public const string WheelDark = "wheel.dark";

    public const string BoxHueStart = "box.hue_start";
    public const string BoxHueEnd = "box.hue_end";
    public const string BoxTopLeft = "box.top_left";
    public const string BoxBottomRight = "box.bottom_right";
}

/// <summary>
/// The calibrated anchors for one picker.  This is the contract between calibration capture,
/// profile persistence and the click-point math: an ordered bag of named screen points.  A
/// deferred picker (hex field, R/G/B fields, H/S/V fields) plugs in by declaring its own keys.
/// </summary>
public sealed class PickerCalibration
{
    private readonly Dictionary<string, ScreenPoint> _points = new(StringComparer.OrdinalIgnoreCase);

    public PickerCalibration(string pickerType) => PickerType = pickerType ?? string.Empty;

    public string PickerType { get; }

    public IReadOnlyDictionary<string, ScreenPoint> Points => _points;

    public PickerCalibration Set(string key, ScreenPoint point)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        _points[key] = point;
        return this;
    }

    public ScreenPoint? Point(string key)
        => _points.TryGetValue(key, out var point) ? point : null;

    public bool TryGet(string key, out ScreenPoint point)
    {
        if (_points.TryGetValue(key, out point)) return true;
        point = default;
        return false;
    }

    public bool HasAll(IEnumerable<string> keys)
    {
        foreach (var key in keys)
            if (!_points.ContainsKey(key))
                return false;
        return true;
    }

    public PickerCalibration Clone()
    {
        var clone = new PickerCalibration(PickerType);
        foreach (var pair in _points) clone.Set(pair.Key, pair.Value);
        return clone;
    }

    /// <summary>Builds a calibration from an ordered capture list; throws when counts disagree.</summary>
    public static PickerCalibration FromCaptured(
        string pickerType,
        IReadOnlyList<string> orderedKeys,
        IReadOnlyList<ScreenPoint> captured)
    {
        ArgumentNullException.ThrowIfNull(orderedKeys);
        ArgumentNullException.ThrowIfNull(captured);
        if (orderedKeys.Count != captured.Count)
            throw new ArgumentException(
                $"Picker '{pickerType}' needs {orderedKeys.Count} calibration point(s) but {captured.Count} were captured.",
                nameof(captured));

        var calibration = new PickerCalibration(pickerType);
        for (var index = 0; index < orderedKeys.Count; index++)
            calibration.Set(orderedKeys[index], captured[index]);
        return calibration;
    }
}

/// <summary>One ordered calibration step: the storage key and the localization prompt key.</summary>
public sealed record PickerPointRequirement(string Key, string PromptKey);

/// <summary>
/// The abstraction seam for colour pickers.  Adding a deferred picker (HEX field, R/G/B inputs,
/// H/S/V inputs, eyedropper, ...) means implementing this interface and calling
/// <see cref="PickerRegistry.Register"/>; the executor and the calibration UI never change.
/// </summary>
public interface IPickerDefinition
{
    /// <summary>Stable identifier persisted in <c>dd.json</c> (for example "Wheel").</summary>
    string Id { get; }

    /// <summary>Localization key for the picker's display name.</summary>
    string DisplayNameKey { get; }

    /// <summary>Ordered points the user must click during calibration.</summary>
    IReadOnlyList<PickerPointRequirement> Points { get; }

    /// <summary>Returns null when the calibration is usable, otherwise a user-facing error.</summary>
    string? Validate(PickerCalibration calibration, ScreenRect? screen);

    /// <summary>Creates the picker that types/clicks the colour into the target application.</summary>
    IColorPicker Create(IInputSimulator input, PickerCalibration calibration, RecreateOptions options, Action<string>? log);
}

/// <summary>
/// Registration point for picker definitions.  Exactly the two in-scope pickers are registered;
/// nothing here knows about the executor, the UI, or persistence.
/// </summary>
public static class PickerRegistry
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, IPickerDefinition> ById = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<IPickerDefinition> Ordered = new();

    static PickerRegistry()
    {
        Register(new WheelPickerDefinition());
        Register(new BoxPickerDefinition());
    }

    public static IReadOnlyList<IPickerDefinition> All
    {
        get { lock (Gate) return Ordered.ToArray(); }
    }

    public static void Register(IPickerDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (string.IsNullOrWhiteSpace(definition.Id))
            throw new ArgumentException("A picker definition needs a non-empty Id.", nameof(definition));

        lock (Gate)
        {
            if (ById.ContainsKey(definition.Id))
                throw new InvalidOperationException($"A picker definition with id '{definition.Id}' is already registered.");
            ById[definition.Id] = definition;
            Ordered.Add(definition);
        }
    }

    public static IPickerDefinition? Find(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        lock (Gate) return ById.TryGetValue(id.Trim(), out var definition) ? definition : null;
    }

    /// <summary>Resolves a picker or throws a user-facing <see cref="ColorSelectionException"/>.</summary>
    public static IPickerDefinition Require(string? id)
    {
        var definition = Find(id);
        if (definition is not null) return definition;

        var supported = string.Join(", ", All.Select(item => item.Id));
        throw new ColorSelectionException(
            $"Colour picker '{id}' is not available in this build. Supported pickers: {supported}.");
    }

    /// <summary>True when at least one registered definition declares this picker id.</summary>
    public static bool Supports(string? id) => Find(id) is not null;
}
