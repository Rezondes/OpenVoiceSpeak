using System.Buffers.Binary;
using Avalonia.Headless.XUnit;
using OVS.Client.Services;
using OVS.Shared.Protocol;
using OVS.Tests.TestSupport;
using SkiaSharp;

namespace OVS.Tests.Client.Services;

/// <summary>Package 30: the client checks and scales a logo file before anything is sent.</summary>
public sealed class IconImportTests() : TempDirectoryTest("ovs-icon-", quiet: false)
{
    static int PngWidth(byte[] png) => BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16));

    [AvaloniaFact]
    public void Import_TooBigWrongFormatNotSquareTooSmall_Rejected()
    {
        var big = Path.Combine(dir, "gross.png");
        var bytes = new byte[IconImport.MaxFileBytes + 1];
        TestImages.Encode(64, 64).CopyTo(bytes, 0);
        File.WriteAllBytes(big, bytes);
        Assert.Contains("3 MB", IconImport.Prepare(big).Error, StringComparison.Ordinal);

        var gif = Path.Combine(dir, "bild.gif");
        File.WriteAllBytes(gif, "GIF89a..."u8.ToArray());
        Assert.Contains("PNG- und JPG", IconImport.Prepare(gif).Error, StringComparison.Ordinal);

        Assert.Contains("quadratisch", IconImport.Prepare(TestImages.Write(dir, "breit.png", 300, 200)).Error, StringComparison.Ordinal);
        Assert.Contains("mindestens 64", IconImport.Prepare(TestImages.Write(dir, "klein.png", 32, 32)).Error, StringComparison.Ordinal);
        Assert.Contains("gibt es nicht", IconImport.Prepare(Path.Combine(dir, "fehlt.png")).Error, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void Import_Jpg1024_Becomes256Png()
    {
        var (png, error) = IconImport.Prepare(TestImages.Write(dir, "gross.jpg", 1024, 1024, SKEncodedImageFormat.Jpeg));
        Assert.Null(error);
        Assert.Null(ServerIconFormat.Validate(png));
        Assert.Equal(IconImport.TargetSize, PngWidth(png!));
    }

    [AvaloniaFact]
    public void Import_SmallSquarePng_KeepsItsSize()
    {
        var (png, error) = IconImport.Prepare(TestImages.Write(dir, "mittel.png", 128, 128));
        Assert.Null(error);
        Assert.Equal(128, PngWidth(png!));
    }
}
