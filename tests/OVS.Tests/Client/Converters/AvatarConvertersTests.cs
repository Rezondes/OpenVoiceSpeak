using Avalonia.Media;
using OVS.Client.Models;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol.Messages.Groups;

namespace OVS.Tests.Client.Converters;

/// <summary>Package 167: a name's colour from the highest coloured group, readable on the theme's surface.</summary>
public sealed class AvatarConvertersTests
{
    static readonly Guid Admin = Guid.NewGuid(), Mod = Guid.NewGuid(), Guest = Guid.NewGuid();
    static readonly GroupColor Red = new(0xE7, 0x4C, 0x3C), Blue = new(0x34, 0x98, 0xDB);

    static readonly GroupInfo[] Groups =
    [
        new(Admin, "Admin", Permission.All, Color: Red),
        new(Mod, "Moderator", Permission.Speak), // no colour
        new(Guest, "Gast", Permission.Speak, Color: Blue),
    ];

    [Fact]
    public void NameColor_HighestColoredGroup_ContrastLifted()
    {
        Assert.Equal(Red, NameColors.For([Guest, Admin], Groups)); // the group order decides, not the user's list
        Assert.Equal(Blue, NameColors.For([Mod, Guest], Groups)); // a group without a colour is passed over
        Assert.Null(NameColors.For([Mod], Groups));
        Assert.Null(NameColors.For([], Groups));

        var dark = Color.Parse("#1E2128");
        var light = Colors.White;
        var navy = Color.Parse("#000080");
        var lifted = NameColors.Readable(navy, dark); // too dark on the dark surface: lifted towards white
        Assert.True(NameColors.Contrast(lifted, dark) >= NameColors.MinContrast, $"{lifted}");
        Assert.True(lifted.R > navy.R && lifted.G > navy.G);
        var yellow = Color.Parse("#FFFF66");
        var darkened = NameColors.Readable(yellow, light); // too light on the light surface: darkened
        Assert.True(NameColors.Contrast(darkened, light) >= NameColors.MinContrast, $"{darkened}");
        Assert.True(darkened.R < yellow.R);
        Assert.Equal(yellow, NameColors.Readable(yellow, dark)); // readable already: unchanged
    }
}
