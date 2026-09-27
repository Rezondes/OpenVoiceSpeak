using System.Xml.Linq;

namespace OVS.Tests.Client;

/// <summary>Package 28: the app logo as exe, window and taskbar icon.</summary>
public class LogoTests
{
    static string ClientDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "OVS.Client"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir.FullName, "src", "OVS.Client");
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
}
