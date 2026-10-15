using System.Text;
using OVS.Shared.Protocol;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Shared.Protocol;

/// <summary>Package 168 (A170): the server stores only a well-formed 128 px PNG with nothing but pixels.</summary>
public sealed class AvatarFormatTests
{
    [Fact]
    public void Valid_128Png_Accepted()
    {
        Assert.Null(AvatarFormat.Validate(TestPng.Rgba()));
        // a palette picture with transparency
        var palette = TestPng.File([TestPng.Chunk("IHDR", TestPng.Header(128, 128, 8, 3)), TestPng.Chunk("PLTE", [0, 0, 0, 255, 255, 255]),
            TestPng.Chunk("tRNS", [0]), TestPng.Chunk("IDAT", TestPng.Deflate(TestPng.Raw(128, 1))), TestPng.Chunk("IEND", [])]);
        Assert.Null(AvatarFormat.Validate(palette));
    }

    [Fact]
    public void Executable_Renamed_Refused()
    {
        var exe = new byte[4096];
        "MZ"u8.CopyTo(exe);
        Assert.NotNull(AvatarFormat.Validate(exe));
        Assert.NotNull(AvatarFormat.Validate([]));
        Assert.NotNull(AvatarFormat.Validate([.. TestPng.Rgba()[..8], .. exe])); // a PNG signature in front of something else
    }

    [Theory]
    [InlineData("tEXt")]
    [InlineData("iTXt")]
    [InlineData("zTXt")]
    [InlineData("eXIf")]
    [InlineData("iCCP")]
    [InlineData("acTL")]
    [InlineData("zzZz")]
    public void Png_TextChunk_Refused(string type) =>
        Assert.NotNull(AvatarFormat.Validate(TestPng.Rgba(extraChunks: [TestPng.Chunk(type, Encoding.ASCII.GetBytes("Comment\0<script>"))])));

    [Fact]
    public void Chunk_BadCrc_Refused()
    {
        var png = TestPng.Rgba();
        png[^20] ^= 0xFF; // a byte of the last IDAT's data or its CRC
        Assert.NotNull(AvatarFormat.Validate(png));
    }

    [Fact]
    public void Png_TrailingBytes_Refused()
    {
        Assert.NotNull(AvatarFormat.Validate(TestPng.Rgba(trailing: [0])));
        Assert.NotNull(AvatarFormat.Validate(TestPng.Rgba(trailing: TestPng.Chunk("IEND", []))));
        Assert.NotNull(AvatarFormat.Validate(TestPng.Rgba().AsSpan(..^12))); // no IEND
    }

    [Fact]
    public void Pixels_SizeMismatch_Refused()
    {
        var raw = TestPng.Raw(128, 4);
        Assert.NotNull(AvatarFormat.Validate(TestPng.Rgba(pixels: raw[..^1]))); // one byte short
        Assert.NotNull(AvatarFormat.Validate(TestPng.Rgba(pixels: [.. raw, 0]))); // one byte more
        Assert.NotNull(AvatarFormat.Validate(TestPng.File([TestPng.Chunk("IHDR", TestPng.Header(128, 128, 8, 6)), TestPng.Chunk("IDAT", [1, 2, 3]),
            TestPng.Chunk("IEND", [])]))); // no zlib data at all
    }

    [Fact]
    public void Png_TooLarge_Refused()
    {
        var noise = new byte[TestPng.Raw(128, 4).Length];
        new Random(168).NextBytes(noise); // does not compress: well over 64 KB
        Assert.True(TestPng.Rgba(pixels: noise).Length > AvatarFormat.MaxBytes);
        Assert.NotNull(AvatarFormat.Validate(TestPng.Rgba(pixels: noise)));
    }

    [Theory]
    [InlineData(127, 127)]
    [InlineData(129, 129)]
    [InlineData(128, 64)]
    [InlineData(256, 256)]
    public void Png_WrongSides_Refused(int width, int height)
    {
        var png = TestPng.File([TestPng.Chunk("IHDR", TestPng.Header(width, height, 8, 6)), TestPng.Chunk("IDAT", TestPng.Deflate(new byte[height * (1 + (width * 4))])),
            TestPng.Chunk("IEND", [])]);
        Assert.NotNull(AvatarFormat.Validate(png));
    }

    /// <summary>8 MB of zeros deflate to about 8 KB, within the size limit; the check stops one byte past the 65,664 a picture holds.</summary>
    [Fact]
    public void DeflateBomb_Huge_StopsAtExpectedSize()
    {
        var bomb = TestPng.Rgba(pixels: new byte[8 * 1024 * 1024]);
        Assert.True(bomb.Length < AvatarFormat.MaxBytes);
        Assert.NotNull(AvatarFormat.Validate(bomb));
    }
}
