using System.Xml.Linq;
using OVS.Tools;
using SkiaSharp;

namespace OVS.Tests.Client;

/// <summary>Package 28: the app logo as exe, window and taskbar icon. Package 95: every size shows the sound wave.</summary>
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
        Assert.Equal([16, 24, 32, 48, 64, 256], sizes);
    }

    [Fact]
    public void Csproj_UsesTheIcon()
    {
        var project = XDocument.Load(Path.Combine(ClientDir(), "OVS.Client.csproj"));
        var icon = project.Descendants("ApplicationIcon").Single().Value;
        Assert.True(File.Exists(Path.Combine(ClientDir(), icon)), icon);
    }

    /// <summary>Package 95: the middle wave bar (x 128, y 146 to 190 in the 256 grid) is white in every frame.</summary>
    [Fact]
    public void IcoFrames_ShowWave()
    {
        foreach (var (size, png) in IcoFrames())
        {
            using var bitmap = SKBitmap.Decode(png);
            Assert.Equal(size, bitmap.Width);
            var pixel = bitmap.GetPixel((int)(128f * size / 256), (int)(168f * size / 256));
            Assert.True(pixel.Red > 200 && pixel.Green > 200 && pixel.Blue > 200, $"{size} px: {pixel}");
        }
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
