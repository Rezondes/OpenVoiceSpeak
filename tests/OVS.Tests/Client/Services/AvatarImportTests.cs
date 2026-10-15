using System.IO.Compression;
using Avalonia.Headless.XUnit;
using OVS.Client.Services;
using OVS.Shared.Protocol;
using OVS.Tests.TestSupport;
using SkiaSharp;

namespace OVS.Tests.Client.Services;

/// <summary>Package 168: the client turns a chosen image into the 128 px PNG the server takes.</summary>
public sealed class AvatarImportTests() : TempDirectoryTest("ovs-avatar-", quiet: false)
{
    /// <summary>A GIF of 1 x 1 pixel with two frames: red, then blue.</summary>
    static readonly byte[] AnimatedGif =
    [
        .. "GIF89a"u8, 1, 0, 1, 0, 0x80, 0, 0, 0xFF, 0, 0, 0, 0, 0xFF,
        0x21, 0xF9, 4, 0, 10, 0, 0, 0, 0x2C, 0, 0, 0, 0, 1, 0, 1, 0, 0, 2, 2, 0x44, 0x01, 0,
        0x21, 0xF9, 4, 0, 10, 0, 0, 0, 0x2C, 0, 0, 0, 0, 1, 0, 1, 0, 0, 2, 2, 0x4C, 0x01, 0,
        0x3B,
    ];

    /// <summary>The first pixel of a picture <see cref="AvatarFormat.Encode"/> wrote (RGBA, after the row's filter byte).</summary>
    static byte[] FirstPixel(byte[] png)
    {
        // signature 8, IHDR chunk 25, IDAT length and type 8; the IDAT's CRC and the IEND chunk at the end
        using var zlib = new ZLibStream(new MemoryStream(png[41..^16]), CompressionMode.Decompress);
        var start = new byte[5];
        zlib.ReadExactly(start);
        return start[1..];
    }

    [AvaloniaFact]
    public void JpgGifWebp_ToSquarePng128_NoMetadata()
    {
        foreach (var (name, bytes) in new[]
        {
            ("breit.jpg", TestImages.Encode(300, 200, SKEncodedImageFormat.Jpeg)),
            ("hoch.webp", TestImages.Encode(200, 400, SKEncodedImageFormat.Webp)),
            ("gross.png", TestImages.Encode(1000, 1000)),
            ("klein.png", TestImages.Encode(40, 40)),
            ("bild.gif", AnimatedGif),
        })
        {
            var path = Path.Combine(dir, name);
            File.WriteAllBytes(path, bytes);
            var (png, error) = AvatarImport.Prepare(path);
            Assert.Null(error);
            Assert.Null(AvatarFormat.Validate(png)); // 128 px, square, pixels only: what the server stores
        }

        var big = Path.Combine(dir, "riesig.jpg");
        File.WriteAllBytes(big, [.. TestImages.Encode(64, 64, SKEncodedImageFormat.Jpeg), .. new byte[AvatarImport.MaxFileBytes]]);
        Assert.NotNull(AvatarImport.Prepare(big).Error);
        var exe = Path.Combine(dir, "bild.png");
        File.WriteAllBytes(exe, [.. "MZ"u8, .. new byte[100]]);
        Assert.NotNull(AvatarImport.Prepare(exe).Error);
        Assert.NotNull(AvatarImport.Prepare(Path.Combine(dir, "fehlt.png")).Error);
    }

    [AvaloniaFact]
    public void Animated_Gif_FirstFrame()
    {
        var path = Path.Combine(dir, "animiert.gif");
        File.WriteAllBytes(path, AnimatedGif);
        var (png, error) = AvatarImport.Prepare(path);
        Assert.Null(error);
        Assert.Equal([0xFF, 0, 0, 0xFF], FirstPixel(png!)); // red, the first frame
    }
}
