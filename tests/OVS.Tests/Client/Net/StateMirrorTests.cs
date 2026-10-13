using OVS.Client.Net;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol.Messages.Channels;
using OVS.Shared.Protocol.Messages.Connection;
using OVS.Shared.Protocol.Messages.Groups;
using OVS.Shared.Protocol.Messages.Links;
using OVS.Shared.Protocol.Messages.ServerInfo;
using OVS.Shared.Protocol.Messages.Users;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.Net;

public class StateMirrorTests
{
    static readonly Guid Lobby = Guid.NewGuid(), Raid = Guid.NewGuid();

    static UserInfo User(uint id, Guid channel, string nick = "u") =>
        new(id, "fp" + id, nick, channel, false, false, false, Permission.Speak, []);

    static StateMirror Mirror() => Snapshots.Mirror(Snapshots.Snapshot(
        "S", "", Lobby,
        [new ChannelInfo(Lobby, "Lobby", "", 0)],
        [new GroupInfo(Guid.NewGuid(), "Gast", Permission.Speak)],
        [User(1, Lobby, "ich")]));

    [Fact]
    public void Snapshot_IsMirrored()
    {
        var stateMirror = Mirror();
        Assert.Equal("ich", stateMirror.Self!.Nickname);
        Assert.Equal(Lobby, stateMirror.DefaultChannelId);
        Assert.Single(stateMirror.Channels);
    }

    [Fact]
    public void Apply_EachDelta()
    {
        var stateMirror = Mirror();
        Assert.True(stateMirror.Apply(new ChannelAdded(new ChannelInfo(Raid, "Raid", "", 1))));
        Assert.True(stateMirror.Apply(new ChannelUpdated(new ChannelInfo(Raid, "Raid 2", "x", 2))));
        Assert.Equal("Raid 2", stateMirror.Channels[Raid].Name);

        Assert.True(stateMirror.Apply(new UserJoined(User(2, Lobby))));
        Assert.True(stateMirror.Apply(new UserUpdated(User(2, Raid))));
        Assert.Equal(Raid, stateMirror.Users[2].ChannelId);

        Assert.True(stateMirror.Apply(new ChannelsLinked(Raid, Lobby)));
        Assert.Equal([Raid], stateMirror.LinkedChannels(Lobby));
        Assert.True(stateMirror.Apply(new ChannelsUnlinked(Lobby, Raid)));
        Assert.Empty(stateMirror.LinkedChannels(Lobby));

        Assert.True(stateMirror.Apply(new GroupsChanged([])));
        Assert.Empty(stateMirror.Groups);
        Assert.True(stateMirror.Apply(new ServerSettingsChanged(new ServerSettingsInfo("Neu", "Hi", true))));
        Assert.Equal("Neu", stateMirror.Settings.Name);

        Assert.True(stateMirror.Apply(new UserLeft(2)));
        Assert.False(stateMirror.Users.ContainsKey(2));
        Assert.True(stateMirror.Apply(new ChannelRemoved(Raid)));
        Assert.False(stateMirror.Channels.ContainsKey(Raid));

        Assert.False(stateMirror.Apply(new Pong()));
    }
}
