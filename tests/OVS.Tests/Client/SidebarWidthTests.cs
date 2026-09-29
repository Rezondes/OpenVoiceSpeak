using OVS.Client.Views;

namespace OVS.Tests.Client;

/// <summary>Package 67: the sidebar is as wide as its widest channel row, at least 240 px.</summary>
public class SidebarWidthTests
{
    [Fact]
    public void Fits_LongestName_PlusIcons()
    {
        var rows = new[] { new SidebarRow(80, 2, 8), new SidebarRow(150, 1, 8), new SidebarRow(60, 0, 8) };
        Assert.Equal(150 + SidebarWidth.Fixed + SidebarWidth.PerIcon + 8, SidebarWidth.For(rows));
        Assert.Equal(SidebarWidth.Minimum, SidebarWidth.For([new SidebarRow(40, 2, 8), new SidebarRow(20, 0, 8)]));
        Assert.Equal(240, SidebarWidth.Minimum);
        Assert.Equal(SidebarWidth.Minimum, SidebarWidth.For([]));
    }

    [Fact]
    public void RoundsUp_ToWholePixels()
    {
        var width = SidebarWidth.For([new SidebarRow(250.3, 0, 7.2)]);
        Assert.Equal(Math.Ceiling(250.3 + SidebarWidth.Fixed + 7.2), width);
    }
}
