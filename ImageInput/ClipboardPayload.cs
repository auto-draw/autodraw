using System;
using System.Collections.Generic;
using System.IO;

namespace Autodraw.ImageInput;

public enum ClipboardPayloadKind
{
    None,
    FileReference,
    EncodedImage
}

/// <summary>A clipboard/drop payload after classification: either a file on disk or raw encoded
/// image bytes that must be wrapped in memory.</summary>
public readonly record struct ClipboardPayload(ClipboardPayloadKind Kind, string? Path, byte[]? EncodedBytes)
{
    public static readonly ClipboardPayload None = new(ClipboardPayloadKind.None, null, null);

    public static ClipboardPayload FromFile(string path) =>
        new(ClipboardPayloadKind.FileReference, path, null);

    public static ClipboardPayload FromBytes(byte[] bytes) =>
        new(ClipboardPayloadKind.EncodedImage, null, bytes);

    public bool IsValid => Kind != ClipboardPayloadKind.None;
}

public static class ClipboardPayloadClassifier
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".tif", ".tiff", ".ico"
    };

    public static bool IsSupportedImagePath(string? path) =>
        !string.IsNullOrWhiteSpace(path) && ImageExtensions.Contains(Path.GetExtension(path));

    /// <summary>Chooses the file reference over raw pixels (matching the "file wins" rule) and
    /// rejects payloads that carry neither. Existence and decodability are validated by the caller
    /// so this function stays pure and unit-testable.</summary>
    public static ClipboardPayload Classify(string? filePath, byte[]? encodedBytes)
    {
        if (IsSupportedImagePath(filePath)) return ClipboardPayload.FromFile(filePath!);
        if (encodedBytes is { Length: > 0 }) return ClipboardPayload.FromBytes(encodedBytes);
        return ClipboardPayload.None;
    }

    /// <summary>Drag-and-drop validation: only supported image extensions are accepted before the
    /// file is decoded.</summary>
    public static bool IsDroppableFile(string? path) =>
        IsSupportedImagePath(path) && File.Exists(path);
}
