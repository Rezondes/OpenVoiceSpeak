using OVS.Client.Localization;
using OVS.Client.Settings;
using OVS.Client.ViewModels;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.Channels;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.ViewModels;

/// <summary>Package 163: the quick switcher's hits and what Enter does with them.</summary>
public sealed class QuickSwitcherViewModelTests
{
    static readonly Guid Lobby = Guid.NewGuid(), Evening = Guid.NewGuid(), Raid = Guid.NewGuid(), Line = Guid.NewGuid();
    static readonly Bookmark[] Servers = [new("Abend-Server", "abend.example.org", 9987, "ich"), new("Gilde", "gilde.example.org", 9987, "ich")];

    readonly List<Request> sent = [];

    ServerViewModel Server()
    {
        var snapshot = Snapshots.Snapshot("Gilde", "", Lobby,
            [new ChannelInfo(Lobby, "Lobby", "", 0), new ChannelInfo(Evening, "Abendrunde", "", 1), new ChannelInfo(Line, "Abend ---", "", 2, Kind: ChannelKind.Separator),
             new ChannelInfo(Raid, "Raid am Abend", "", 3)],
            [FakeServers.AdminGroup], [Snapshots.User(1, "ich", Lobby, Permission.All, [WellKnownGroups.Admin])]);
        return new ServerViewModel(Snapshots.Mirror(snapshot), request =>
        {
            sent.Add(request);
            return Task.CompletedTask;
        }, TimeProvider.System);
    }

    [Fact]
    public void Filter_StartOfWordFirst_NoSeparators()
    {
        var switcher = new QuickSwitcherViewModel(Server().Tree.Channels, Servers, _ => Task.CompletedTask);
        Assert.Equal(["Lobby", "Abendrunde", "Raid am Abend", "Abend-Server", "Gilde"], switcher.Hits.Select(hit => hit.Title)); // all, no separator
        Assert.Equal(TextFormat.Of(Strings.Quick_Users, 1), switcher.Hits[0].Detail); // the own user in the lobby
        switcher.Query = "abend";
        // the start of the name first, a channel before a server, then a word's start
        Assert.Equal(["Abendrunde", "Abend-Server", "Raid am Abend"], switcher.Hits.Select(hit => hit.Title));
        Assert.Equal(0, switcher.SelectedIndex);
        switcher.Move(1);
        switcher.Move(5);
        Assert.Equal("Raid am Abend", switcher.Selected?.Title); // Down stays on the last hit
        switcher.Move(-9);
        Assert.Equal("Abendrunde", switcher.Selected?.Title);
    }

    [Fact]
    public async Task Enter_Channel_JoinsLikeDoubleClick()
    {
        var switcher = new QuickSwitcherViewModel(Server().Tree.Channels, Servers, _ => Task.CompletedTask);
        switcher.Query = "raid";
        await switcher.ChooseAsync(switcher.Selected!);
        Assert.Equal(Raid, Assert.IsType<JoinChannel>(Assert.Single(sent)).ChannelId);
    }

    [Fact]
    public async Task Enter_Server_ConnectsLikeBookmark()
    {
        var connected = new List<Bookmark>();
        var switcher = new QuickSwitcherViewModel(Server().Tree.Channels, Servers, bookmark =>
        {
            connected.Add(bookmark);
            return Task.CompletedTask;
        });
        switcher.Query = "gilde";
        await switcher.ChooseAsync(switcher.Selected!);
        Assert.Equal(Servers[1], Assert.Single(connected));
        Assert.Empty(sent);
    }

    [Fact]
    public void Hits_NoConnection_OnlyServers()
    {
        var switcher = new QuickSwitcherViewModel([], Servers, _ => Task.CompletedTask);
        Assert.Equal(["Abend-Server", "Gilde"], switcher.Hits.Select(hit => hit.Title));
        switcher.Query = "lobby";
        Assert.True(switcher.HasNoHits);
        Assert.Null(switcher.Selected);
    }
}
