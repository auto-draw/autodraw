using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Avalonia.Platform;
using Newtonsoft.Json.Linq;

namespace Autodraw;

public sealed record UiLanguage(string DisplayName, string Code, string FileName)
{
    public override string ToString() => DisplayName;
}

/// <summary>Runtime JSON localization for the HexedDrawer panel.  The language files are the
/// same ones used by HexedDrawer so the ported tabs can be switched without a rebuild.</summary>
public static class LocalizationManager
{
    public static readonly IReadOnlyList<UiLanguage> Languages = new[]
    {
        new UiLanguage("English", "en", "en.json"),
        new UiLanguage("Deutsch", "de", "de.json"),
        new UiLanguage("Español", "es", "es.json"),
        new UiLanguage("Français", "fr", "fr.json"),
        new UiLanguage("한국어", "ko", "ko.json"),
        new UiLanguage("Русский", "ru", "ru.json"),
        new UiLanguage("Tiếng Việt", "vi", "vi.json")
    };

    private static Dictionary<string, string> _strings = LoadFile("en.json");
    public static string CurrentCode { get; private set; } = "en";

    public static UiLanguage? Find(string code)
    {
        var normalized = (code ?? string.Empty).Trim();
        if (normalized.Length > 2) normalized = normalized[..2];
        return Languages.FirstOrDefault(language => language.Code.Equals(normalized, StringComparison.OrdinalIgnoreCase))
               ?? Languages.FirstOrDefault(language => language.Code.Equals("en", StringComparison.OrdinalIgnoreCase));
    }

    public static void Load(string code)
    {
        var language = Find(code) ?? Languages[0];
        _strings = LoadFile(language.FileName);
        CurrentCode = language.Code;

        CultureInfo culture;
        try { culture = new CultureInfo(language.Code); }
        catch { culture = CultureInfo.InstalledUICulture; }
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Config.SetEntry("userlang", language.Code);
    }

    private static Dictionary<string, string> LoadFile(string fileName)
    {
        try
        {
            var uri = new Uri($"avares://Autodraw/Assets/Languages/{fileName}");
            using var stream = AssetLoader.Open(uri);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var root = JObject.Parse(reader.ReadToEnd());
            return root.Properties()
                .Where(property => property.Value.Type == JTokenType.String)
                .ToDictionary(property => property.Name, property => property.Value.Value<string>() ?? string.Empty);
        }
        catch (Exception ex)
        {
            Utils.Log($"Failed to load language '{fileName}': {ex.Message}");
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public static string T(string key)
        => _strings.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value) ? value : key;

    private static readonly Regex NamedPlaceholder =
        new(@"\{[A-Za-z_][A-Za-z0-9_]*\}", RegexOptions.Compiled);

    /// <summary>Formats a localized string. HexedDrawer's language files use named
    /// placeholders such as <c>{method}</c>; values are substituted in argument order.</summary>
    public static string F(string key, params object?[] args)
    {
        var format = T(key);
        if (args.Length == 0 || format.IndexOf('{') < 0) return format;
        var index = 0;
        return NamedPlaceholder.Replace(format, match =>
        {
            if (index >= args.Length) return match.Value;
            var value = Convert.ToString(args[index++], CultureInfo.CurrentCulture);
            return value ?? string.Empty;
        });
    }
}
