using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Autodraw.Configuration;

/// <summary>Portable dd.json configuration.  Property names intentionally match the public file format.</summary>
public sealed class AutoDrawConfig
{
    [JsonProperty("language")] public string Language { get; set; } = "English";
    [JsonProperty("monitor_index")] public int MonitorIndex { get; set; }
    [JsonProperty("mode")] public string Mode { get; set; } = "color";
    [JsonProperty("draw_method")] public string DrawMethod { get; set; } = "1:1 Exact (AutoDraw)";
    [JsonProperty("algorithm")] public string Algorithm { get; set; } = "DFS (Default)";
    [JsonProperty("layer_order")] public string LayerOrder { get; set; } = "Brightness to darkness";
    /// <summary>Selects the legacy single-colour luminance trace or the colour-layer recreate engine.
    /// Defaults to the legacy path so old dd.json files keep their exact behaviour.</summary>
    [JsonProperty("mask_mode"), JsonConverter(typeof(LayerMaskModeConverter))]
    public LayerMaskMode MaskMode { get; set; } = LayerMaskMode.LuminanceThreshold;
    [JsonProperty("pixel_size")] public int PixelSize { get; set; } = 1;
    [JsonProperty("k_clusters")] public int KClusters { get; set; } = 50;
    [JsonProperty("interval")] public long Interval { get; set; } = 45000;
    [JsonProperty("click_delay")] public double ClickDelay { get; set; } = .03;
    /// <summary>HexedDrawer-compatible draw delay in seconds. When present it is the canonical
    /// import value for <see cref="Interval"/>; the native tick field remains supported.</summary>
    [JsonProperty("draw_delay")] public double? DrawDelaySeconds { get; set; }
    [JsonProperty("brush_size")] public int BrushSize { get; set; } = 1;
    [JsonProperty("auto_quantize")] public bool AutoQuantize { get; set; }
    [JsonProperty("enhance_image")] public bool EnhanceImage { get; set; }
    [JsonProperty("quantize_style")] public string QuantizeStyle { get; set; } = "Pixelated (Classic)";
    [JsonProperty("dfs_mode")] public string DfsMode { get; set; } = "8-Directional";
    [JsonProperty("stray_removal_mode")] public string StrayRemovalMode { get; set; } = "Disabled";
    [JsonProperty("drawer_module")] public string DrawerModule { get; set; } = "New (A* Bridge)";
    [JsonProperty("color_picking_method")] public string ColorPickingMethod { get; set; } = "Circle";
    [JsonProperty("quantize_method")] public string QuantizeMethod { get; set; } = "NeuQuant Neural-Net";
    [JsonProperty("kmeans_algo")] public string KmeansAlgorithm { get; set; } = "KMeans++ (Original)";
    /// <summary>Color space used for palette distance: CIELAB, RGB, or OKLAB. Null falls back to CIELAB.</summary>
    [JsonProperty("color_distance_method")] public string? ColorDistanceMethod { get; set; }
    [JsonProperty("lab_lum_weight")] public double LabLumWeight { get; set; } = 1;
    [JsonProperty("lab_chroma_weight")] public double LabChromaWeight { get; set; } = 1;
    [JsonProperty("lab_hue_weight")] public double LabHueWeight { get; set; } = 1;
    [JsonProperty("lab_contrast_thresh")] public double LabContrastThreshold { get; set; } = 10;
    [JsonProperty("lab_spatial_sigma")] public double LabSpatialSigma { get; set; } = 10;
    [JsonProperty("lab_spatial_iter")] public int LabSpatialIterations { get; set; } = 1;
    [JsonProperty("lab_edge_preservation")] public double LabEdgePreservation { get; set; } = 50;
    [JsonProperty("lab_range_sigma")] public double LabRangeSigma { get; set; } = 20;
    [JsonProperty("use_bilateral")] public bool UseBilateral { get; set; } = true;
    [JsonProperty("bi_d")] public int BilateralDiameter { get; set; } = 9;
    [JsonProperty("bi_sc")] public double BilateralColorSigma { get; set; } = 75;
    [JsonProperty("bi_ss")] public double BilateralSpaceSigma { get; set; } = 75;
    [JsonProperty("use_seeds")] public bool UseSeeds { get; set; }
    [JsonProperty("seeds_count")] public int SeedsCount { get; set; } = 8000;
    [JsonProperty("remove_stray")] public bool RemoveStray { get; set; } = true;
    [JsonProperty("picker_type")] public string PickerType { get; set; } = "Wheel";
    /// <summary>Name of the active game profile under <c>%LOCALAPPDATA%\AutoDraw\profiles</c>.</summary>
    [JsonProperty("picker_profile")] public string PickerProfile { get; set; } = string.Empty;
    [JsonProperty("wheel_center"), JsonConverter(typeof(NullableScreenPointConverter))] public ScreenPoint? WheelCenter { get; set; }
    [JsonProperty("wheel_edge"), JsonConverter(typeof(NullableScreenPointConverter))] public ScreenPoint? WheelEdge { get; set; }
    [JsonProperty("wheel_dark"), JsonConverter(typeof(NullableScreenPointConverter))] public ScreenPoint? WheelDark { get; set; }
    [JsonProperty("wheel_bright"), JsonConverter(typeof(NullableScreenPointConverter))] public ScreenPoint? WheelBright { get; set; }
    [JsonProperty("wheel_dir")] public string WheelDirection { get; set; } = "CCW";
    /// <summary>Imported LxPainter metadata only; the picker derives its rotation origin from
    /// <see cref="WheelEdge"/>, so this value is never used to compute a click.</summary>
    [JsonProperty("wheel_angle")] public double WheelAngle { get; set; } = 180;
    [JsonProperty("box_hue_start"), JsonConverter(typeof(NullableScreenPointConverter))] public ScreenPoint? BoxHueStart { get; set; }
    [JsonProperty("box_hue_end"), JsonConverter(typeof(NullableScreenPointConverter))] public ScreenPoint? BoxHueEnd { get; set; }
    [JsonProperty("box_tl"), JsonConverter(typeof(NullableScreenPointConverter))] public ScreenPoint? BoxTopLeft { get; set; }
    [JsonProperty("box_br"), JsonConverter(typeof(NullableScreenPointConverter))] public ScreenPoint? BoxBottomRight { get; set; }
    [JsonProperty("enable_macros")] public bool EnableMacros { get; set; }
    [JsonProperty("delay_open")] public double DelayOpen { get; set; } = .01;
    [JsonProperty("delay_input")] public double DelayInput { get; set; } = .01;
    [JsonProperty("delay_close")] public double DelayClose { get; set; } = .01;
    [JsonProperty("open_picker_coord"), JsonConverter(typeof(NullableScreenPointConverter))] public ScreenPoint? OpenPickerCoordinate { get; set; }
    [JsonProperty("close_picker_coord"), JsonConverter(typeof(NullableScreenPointConverter))] public ScreenPoint? ClosePickerCoordinate { get; set; }
    [JsonProperty("bg_colors_to_remove")] public List<string> BackgroundColorsToRemove { get; set; } = new();
    [JsonProperty("search_limit")] public int SearchLimit { get; set; } = 20;
    [JsonProperty("safe_search")] public string SafeSearch { get; set; } = "off";
    [JsonProperty("location_mode")] public int LocationMode { get; set; }
    [JsonProperty("lock_shape")] public bool LockShape { get; set; }
    [JsonProperty("custom_w")] public int CustomWidth { get; set; }
    [JsonProperty("custom_h")] public int CustomHeight { get; set; }
    [JsonProperty("draw_region")] public ScreenRect DrawRegion { get; set; } = new(607, 92, 706, 709);
    [JsonProperty("area_selected")] public bool AreaSelected { get; set; }
    [JsonProperty("stencil_enabled")] public bool StencilEnabled { get; set; }
    [JsonProperty("stencil_opacity")] public int StencilOpacity { get; set; } = 35;

