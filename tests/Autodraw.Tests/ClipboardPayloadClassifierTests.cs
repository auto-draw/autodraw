using System;
using System.IO;
using Autodraw.Configuration;
using Autodraw.ImageInput;
using Xunit;

namespace Autodraw.Tests;

public class ClipboardPayloadClassifierTests
{
    [Theory]
    [InlineData("image.png")]
    [InlineData("photo.JPG")]
    [InlineData("scan.jpeg")]
    [InlineData("shot.bmp")]
    [InlineData("anim.gif")]
    [InlineData("modern.webp")]
    public void Classify_SupportedFileReference_ReturnsFile(string path)
    {
        var payload = ClipboardPayloadClassifier.Classify(path, null);

        Assert.Equal(ClipboardPayloadKind.FileReference, payload.Kind);
        Assert.Equal(path, payload.Path);
        Assert.Null(payload.EncodedBytes);
    }

    [Fact]
    public void Classify_FileWinsOverPixels()
    {
        var payload = ClipboardPayloadClassifier.Classify("image.png", new byte[] { 1, 2, 3 });

        Assert.Equal(ClipboardPayloadKind.FileReference, payload.Kind);
    }

    [Fact]
    public void Classify_RawPixels_ReturnsEncodedImage()
    {
        var payload = ClipboardPayloadClassifier.Classify(null, new byte[] { 137, 80, 78, 71 });

        Assert.Equal(ClipboardPayloadKind.EncodedImage, payload.Kind);
        Assert.Null(payload.Path);
        Assert.Equal(4, payload.EncodedBytes!.Length);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("notes.txt", null)]
    [InlineData("notes.txt", new byte[0])]
    public void Classify_NeitherFileNorPixels_ReturnsNone(string? path, byte[]? bytes)
    {
        var payload = ClipboardPayloadClassifier.Classify(path, bytes);

        Assert.Equal(ClipboardPayloadKind.None, payload.Kind);
        Assert.False(payload.IsValid);
    }

    [Fact]
    public void Classify_SupportedFileWithEmptyPixels_StillReturnsFile()
    {
        var payload = ClipboardPayloadClassifier.Classify("image.png", Array.Empty<byte>());

        Assert.Equal(ClipboardPayloadKind.FileReference, payload.Kind);
        Assert.Equal("image.png", payload.Path);
    }

    [Fact]
    public void IsDroppableFile_RequiresSupportedExtensionAndExistingFile()
    {
        var png = Path.Combine(Path.GetTempPath(), $"autodraw-{Guid.NewGuid():N}.png");
        var txt = Path.ChangeExtension(png, ".txt");
        File.WriteAllBytes(png, new byte[] { 1 });
        File.WriteAllBytes(txt, new byte[] { 1 });
        try
        {
            Assert.True(ClipboardPayloadClassifier.IsDroppableFile(png));
            Assert.False(ClipboardPayloadClassifier.IsDroppableFile(txt));
            Assert.False(ClipboardPayloadClassifier.IsDroppableFile(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png")));
            Assert.False(ClipboardPayloadClassifier.IsDroppableFile(null));
        }
        finally
        {
            File.Delete(png);
            File.Delete(txt);
        }
    }
}
