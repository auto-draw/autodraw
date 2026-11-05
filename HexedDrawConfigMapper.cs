using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodraw.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Autodraw;

/// <summary>Converts between AutoDraw's portable dd configuration and HexedDrawer's
/// SpeedDraw.json schema so existing HexedDrawer configs can be loaded and shared.</summary>
public static class HexedDrawConfigMapper
{
    public static string ConfigDirectory => Path.Combine(Config.FolderPath, "Configs");

    public static string EnsureConfigDirectory()
    {
        Directory.CreateDirectory(ConfigDirectory);
        return ConfigDirectory;
    }

    public static IReadOnlyList<string> ListConfigs()
    {
        try
        {
            EnsureConfigDirectory();
            return Directory.GetFiles(ConfigDirectory, "*.json")
                .OrderByDescending(File.GetLastWriteTime)
                .ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public static bool LooksLikeHexedDraw(JObject root)
        => root["k_clusters"] is not null || root["draw_delay"] is not null || root["color_picking_method"] is not null;

    /// <summary>Loads either a SpeedDraw.json (HexedDrawer) or auto-draw config and applies it.</summary>
    public static bool TryImport(string path, out string error)
    {
        try
        {
            var text = File.ReadAllText(path);
            var root = JObject.Parse(text);
            var config = LooksLikeHexedDraw(root) ? FromHexedDraw(root) : JsonConvert.DeserializeObject<AutoDrawConfig>(text);
            if (config is null)
            {
                error = "The configuration file could not be parsed.";
                return false;
            }
            if (!DdConfigService.TryApply(config, out error)) return false;
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static bool SaveAutoDraw(string path, out string error)
    {
        try
        {
            DdConfigService.Export(path);
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static bool SaveHexedDraw(string path, out string error)
    {
        try
        {
            File.WriteAllText(path, ToHexedDraw(DdConfigService.Current).ToString(Formatting.Indented));
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static AutoDrawConfig FromHexedDraw(JObject root)
    {
        var config = DdConfigService.Clone(DdConfigService.Current);
        config.KClusters = ReadInt(root, "k_clusters", config.KClusters, 1, 256);
        if (root["draw_delay"] is not null && double.TryParse(root["draw_delay"]!.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var delay))
        {
            config.DrawDelaySeconds = Math.Max(0, delay);
            config.Interval = (long)Math.Clamp(delay * 10_000_000d, 0, long.MaxValue);
        }
        config.BrushSize = ReadInt(root, "brush_size", config.BrushSize, 1, 64);
        config.AutoQuantize = ReadBool(root, "auto_quantize", config.AutoQuantize);
        config.EnhanceImage = ReadBool(root, "enhance_image", config.EnhanceImage);
        var mode = ReadString(root, "mode", config.Mode);
        config.Mode = mode.Equals("bw", StringComparison.OrdinalIgnoreCase) || mode.Contains("black", StringComparison.OrdinalIgnoreCase)
            ? "bw" : "color";

        var stray = ReadString(root, "stray_removal_mode", config.StrayRemovalMode);
        config.RemoveStray = stray.Contains("smart", StringComparison.OrdinalIgnoreCase) || stray.Contains("v5", StringComparison.OrdinalIgnoreCase);
        config.StrayRemovalMode = config.RemoveStray ? "Smart Merge (V5)" : "Disabled";

        config.DfsMode = ReadString(root, "dfs_mode", config.DfsMode) switch
        {
            var value when value.Contains('4') => "4-Directional",
            _ => "8-Directional"
        };
        config.LayerOrder = ReadString(root, "draw_order", config.LayerOrder) switch
        {
            var value when value.Contains("large", StringComparison.OrdinalIgnoreCase) => "Largest area first",
            var value when value.Contains("bright", StringComparison.OrdinalIgnoreCase) => "Brightness to darkness",
            _ => "Darkness to brightness"
        };
        config.DrawerModule = ReadString(root, "drawer_module", config.DrawerModule).Contains("old", StringComparison.OrdinalIgnoreCase)
            ? "Old (No Bridge)" : "New (A* Bridge)";
        var picker = ReadString(root, "picker_type", ReadString(root, "color_picking_method", config.ColorPickingMethod));
        config.ColorPickingMethod = picker.Contains("box", StringComparison.OrdinalIgnoreCase) ? "Box" : "Circle";
        // Only the wheel and box pickers are implemented; every other imported picker type
        // becomes the wheel so the imported profile stays editable instead of failing validation.
        config.PickerType = picker.Contains("box", StringComparison.OrdinalIgnoreCase) ||
                            config.ColorPickingMethod == "Box"
            ? "Box"
            : "Wheel";

        config.WheelDirection = ReadString(root, "palette_rotation_direction_calib", config.WheelDirection)
            .Contains("cw", StringComparison.OrdinalIgnoreCase) ? "CW" : "CCW";

        if (root["circle_params_calib"] is JArray circle && circle.Count >= 3)
        {
            var cx = circle[0].Value<double>();
            var cy = circle[1].Value<double>();
            var radius = circle[2].Value<double>();
            config.WheelCenter = new ScreenPoint((int)Math.Round(cx), (int)Math.Round(cy));
            config.WheelEdge = new ScreenPoint((int)Math.Round(cx + radius), (int)Math.Round(cy));
            if (circle.Count >= 4) config.WheelAngle = circle[3].Value<double>();
        }
        if (root["slider_params_calib"] is JArray slider && slider.Count >= 2)
        {
            config.WheelDark = ToPoint(slider[0]);
            config.WheelBright = ToPoint(slider[1]);
        }
        if (root["box_params_calib"] is JArray box && box.Count >= 2)
        {
            config.BoxTopLeft = ToPoint(box[0]);
            config.BoxBottomRight = ToPoint(box[1]);
        }
        if (root["hue_slider_params_calib"] is JArray hue && hue.Count >= 2)
        {
            config.BoxHueStart = ToPoint(hue[0]);
            config.BoxHueEnd = ToPoint(hue[1]);
        }

        var smoothing = ReadString(root, "smoothing_level", string.Empty);
        if (root["quantize_style"] is null && smoothing.Length > 0)
            config.QuantizeStyle = smoothing.Equals("none", StringComparison.OrdinalIgnoreCase)
                ? "Pixelated (Classic)" : "Vectorized (Smooth)";
        DdConfigService.Normalize(config);
        return config;
    }

    public static JObject ToHexedDraw(AutoDrawConfig config)
    {
        var center = config.WheelCenter ?? new ScreenPoint(0, 0);
        var edge = config.WheelEdge ?? center;
        var radius = Math.Sqrt(Math.Pow(edge.X - center.X, 2) + Math.Pow(edge.Y - center.Y, 2));
        return new JObject
        {
            ["k_clusters"] = config.KClusters,
            ["draw_delay"] = Math.Round(config.Interval / 10_000_000d, 6),
            ["brush_size"] = config.BrushSize,
            ["mode"] = config.Mode == "bw" ? "bw" : "color",
            ["stray_removal_mode"] = config.RemoveStray ? "smart_merge_v5" : "disabled",
            ["auto_quantize"] = config.AutoQuantize,
            ["enhance_image"] = config.EnhanceImage,
            ["smoothing_level"] = config.QuantizeStyle.Contains("vector", StringComparison.OrdinalIgnoreCase) ? "light" : "none",
            ["dfs_mode"] = config.DfsMode.Contains('4') ? "4dir" : "8dir",
            ["draw_order"] = config.LayerOrder.Contains("largest", StringComparison.OrdinalIgnoreCase)
                ? "largest_area_first"
                : config.LayerOrder.Contains("bright", StringComparison.OrdinalIgnoreCase)
                    ? "brightness_to_darkest" : "darkest_to_brightest",
            ["drawer_module"] = config.DrawerModule.Contains("old", StringComparison.OrdinalIgnoreCase) ? "old" : "new",
            ["picker_type"] = config.PickerType,
            ["color_picking_method"] = config.PickerType.Equals("Box", StringComparison.OrdinalIgnoreCase) ? "box" : "circle",
            ["palette_rotation_direction_calib"] = config.WheelDirection.Equals("CW", StringComparison.OrdinalIgnoreCase) ? "cw" : "ccw",
            ["circle_params_calib"] = new JArray(center.X, center.Y, Math.Round(radius, 3), Math.Round(config.WheelAngle, 3)),
            ["slider_params_calib"] = new JArray(
                ToArray(config.WheelDark ?? new ScreenPoint(0, 0)),
                ToArray(config.WheelBright ?? new ScreenPoint(0, 0))),
            ["box_params_calib"] = config.BoxTopLeft is { } topLeft && config.BoxBottomRight is { } bottomRight
                ? new JArray(ToArray(topLeft), ToArray(bottomRight)) : JValue.CreateNull(),
            ["hue_slider_params_calib"] = config.BoxHueStart is { } hueStart && config.BoxHueEnd is { } hueEnd
                ? new JArray(ToArray(hueStart), ToArray(hueEnd)) : JValue.CreateNull()
        };
    }

    private static JArray ToArray(ScreenPoint point) => new(point.X, point.Y);

    private static ScreenPoint ToPoint(JToken token)
    {
        var x = token[0]?.Value<double>() ?? 0;
        var y = token[1]?.Value<double>() ?? 0;
        return new ScreenPoint((int)Math.Round(x), (int)Math.Round(y));
    }

    private static int ReadInt(JObject root, string name, int fallback, int min, int max)
        => root[name] is { } token && int.TryParse(token.ToString(), out var value) ? Math.Clamp(value, min, max) : fallback;

    private static bool ReadBool(JObject root, string name, bool fallback)
        => root[name] is { } token && bool.TryParse(token.ToString(), out var value) ? value : fallback;

    private static string ReadString(JObject root, string name, string fallback)
        => root[name] is { } token && token.Type == JTokenType.String ? token.Value<string>() ?? fallback : fallback;
}