    // ------------------------------------------------------------------
    // Colour-layer recreate engine (used when MaskMode == ColorLayers).
    // The legacy luminance path ignores these fields.
    // ------------------------------------------------------------------
    /// <summary>coverage-desc | coverage-asc | luminance-desc | luminance-asc.</summary>
    [JsonProperty("recreate_layer_order")] public string RecreateLayerOrder { get; set; } = "coverage-desc";
    [JsonProperty("recreate_stroke_delay_ms")] public int RecreateStrokeDelayMs { get; set; } = 10;
    [JsonProperty("recreate_move_step_px")] public int RecreateMoveStepPixels { get; set; } = 8;
    /// <summary>serpentine | nearest-neighbour. Serpentine is cheaper and row-coherent; nearest
    /// reduces travel between disconnected runs.</summary>
    [JsonProperty("recreate_stroke_order")] public string RecreateStrokeOrder { get; set; } = "serpentine";
    [JsonProperty("recreate_verify")] public bool RecreateVerify { get; set; } = true;
    /// <summary>Allowed CIE ΔE2000 difference between the requested and sampled colour.</summary>
    [JsonProperty("recreate_verify_tolerance")] public double RecreateVerifyTolerance { get; set; } = 8;
    [JsonProperty("recreate_verify_retries")] public int RecreateVerifyRetries { get; set; } = 2;
    /// <summary>Selection attempts when verification is disabled (a dropped click still retries).</summary>
    [JsonProperty("recreate_selection_attempts")] public int RecreateSelectionAttempts { get; set; } = 2;
    /// <summary>Attempts per stroke when the input backend throws.</summary>
    [JsonProperty("recreate_stroke_attempts")] public int RecreateStrokeAttempts { get; set; } = 2;
    [JsonProperty("recreate_dry_run")] public bool RecreateDryRun { get; set; }
    [JsonProperty("recreate_alpha_threshold")] public int RecreateAlphaThreshold { get; set; } = 200;
    /// <summary>Abort when a window other than the targeted one becomes foreground.</summary>
    [JsonProperty("recreate_focus_guard")] public bool RecreateFocusGuard { get; set; } = true;
    /// <summary>Resume from a matching checkpoint instead of starting over.</summary>
    [JsonProperty("recreate_resume")] public bool RecreateResume { get; set; } = true;
    /// <summary>Strokes between checkpoint writes.</summary>
    [JsonProperty("recreate_checkpoint_every")] public int RecreateCheckpointEvery { get; set; } = 25;
    /// <summary>Pixel that shows the target application's currently selected colour.</summary>
    [JsonProperty("recreate_current_color"), JsonConverter(typeof(NullableScreenPointConverter))]
    public ScreenPoint? RecreateCurrentColorPoint { get; set; }
}

