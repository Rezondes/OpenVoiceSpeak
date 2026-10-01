using System.Xml.Linq;
using OVS.Tools;
using SkiaSharp;

namespace OVS.Tests.Client;

/// <summary>Package 28: the app logo as exe, window and taskbar icon. Package 95: every size shows the sound wave, the small ones pixel-hinted.</summary>
public class LogoTests
{
    static string RepoDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "OVS.Client"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir.FullName;
    }

    static string ClientDir() => Path.Combine(RepoDir(), "src", "OVS.Client");

    /// <summary>The PNG bytes of every ICO frame by size (all frames are PNG compressed).</summary>
    static Dictionary<int, byte[]> IcoFrames()
    {
        var bytes = File.ReadAllBytes(Path.Combine(ClientDir(), "Assets", "ovs.ico"));
        int count = BitConverter.ToUInt16(bytes, 4);
        return Enumerable.Range(0, count).ToDictionary(
            i => bytes[6 + i * 16] is 0 ? 256 : bytes[6 + i * 16],
            i => bytes.AsSpan(BitConverter.ToInt32(bytes, 6 + i * 16 + 12), BitConverter.ToInt32(bytes, 6 + i * 16 + 8)).ToArray());
    }

    [Fact]
    public void Ico_ContainsAllSizes()
    {
        // ICONDIR: reserved, type 1, count; then 16-byte entries whose first byte is the width (0 means 256).
        var bytes = File.ReadAllBytes(Path.Combine(ClientDir(), "Assets", "ovs.ico"));
        Assert.Equal(1, BitConverter.ToUInt16(bytes, 2));
        int count = BitConverter.ToUInt16(bytes, 4);
        var sizes = Enumerable.Range(0, count).Select(i => bytes[6 + i * 16] is 0 ? 256 : bytes[6 + i * 16]).Order().ToList();
        Assert.Equal([16, 20, 24, 32, 48, 64, 256], sizes);
    }

    [Fact]
    public void Csproj_UsesTheIcon()
    {
        var project = XDocument.Load(Path.Combine(ClientDir(), "OVS.Client.csproj"));
        var icon = project.Descendants("ApplicationIcon").Single().Value;
        Assert.True(File.Exists(Path.Combine(ClientDir(), icon)), icon);
    }

    /// <summary>Package 95: the middle wave bar (x 128, y 146 to 190 in the 256 grid) is white in the frames rendered from the SVG.</summary>
    [Fact]
    public void IcoFrames_ShowWave()
    {
        foreach (var (size, png) in IcoFrames().Where(f => f.Key >= 48))
        {
            using var bitmap = SKBitmap.Decode(png);
            Assert.Equal(size, bitmap.Width);
            var pixel = bitmap.GetPixel((int)(128f * size / 256), (int)(168f * size / 256));
            Assert.True(pixel.Red > 200 && pixel.Green > 200 && pixel.Blue > 200, $"{size} px: {pixel}");
        }
    }

    /// <summary>
    /// The small frames are pixel-hinted: the row through the wave crosses cup, three bars and cup as five runs of pure
    /// white separated by pure blue (no anti-aliasing), the bars 1 px wide at 16 and 20 px and 2 px at 24 and 32 px.
    /// </summary>
    [Theory]
    [InlineData(16, 1)]
    [InlineData(20, 1)]
    [InlineData(24, 2)]
    [InlineData(32, 2)]
    public void SmallFrames_ShowSeparateCrispBars(int size, int barWidth)
    {
        using var bitmap = SKBitmap.Decode(IcoFrames()[size]);
        int y = (int)(168f * size / 256);
        var row = Enumerable.Range(0, size).Select(x => bitmap.GetPixel(x, y)).ToList();
        var runs = new List<(int Start, int Width)>();
        for (int x = 0; x < size; x++)
        {
            if (row[x] != SKColors.White) continue;
            if (runs.Count > 0 && runs[^1].Start + runs[^1].Width == x) runs[^1] = (runs[^1].Start, runs[^1].Width + 1);
            else runs.Add((x, 1));
        }
        Assert.Equal(5, runs.Count);
        Assert.All(runs.Skip(1).Take(3), r => Assert.Equal(barWidth, r.Width));
        for (int x = runs[0].Start; x < runs[^1].Start + runs[^1].Width; x++)
            Assert.True(row[x] == SKColors.White || row[x] == new SKColor(0x2F, 0x6F, 0xEB), $"{size} px, x {x}: {row[x]}");
    }

    /// <summary>The checked-in small frames are exactly what the tool draws from its small artwork.</summary>
    [Fact]
    public void SmallFrames_MatchTool()
    {
        var svg = File.ReadAllText(Path.Combine(ClientDir(), "Assets", "logo.svg"));
        Assert.Equal([16, 20, 24, 32], LogoRenderer.Small.Keys.Order());
        foreach (var size in LogoRenderer.Small.Keys) Assert.Equal(LogoRenderer.RenderPng(svg, size), IcoFrames()[size]);
    }

    /// <summary>Package 95: the checked-in icon is what the tool renders from logo.svg, and the tool is deterministic.</summary>
    [Fact]
    public void IcoMatchesSvgRender()
    {
        var svg = File.ReadAllText(Path.Combine(ClientDir(), "Assets", "logo.svg"));
        var rendered = LogoRenderer.RenderPng(svg, 64);
        Assert.Equal(rendered, LogoRenderer.RenderPng(svg, 64));
        Assert.Equal(LogoRenderer.RenderIco(svg), LogoRenderer.RenderIco(svg));

        using var expected = SKBitmap.Decode(rendered);
        using var actual = SKBitmap.Decode(IcoFrames()[64]);
        for (int y = 0; y < 64; y++)
        for (int x = 0; x < 64; x++)
        {
            SKColor a = expected.GetPixel(x, y), b = actual.GetPixel(x, y);
            var diff = Math.Max(Math.Max(Math.Abs(a.Red - b.Red), Math.Abs(a.Green - b.Green)), Math.Max(Math.Abs(a.Blue - b.Blue), Math.Abs(a.Alpha - b.Alpha)));
            Assert.True(diff <= 8, $"({x},{y}): {a} vs {b}");
        }
    }

    [Fact]
    public void DocsLogo_Equals256Frame() =>
        Assert.Equal(IcoFrames()[256], File.ReadAllBytes(Path.Combine(RepoDir(), "docs", "logo.png")));
}
