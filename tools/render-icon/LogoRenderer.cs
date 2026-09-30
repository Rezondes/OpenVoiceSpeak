using System.Globalization;
using System.Xml.Linq;
using SkiaSharp;

namespace OVS.Tools;

/// <summary>
/// Package 95: renders src/OVS.Client/Assets/logo.svg to PNG and to the multi-size ICO. It understands the subset
/// the logo uses (one rounded rect, filled or stroked paths). The tests compile this file too, so the checked-in
/// icon and the tool can not drift apart.
/// </summary>
public static class LogoRenderer
{
    public static readonly int[] IcoSizes = [16, 24, 32, 48, 64, 256];

    /// <summary>Below this size the path with id="wave" gets <see cref="SmallWaveWidth"/>, so it stays at least one pixel wide.</summary>
    public const int SmallSize = 32;

    /// <summary>Wave stroke width in the 256 grid for small sizes: 1.25 px at 16 px (the SVG's 10 would be 0.6 px), so the bars fill whole pixels.</summary>
    public const float SmallWaveWidth = 20;

    public static byte[] RenderPng(string svg, int size)
    {
        var root = XDocument.Parse(svg).Root!;
        var info = new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        canvas.Scale(size / Number(root, "width"));
        foreach (var element in root.Elements())
        {
            switch (element.Name.LocalName)
            {
                case "rect":
                    using (var paint = new SKPaint { IsAntialias = true, Color = SKColor.Parse(element.Attribute("fill")!.Value) })
                    {
                        float rx = Number(element, "rx");
                        canvas.DrawRoundRect(0, 0, Number(element, "width"), Number(element, "height"), rx, rx, paint);
                    }
                    break;
                case "path":
                    using (var path = SKPath.ParseSvgPathData(element.Attribute("d")!.Value))
                    using (var paint = new SKPaint { IsAntialias = true })
                    {
                        var fill = (string?)element.Attribute("fill");
                        if (fill is not null && fill != "none")
                        {
                            paint.Color = SKColor.Parse(fill);
                            canvas.DrawPath(path, paint);
                        }
                        if ((string?)element.Attribute("stroke") is { } stroke)
                        {
                            paint.Color = SKColor.Parse(stroke);
                            paint.Style = SKPaintStyle.Stroke;
                            paint.StrokeWidth = (string?)element.Attribute("id") == "wave" && size < SmallSize ? SmallWaveWidth : Number(element, "stroke-width");
                            paint.StrokeCap = (string?)element.Attribute("stroke-linecap") == "round" ? SKStrokeCap.Round : SKStrokeCap.Butt;
                            canvas.DrawPath(path, paint);
                        }
                    }
                    break;
            }
        }
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>An ICO with one PNG-compressed frame per size in <see cref="IcoSizes"/>.</summary>
    public static byte[] RenderIco(string svg)
    {
        var frames = IcoSizes.Select(s => RenderPng(svg, s)).ToList();
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0); // reserved
        writer.Write((ushort)1); // type: icon
        writer.Write((ushort)frames.Count);
        int offset = 6 + 16 * frames.Count;
        for (int i = 0; i < frames.Count; i++)
        {
            writer.Write((byte)(IcoSizes[i] % 256)); // 0 means 256
            writer.Write((byte)(IcoSizes[i] % 256));
            writer.Write((byte)0); // no palette
            writer.Write((byte)0); // reserved
            writer.Write((ushort)1); // planes
            writer.Write((ushort)32); // bits per pixel
            writer.Write(frames[i].Length);
            writer.Write(offset);
            offset += frames[i].Length;
        }
        foreach (var frame in frames) writer.Write(frame);
        writer.Flush();
        return stream.ToArray();
    }

    static float Number(XElement element, string name) => float.Parse(element.Attribute(name)!.Value, CultureInfo.InvariantCulture);
}