/// <summary>Which mask source the drawing pipeline uses.</summary>
public enum LayerMaskMode
{
    /// <summary>Legacy: one luminance-threshold mask, drawn by <see cref="Drawing.Draw"/>.</summary>
    LuminanceThreshold = 0,
    /// <summary>New: one opaque bitmap mask per quantized palette colour.</summary>
    ColorLayers = 1
}

public sealed class LayerMaskModeConverter : JsonConverter<LayerMaskMode>
{
    public override void WriteJson(JsonWriter writer, LayerMaskMode value, JsonSerializer serializer)
        => writer.WriteValue(value == LayerMaskMode.ColorLayers ? "color-layers" : "luminance-threshold");

    public override LayerMaskMode ReadJson(JsonReader reader, Type type, LayerMaskMode existing, bool hasExisting, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Boolean)
            return reader.Value is true ? LayerMaskMode.ColorLayers : LayerMaskMode.LuminanceThreshold;
        if (reader.TokenType == JsonToken.Integer)
            return Convert.ToInt32(reader.Value) == 0 ? LayerMaskMode.LuminanceThreshold : LayerMaskMode.ColorLayers;
        if (reader.TokenType != JsonToken.String)
            throw new JsonSerializationException("mask_mode must be a string, boolean, or integer.");

        var value = ((string?)reader.Value ?? string.Empty).Trim().ToLowerInvariant();
        return value switch
        {
            "color" or "colour" or "color-layers" or "colour-layers" or "layers" or "layer"
                or "colorlayer" or "colorlayers" or "true" or "on" => LayerMaskMode.ColorLayers,
            _ => LayerMaskMode.LuminanceThreshold
        };
    }
}

[JsonConverter(typeof(ScreenPointConverter))]
public readonly record struct ScreenPoint(int X, int Y);
[JsonConverter(typeof(ScreenRectConverter))]
public readonly record struct ScreenRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left + 1;
    public int Height => Bottom - Top + 1;
}

public sealed class ScreenPointConverter : JsonConverter<ScreenPoint>
{
    public override void WriteJson(JsonWriter writer, ScreenPoint value, JsonSerializer serializer) => new JArray(value.X, value.Y).WriteTo(writer);
    public override ScreenPoint ReadJson(JsonReader reader, Type type, ScreenPoint existing, bool hasExisting, JsonSerializer serializer)
    {
        var a = JArray.Load(reader);
        if (a.Count != 2 || a[0]?.Type != JTokenType.Integer || a[1]?.Type != JTokenType.Integer) throw new JsonSerializationException("Coordinates must be [x, y].");
        return new ScreenPoint(a[0]!.Value<int>(), a[1]!.Value<int>());
    }
}

public sealed class NullableScreenPointConverter : JsonConverter<ScreenPoint?>
{
    public override void WriteJson(JsonWriter writer, ScreenPoint? value, JsonSerializer serializer)
    {
        if (value is null) writer.WriteNull();
        else new JArray(value.Value.X, value.Value.Y).WriteTo(writer);
    }

    public override ScreenPoint? ReadJson(JsonReader reader, Type type, ScreenPoint? existing, bool hasExisting, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null) return null;
        var a = JArray.Load(reader);
        if (a.Count != 2 || a[0]?.Type != JTokenType.Integer || a[1]?.Type != JTokenType.Integer) throw new JsonSerializationException("Coordinates must be [x, y] or null.");
        return new ScreenPoint(a[0]!.Value<int>(), a[1]!.Value<int>());
    }
}

public sealed class ScreenRectConverter : JsonConverter<ScreenRect>
{
    public override void WriteJson(JsonWriter writer, ScreenRect value, JsonSerializer serializer) => new JArray(value.Left, value.Top, value.Right, value.Bottom).WriteTo(writer);
    public override ScreenRect ReadJson(JsonReader reader, Type type, ScreenRect existing, bool hasExisting, JsonSerializer serializer)
    {
        var a = JArray.Load(reader);
        if (a.Count != 4 || a.Any(v => v?.Type != JTokenType.Integer)) throw new JsonSerializationException("Draw region must be [left, top, right, bottom].");
        return new ScreenRect(a[0]!.Value<int>(), a[1]!.Value<int>(), a[2]!.Value<int>(), a[3]!.Value<int>());
    }
}
