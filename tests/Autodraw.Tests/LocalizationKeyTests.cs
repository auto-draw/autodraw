using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Autodraw.Painting.Recreate;
using Avalonia.Platform;
using Xunit;

namespace Autodraw.Tests;

/// <summary>
/// Guards the language files themselves.  <see cref="LocalizationManager"/> silently renders a
/// missing key as the key, so a typo, a forgotten translation or a file that fails to load would
/// only show up at runtime.  Two sources are checked: the files copied next to the test assembly
/// (content rules) and the Avalonia resources compiled into the app (runtime lookup).
/// </summary>
public sealed class LocalizationKeyTests
{
    private static readonly Regex NamedPlaceholder =
        new(@"\{[A-Za-z_][A-Za-z0-9_]*\}", RegexOptions.Compiled);

    private static readonly Regex NumericPlaceholder = new(@"\{\d+\}", RegexOptions.Compiled);

    private static string LanguageDirectory => Path.Combine(AppContext.BaseDirectory, "Languages");

    private static SortedDictionary<string, Dictionary<string, string>> LoadLanguages()
    {
        Assert.True(Directory.Exists(LanguageDirectory),
            $"Language files were not copied to '{LanguageDirectory}'.");

        var files = Directory.GetFiles(LanguageDirectory, "*.json");
        Assert.Equal(LocalizationManager.Languages.Count, files.Length);

        var languages = new SortedDictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            var strings = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.String) continue;
                strings[property.Name] = property.Value.GetString() ?? string.Empty;
            }

            Assert.NotEmpty(strings);
            languages[Path.GetFileName(file)] = strings;
        }

        return languages;
    }

    [Fact]
    public void EveryLanguageResourceIsEmbeddedAndParses()
    {
        var loader = new StandardAssetLoader(typeof(LocalizationManager).Assembly);

        foreach (var language in LocalizationManager.Languages)
        {
            var uri = new Uri($"avares://Autodraw/Assets/Languages/{language.FileName}");
            using var stream = loader.Open(uri);
            using var reader = new StreamReader(stream);
            using var document = JsonDocument.Parse(reader.ReadToEnd());

            var count = document.RootElement.EnumerateObject().Count(property => property.Value.ValueKind == JsonValueKind.String);
            Assert.True(count > 0, $"{language.FileName} embedded no strings.");
        }
    }

    [Fact]
    public void EveryLanguageFileHasTheSameKeys()
    {
        var languages = LoadLanguages();
        var reference = languages["en.json"].Keys.OrderBy(key => key, StringComparer.Ordinal).ToArray();

        foreach (var (name, strings) in languages)
        {
            var keys = strings.Keys.OrderBy(key => key, StringComparer.Ordinal).ToArray();
            var missing = reference.Except(keys, StringComparer.Ordinal).ToArray();
            var extra = keys.Except(reference, StringComparer.Ordinal).ToArray();

            Assert.True(missing.Length == 0, $"{name} is missing key(s): {string.Join(", ", missing)}");
            Assert.True(extra.Length == 0, $"{name} has key(s) that English does not: {string.Join(", ", extra)}");
        }
    }

    [Fact]
    public void PickerPromptsExistInEveryLanguage()
    {
        var languages = LoadLanguages();
        var pickerKeys = PickerRegistry.All
            .SelectMany(definition => definition.Points
                .Select(point => point.PromptKey)
                .Append(definition.DisplayNameKey))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(pickerKeys);
        foreach (var (name, strings) in languages)
        {
            var missing = pickerKeys.Where(key => !strings.ContainsKey(key)).ToArray();
            Assert.True(missing.Length == 0,
                $"{name} is missing picker prompt(s): {string.Join(", ", missing)}");
        }
    }

    [Fact]
    public void LocalizedTextUsesNamedPlaceholdersOnly()
    {
        var languages = LoadLanguages();

        foreach (var (name, strings) in languages)
        {
            var offenders = strings
                .Where(pair => NumericPlaceholder.IsMatch(pair.Value))
                .Select(pair => pair.Key)
                .OrderBy(key => key, StringComparer.Ordinal)
                .ToArray();

            Assert.True(offenders.Length == 0,
                $"{name} uses numeric placeholders ({{0}}) in: {string.Join(", ", offenders)}. " +
                "LocalizationManager.F only substitutes named placeholders such as {picker}.");
        }
    }

    [Fact]
    public void PlaceholderNamesMatchEnglishForEveryKey()
    {
        var languages = LoadLanguages();
        var english = languages["en.json"];

        foreach (var (name, strings) in languages)
        {
            if (name == "en.json") continue;

            foreach (var (key, value) in strings)
            {
                var expected = Placeholders(english[key]);
                var actual = Placeholders(value);
                Assert.True(expected.SetEquals(actual),
                    $"{name}:{key} has placeholders [{string.Join(", ", actual)}] " +
                    $"but English has [{string.Join(", ", expected)}].");
            }
        }
    }

    private static HashSet<string> Placeholders(string text)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in NamedPlaceholder.Matches(text)) set.Add(match.Value);
        return set;
    }

    /// <summary>
    /// Keys that predate the picker rework and are referenced by nothing.  They are inert data, not
    /// runtime errors, so they are listed here instead of being deleted wholesale with the older
    /// UI.  The list is a ratchet: remove an entry in the same change that deletes its key.
    /// </summary>
    private static readonly HashSet<string> KnownUnusedKeys = new(StringComparer.Ordinal)
    {
        "_language_name",
        "confirm_clear_cache_body",
        "confirm_clear_cache_title",
        "confirm_exit_body",
        "confirm_exit_drawing_body",
        "confirm_exit_title",
        "confirm_reset_body",
        "confirm_reset_drawing_body",
        "confirm_reset_title",
        "error_calib_popup_title",
        "error_dependency_finder_body",
        "error_dependency_pillow_body",
        "error_dependency_pynput_body",
        "error_dependency_pynput_title",
        "error_load_config_body",
        "error_load_config_title",
        "error_save_config_body",
        "error_save_config_title",
        "filetype_all",
        "filetype_image",
        "filetype_json",
        "info_complete_body",
        "info_complete_title",
        "label_config_none",
        "label_preview_error",
        "label_safe_search",
        "lf_options",
        "lf_results",
        "status_area_selection_start",
        "status_cache_clear_error",
        "status_cache_delete_error",
        "status_color_select_error",
        "status_drawing_color",
        "status_error_file_not_found",
        "status_error_unknown_picker",
        "status_esrgan_exception",
        "status_esrgan_not_found",
        "status_invalid_brush",
        "status_invalid_delay",
        "status_invalid_k",
        "status_lang_error",
        "status_manualq_disabled",
        "status_paste_failed",
        "status_quantize_f6",
        "status_select_image",
        "status_stopping",
    };

    [Fact]
    public void EveryKeyIsReferencedOrListedAsUnused()
    {
        var languages = LoadLanguages();
        var repositoryRoot = FindRepositoryRoot();
        var source = string.Concat(EnumerateProductSource(repositoryRoot).Select(File.ReadAllText));

        var unused = languages["en.json"].Keys
            .Where(key => !source.Contains(key, StringComparison.Ordinal))
            .Where(key => !KnownUnusedKeys.Contains(key))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();

        Assert.True(unused.Length == 0,
            "Unused localization keys — wire them up or delete them from every language file: " +
            string.Join(", ", unused));
    }

    /// <summary>Walks the app sources only, skipping build output, tests and gitignored content.</summary>
    private static IEnumerable<string> EnumerateProductSource(string root)
    {
        var skipped = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "bin", "obj", "archive", "tests", "HexedDraw", "LxPainter V1.8",
        };

        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var current = pending.Pop();

            foreach (var file in Directory.EnumerateFiles(current))
            {
                var extension = Path.GetExtension(file);
                if (extension.Equals(".cs", StringComparison.OrdinalIgnoreCase) ||
                    extension.Equals(".axaml", StringComparison.OrdinalIgnoreCase))
                {
                    yield return file;
                }
            }

            foreach (var child in Directory.EnumerateDirectories(current))
            {
                var name = Path.GetFileName(child);
                if (skipped.Contains(name) || name.StartsWith(".", StringComparison.Ordinal)) continue;
                pending.Push(child);
            }
        }
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Autodraw.sln")))
                return directory.FullName;
        }

        throw new InvalidOperationException(
            $"Could not find Autodraw.sln above '{AppContext.BaseDirectory}'. " +
            "The localization liveness check needs the repository sources.");
    }
}
