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
    public static readonly int[] IcoSizes = [16, 20, 24, 32, 48, 64, 256];

    /// <summary>
    /// Pixel-hinted artwork for the small sizes, where the scaled-down SVG blurs the wave into a blob. Cups and wave bars
    /// are whole-pixel rectangles drawn without anti-aliasing, the headset arc is a thinner anti-aliased stroke whose
    /// centre line ends in the middle of the cups. All values in pixels of that size.
    /// Bar: wave bar width; Gap: space between bars and between the outer bars and the cups; Cup: cup width;
    /// Top/Bottom: rows of the cups and the middle bar; Inset: how much shorter the outer bars are at each end;
    /// Arc: arc stroke width.
    /// </summary>
    public sealed record SmallArt(int Bar, int Gap, int Cup, int Top, int Bottom, int Inset, float Arc);

    public static readonly Dictionary<int, SmallArt> Small = new()
    {
        [16] = new(Bar: 1, Gap: 1, Cup: 2, Top: 8, Bottom: 13, Inset: 1, Arc: 1f),
        [20] = new(Bar: 1, Gap: 1, Cup: 3, Top: 10, Bottom: 16, Inset: 1, Arc: 1.25f),
        [24] = new(Bar: 2, Gap: 1, Cup: 3, Top: 12, Bottom: 19, Inset: 1, Arc: 1.5f),
        [32] = new(Bar: 2, Gap: 2, Cup: 4, Top: 16, Bottom: 25, Inset: 2, Arc: 2f),
    };

    public static byte[] RenderPng(string svg, int size)
    {
        if (Small.TryGetValue(size, out var art)) return RenderSmall(svg, size, art);
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
                            paint.StrokeWidth = Number(element, "stroke-width");
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

    /// <summary>
    /// The small artwork of <paramref name="size"/>: the SVG's rounded square, then the headset and three wave bars on the
    /// pixel grid. With 1 px bars the middle bar sits on the pixel left of the centre, so the headset shifts with it.
    /// </summary>
    static byte[] RenderSmall(string svg, int size, SmallArt a)
    {
        var rect = XDocument.Parse(svg).Root!.Elements().First(e => e.Name.LocalName == "rect");
        var info = new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        using (var paint = new SKPaint { IsAntialias = true, Color = SKColor.Parse(rect.Attribute("fill")!.Value) })
        {
            float rx = Number(rect, "rx") * size / Number(rect, "width");
            canvas.DrawRoundRect(0, 0, size, size, rx, rx, paint);
        }

        int mid = (size - a.Bar) / 2; // left column of the middle bar
        int step = a.Bar + a.Gap;
        using var white = new SKPaint { IsAntialias = false, Color = SKColors.White };
        foreach (int x in new[] { mid - step, mid + step })
            canvas.DrawRect(SKRect.Create(x, a.Top + a.Inset, a.Bar, a.Bottom - a.Top - 2 * a.Inset), white);
        canvas.DrawRect(SKRect.Create(mid, a.Top, a.Bar, a.Bottom - a.Top), white);
        int leftCup = mid - step - a.Gap - a.Cup, rightCup = mid + step + a.Bar + a.Gap;
        canvas.DrawRect(SKRect.Create(leftCup, a.Top, a.Cup, a.Bottom - a.Top), white);
        canvas.DrawRect(SKRect.Create(rightCup, a.Top, a.Cup, a.Bottom - a.Top), white);

        // The arc's outer edge lines up with the outer edge of the cups and its top edge sits on a pixel boundary,
        // so its sides and top are crisp; its ends reach into the cups.
        float cx = mid + a.Bar / 2f, r = cx - (leftCup + a.Arc / 2);
        float cy = MathF.Round(a.Top + 1 - r - a.Arc / 2) + a.Arc / 2 + r;
        using var arc = new SKPaint { IsAntialias = true, Color = SKColors.White, Style = SKPaintStyle.Stroke, StrokeWidth = a.Arc };
        using var path = new SKPath();
        path.AddArc(new SKRect(cx - r, cy - r, cx + r, cy + r), 180, 180);
        canvas.DrawPath(path, arc);

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
