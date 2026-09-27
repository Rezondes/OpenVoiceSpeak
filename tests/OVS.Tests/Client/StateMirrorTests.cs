using OVS.Client.Net;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;

namespace OVS.Tests.Client;

public class StateMirrorTests
{
    static readonly Guid Lobby = Guid.NewGuid(), Raid = Guid.NewGuid();

    static UserInfo User(uint id, Guid channel, string nick = "u") =>
        new(id, "fp" + id, nick, channel, false, false, false, Permission.Speak, []);

    static StateMirror Mirror() => new(new Welcome(1, "", new ServerSnapshot(
        new ServerSettingsInfo("S", "", false), Lobby,
        [new ChannelInfo(Lobby, "Lobby", "", 0)], [],
        [new GroupInfo(Guid.NewGuid(), "Gast", Permission.Speak)],
        [User(1, Lobby, "ich")])));

    [Fact]
    public void Snapshot_IsMirrored()
    {
        var m = Mirror();
        Assert.Equal("ich", m.Self!.Nickname);
        Assert.Equal(Lobby, m.DefaultChannelId);
        Assert.Single(m.Channels);
    }

    [Fact]
    public void Apply_EachDelta()
    {
        var m = Mirror();
        Assert.True(m.Apply(new ChannelAdded(new ChannelInfo(Raid, "Raid", "", 1))));
        Assert.True(m.Apply(new ChannelUpdated(new ChannelInfo(Raid, "Raid 2", "x", 2))));
        Assert.Equal("Raid 2", m.Channels[Raid].Name);

        Assert.True(m.Apply(new UserJoined(User(2, Lobby))));
        Assert.True(m.Apply(new UserUpdated(User(2, Raid))));
        Assert.Equal(Raid, m.Users[2].ChannelId);

        Assert.True(m.Apply(new ChannelsLinked(Raid, Lobby)));
        Assert.Equal([Raid], m.LinkedChannels(Lobby));
        Assert.True(m.Apply(new ChannelsUnlinked(Lobby, Raid)));
        Assert.Empty(m.LinkedChannels(Lobby));

        Assert.True(m.Apply(new GroupsChanged([])));
        Assert.Empty(m.Groups);
        Assert.True(m.Apply(new ServerSettingsChanged(new ServerSettingsInfo("Neu", "Hi", true))));
        Assert.Equal("Neu", m.Settings.Name);

        Assert.True(m.Apply(new UserLeft(2)));
        Assert.False(m.Users.ContainsKey(2));
        Assert.True(m.Apply(new ChannelRemoved(Raid)));
        Assert.False(m.Channels.ContainsKey(Raid));

        Assert.False(m.Apply(new Pong()));
    }
}
