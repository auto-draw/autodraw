using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace Autodraw;

public sealed class ImageSearchResult : INotifyPropertyChanged
{
    private Bitmap? _thumbnail;

    public string Title { get; init; } = string.Empty;
    public string ImageUrl { get; init; } = string.Empty;
    public string ThumbnailUrl { get; init; } = string.Empty;
    public string PageUrl { get; init; } = string.Empty;

    public Bitmap? Thumbnail
    {
        get => _thumbnail;
        set
        {
            if (ReferenceEquals(_thumbnail, value)) return;
            _thumbnail = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Thumbnail)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>Small, dependency-free image search used by the HexedDrawer Image Finder tab.
/// DuckDuckGo's public image JSON endpoint is tried first; Bing image search HTML is the
/// fallback so the feature still works when one provider changes its markup.</summary>
public static class ImageFinder
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36");
        client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/json;q=0.9,*/*;q=0.8");
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
        return client;
    }

    public static async Task<IReadOnlyList<ImageSearchResult>> SearchAsync(string query, int limit, string safeSearch, CancellationToken cancellationToken)
    {
        query = query.Trim();
        if (query.Length == 0) return Array.Empty<ImageSearchResult>();
        limit = Math.Clamp(limit, 1, 100);

        try
        {
            var results = await SearchDuckDuckGoAsync(query, limit, safeSearch, cancellationToken);
            if (results.Count > 0) return results;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Utils.Log("DuckDuckGo image search failed: " + ex.Message);
        }

        try
        {
            return await SearchBingAsync(query, limit, safeSearch, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Utils.Log("Bing image search failed: " + ex.Message);
            return Array.Empty<ImageSearchResult>();
        }
    }

    private static async Task<List<ImageSearchResult>> SearchDuckDuckGoAsync(string query, int limit, string safeSearch, CancellationToken cancellationToken)
    {
        var escaped = Uri.EscapeDataString(query);
        var html = await Http.GetStringAsync($"https://duckduckgo.com/?q={escaped}&iax=images&ia=images", cancellationToken);
        var vqdMatch = Regex.Match(html, @"vqd\s*[=:]\s*[""']?([\d-]+)");
        if (!vqdMatch.Success) return new List<ImageSearchResult>();
        var vqd = vqdMatch.Groups[1].Value;

        var safe = safeSearch.Equals("Strict", StringComparison.OrdinalIgnoreCase) ? "1"
            : safeSearch.Equals("Off", StringComparison.OrdinalIgnoreCase) ? "-1" : "0";
        var json = await Http.GetStringAsync(
            $"https://duckduckgo.com/i.js?l=us-en&o=json&q={escaped}&vqd={vqd}&f=,,,,&p={safe}",
            cancellationToken);
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("results", out var resultsElement)) return new List<ImageSearchResult>();

        var results = new List<ImageSearchResult>();
        foreach (var item in resultsElement.EnumerateArray())
        {
            if (results.Count >= limit) break;
            var image = GetString(item, "image");
            if (string.IsNullOrWhiteSpace(image)) continue;
            results.Add(new ImageSearchResult
            {
                ImageUrl = image,
                ThumbnailUrl = GetString(item, "thumbnail"),
                Title = GetString(item, "title"),
                PageUrl = GetString(item, "url")
            });
        }
        return results;
    }

    private static async Task<List<ImageSearchResult>> SearchBingAsync(string query, int limit, string safeSearch, CancellationToken cancellationToken)
    {
        var safe = safeSearch.Equals("Strict", StringComparison.OrdinalIgnoreCase) ? "strict"
            : safeSearch.Equals("Off", StringComparison.OrdinalIgnoreCase) ? "off" : "moderate";
        var html = await Http.GetStringAsync(
            $"https://www.bing.com/images/search?q={Uri.EscapeDataString(query)}&first=1&count={Math.Min(limit * 2, 100)}&safeSearch={safe}",
            cancellationToken);

        var images = ExtractBingField(html, "murl");
        var thumbs = ExtractBingField(html, "turl");
        var titles = ExtractBingField(html, "t");

        var results = new List<ImageSearchResult>();
        for (var i = 0; i < images.Count && results.Count < limit; i++)
        {
            if (string.IsNullOrWhiteSpace(images[i])) continue;
            results.Add(new ImageSearchResult
            {
                ImageUrl = images[i],
                ThumbnailUrl = i < thumbs.Count ? thumbs[i] : string.Empty,
                Title = i < titles.Count ? titles[i] : string.Empty
            });
        }
        return results;
    }

    private static List<string> ExtractBingField(string html, string key)
    {
        var matches = Regex.Matches(html, $@"{key}&quot;:&quot;(.*?)&quot;|""{key}"":""(.*?)""");
        return matches.Select(match => WebUtility.HtmlDecode(match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value)).ToList();
    }

    private static string GetString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty : string.Empty;

    public static async Task LoadThumbnailAsync(ImageSearchResult result, CancellationToken cancellationToken)
    {
        var url = string.IsNullOrWhiteSpace(result.ThumbnailUrl) ? result.ImageUrl : result.ThumbnailUrl;
        if (string.IsNullOrWhiteSpace(url)) return;
        try
        {
            var bytes = await Http.GetByteArrayAsync(url, cancellationToken);
            using var data = SKData.CreateCopy(bytes);
            using var decoded = SKBitmap.Decode(data);
            if (decoded is null) return;
            const int target = 128;
            var scale = Math.Min(1d, target / (double)Math.Max(decoded.Width, decoded.Height));
            using var resized = scale < 1
                ? decoded.Resize(new SKImageInfo(Math.Max(1, (int)(decoded.Width * scale)), Math.Max(1, (int)(decoded.Height * scale))), SKFilterQuality.Medium)
                : decoded.Copy();
            result.Thumbnail = resized.ConvertToAvaloniaBitmap();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Utils.Log("Thumbnail download failed: " + ex.Message);
        }
    }

    public static async Task<string> DownloadAsync(string url, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Config.CachePath);
        var bytes = await Http.GetByteArrayAsync(url, cancellationToken);
        using (var probe = SKBitmap.Decode(bytes))
            if (probe is null) throw new InvalidDataException("The selected result is not a readable image.");

        var extension = ".png";
        var path = url.Split('?')[0];
        var dot = path.LastIndexOf('.');
        if (dot >= 0 && dot < path.Length - 1 && path.Length - dot <= 6)
        {
            var candidate = path[(dot + 1)..].ToLowerInvariant();
            if (candidate is "png" or "jpg" or "jpeg" or "webp" or "bmp" or "gif") extension = "." + candidate;
        }
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)))[..16];
        var filePath = Path.Combine(Config.CachePath, hash + extension);
        await File.WriteAllBytesAsync(filePath, bytes, cancellationToken);
        return filePath;
    }

    public static int ClearCache()
    {
        try
        {
            if (!Directory.Exists(Config.CachePath)) return 0;
            var files = Directory.GetFiles(Config.CachePath);
            foreach (var file in files)
            {
                try { File.Delete(file); } catch (Exception ex) { Utils.Log("Cache delete failed: " + ex.Message); }
            }
            return files.Length;
        }
        catch (Exception ex)
        {
            Utils.Log("Cache clear failed: " + ex.Message);
            return 0;
        }
    }
}
