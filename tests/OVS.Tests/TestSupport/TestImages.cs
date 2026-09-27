using SkiaSharp;

namespace OVS.Tests.TestSupport;

/// <summary>Real, decodable test images (SkiaSharp comes with Avalonia).</summary>
public static class TestImages
{
    public static byte[] Encode(int width, int height, SKEncodedImageFormat format = SKEncodedImageFormat.Png)
    {
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        surface.Canvas.Clear(new SKColor(0x2F, 0x6F, 0xEB));
        using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        surface.Canvas.DrawCircle(width / 2f, height / 2f, Math.Min(width, height) / 3f, paint);
        using var image = surface.Snapshot();
        using var data = image.Encode(format, 90);
        return data.ToArray();
    }

    public static string Write(string dir, string name, int width, int height, SKEncodedImageFormat format = SKEncodedImageFormat.Png)
    {
        var path = Path.Combine(dir, name);
        File.WriteAllBytes(path, Encode(width, height, format));
        return path;
    }
}
