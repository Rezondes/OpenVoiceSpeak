using OVS.Shared.Identity;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.Channels;
using OVS.Shared.Protocol.Messages.Connection;
using OVS.Shared.Protocol.Messages.Links;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Server.State;

public sealed class LinkCommandTests : IAsyncLifetime
{
    readonly ClientIdentity adminId = ClientIdentity.Create();
    readonly ClientIdentity modId = ClientIdentity.Create();
    TestServer server = null!;
    TestClient admin = null!, moderator = null!, guest = null!;
    Guid lobby, channelX, channelY;

    public async Task InitializeAsync()
    {
        server = await TestServer.StartAsync(serverData =>
        {
            TestServer.Grant(adminId, "Admin")(serverData);
            TestServer.Grant(modId, "Moderator")(serverData);
        });
        admin = await TestClient.ConnectAsync(server, "admin", adminId);
        moderator = await TestClient.ConnectAsync(server, "mod", modId);
        guest = await TestClient.ConnectAsync(server, "gast");
        lobby = admin.Welcome.Snapshot.DefaultChannelId;
        channelX = await CreateAsync("X");
        channelY = await CreateAsync("Y");
    }

    public async Task DisposeAsync()
    {
        foreach (var client in new[] { admin, moderator, guest }) await client.DisposeAsync();
        await server.DisposeAsync();
    }

    async Task<Guid> CreateAsync(string name)
    {
        await admin.SendAsync(new CreateChannel(name, ""));
        return (await admin.WaitForAsync<ChannelAdded>(channelAdded => channelAdded.Channel.Name == name)).Channel.Id;
    }

    static (Guid, Guid) Norm(Guid id, Guid otherId) => id.CompareTo(otherId) < 0 ? (id, otherId) : (otherId, id);

    /// <summary>
    /// Sends a request that surely fails and checks nothing unexpected arrives before its answer. The server answers
    /// in order, so the requests before it are done then, however slow the runner: the silence checks after it hold.
    /// </summary>
    static async Task ProbeAsync(TestClient client, Func<Message, bool> unexpected)
    {
        await client.SendAsync(new LinkChannels(Guid.Empty, Guid.Empty) { RequestId = "probe" });
        while (await client.NextAsync() is { } message and not Error { RequestId: "probe" })
            Assert.False(unexpected(message), $"unexpected {message}");
    }

    [Fact]
    public async Task Link_AsModerator_BroadcastsAndPersists()
    {
        await moderator.SendAsync(new LinkChannels(channelX, channelY));
        var linked = await guest.WaitForAsync<ChannelsLinked>();
        Assert.Equal(Norm(channelX, channelY), (linked.A, linked.B));
        Assert.Equal(channelY, Assert.Single(server.State.LinkedChannels(channelX)));
    }

    /// <summary>Package 111: nothing is spoken in a separator, so it has no links, alone or in the link matrix.</summary>
    [Fact]
    public async Task Link_ToSeparator_InvalidLink()
    {
        await admin.SendAsync(new CreateChannel("", "", Kind: ChannelKind.Separator));
        var separator = (await admin.WaitForAsync<ChannelAdded>(channelAdded => channelAdded.Channel.Kind == ChannelKind.Separator)).Channel.Id;
        await moderator.SendAsync(new LinkChannels(channelX, separator) { RequestId = "l" });
        Assert.Equal(ReasonCodes.InvalidLink, (await moderator.ErrorAsync("l")).Code);
        await moderator.SendAsync(new SetChannelLinks([new LinkInfo(separator, channelY)], []) { RequestId = "s" });
        Assert.Equal(ReasonCodes.InvalidLink, (await moderator.ErrorAsync("s")).Code);
        Assert.Empty(server.State.LinkedChannels(separator));
    }

    [Fact]
    public async Task Link_SameChannel_InvalidLink()
    {
        await moderator.SendAsync(new LinkChannels(channelX, channelX) { RequestId = "l" });
        Assert.Equal(ReasonCodes.InvalidLink, (await moderator.ErrorAsync("l")).Code);
    }

    [Fact]
    public async Task Link_UnknownChannel_NotFound()
    {
        await moderator.SendAsync(new LinkChannels(channelX, Guid.NewGuid()) { RequestId = "l" });
        Assert.Equal(ReasonCodes.NotFound, (await moderator.ErrorAsync("l")).Code);
    }

    [Fact]
    public async Task Link_ExistingReversed_NoOp()
    {
        await moderator.SendAsync(new LinkChannels(channelX, channelY));
        await guest.WaitForAsync<ChannelsLinked>();
        await moderator.WaitForAsync<ChannelsLinked>(); // m's own copy of the real link, so the probe only sees what the no-op sends
        await moderator.SendAsync(new LinkChannels(channelY, channelX) { RequestId = "l" });
        await ProbeAsync(moderator, message => message is ChannelsLinked or Error);
        await guest.AssertNoMessageAsync<ChannelsLinked>();
    }

    [Fact]
    public async Task Unlink_Broadcasts_MissingIsNoOp()
    {
        await moderator.SendAsync(new LinkChannels(channelX, channelY));
        await guest.WaitForAsync<ChannelsLinked>();
        await moderator.SendAsync(new UnlinkChannels(channelY, channelX));
        Assert.Equal(Norm(channelX, channelY), await guest.WaitForAsync<ChannelsUnlinked>() is var channelsUnlinked ? (channelsUnlinked.A, channelsUnlinked.B) : default);
        await moderator.WaitForAsync<ChannelsUnlinked>(); // m's own copy of the real unlink, so the probe only sees what the no-op sends

        await moderator.SendAsync(new UnlinkChannels(channelY, channelX));
        await ProbeAsync(moderator, message => message is ChannelsUnlinked or Error);
        await guest.AssertNoMessageAsync<ChannelsUnlinked>();
    }

    [Fact]
    public async Task DeleteChannel_Linked_UnlinkedBeforeRemove()
    {
        await moderator.SendAsync(new LinkChannels(channelX, channelY));
        await moderator.SendAsync(new LinkChannels(channelX, lobby));
        await guest.WaitForAsync<ChannelsLinked>();
        await guest.WaitForAsync<ChannelsLinked>();

        await admin.SendAsync(new DeleteChannel(channelX));
        var order = new List<Message>();
        while (order.LastOrDefault() is not ChannelRemoved) order.Add((await guest.NextAsync())!);
        Assert.Equal(2, order.OfType<ChannelsUnlinked>().Count());
        Assert.Empty(server.State.LinkedChannels(channelY));
        Assert.Empty(server.State.LinkedChannels(lobby));
    }

    [Fact]
    public async Task LinkedChannels_Chain_OnlyDirectNeighbours()
    {
        // lobby - x - y
        await moderator.SendAsync(new LinkChannels(lobby, channelX));
        await moderator.SendAsync(new LinkChannels(channelX, channelY));
        await guest.WaitForAsync<ChannelsLinked>();
        await guest.WaitForAsync<ChannelsLinked>();

        Assert.Equal(channelX, Assert.Single(server.State.LinkedChannels(lobby)));
        Assert.Equal(new HashSet<Guid> { lobby, channelY }, server.State.LinkedChannels(channelX));
        Assert.Empty(server.State.LinkedChannels(Guid.NewGuid()));
    }

    [Fact]
    public async Task Snapshot_ContainsLinks_AfterRestart()
    {
        await moderator.SendAsync(new LinkChannels(channelX, channelY));
        await guest.WaitForAsync<ChannelsLinked>();
        foreach (var client in new[] { admin, moderator, guest }) await client.DisposeAsync();

        server = await server.RestartAsync();
        admin = await TestClient.ConnectAsync(server, "admin", adminId);
        moderator = await TestClient.ConnectAsync(server, "mod", modId);
        guest = await TestClient.ConnectAsync(server, "gast", guest.Identity); // Package 83: the stored name belongs to this identity
        var link = Assert.Single(guest.Welcome.Snapshot.Links);
        Assert.Equal(Norm(channelX, channelY), (link.A, link.B));
    }

    [Fact]
    public async Task Link_AsGuest_PermissionDenied()
    {
        await guest.SendAsync(new LinkChannels(channelX, channelY) { RequestId = "l" });
        Assert.Equal(ReasonCodes.PermissionDenied, (await guest.ErrorAsync("l")).Code);
        await guest.SendAsync(new UnlinkChannels(channelX, channelY) { RequestId = "u" });
        Assert.Equal(ReasonCodes.PermissionDenied, (await guest.ErrorAsync("u")).Code);
    }

    // ---- Package 38: link matrix ----

    [Fact]
    public async Task SetLinks_AddAndRemove_OneSaveAllBroadcasts()
    {
        await admin.SendAsync(new LinkChannels(lobby, channelX));
        await guest.WaitForAsync<ChannelsLinked>();

        await moderator.SendAsync(new SetChannelLinks([new LinkInfo(channelX, channelY), new LinkInfo(channelY, lobby), new LinkInfo(lobby, channelX)], [new LinkInfo(channelX, lobby)]) { RequestId = "beides" });
        Assert.Equal(ReasonCodes.InvalidValue, (await moderator.ErrorAsync("beides")).Code); // the same link set and removed

        await moderator.SendAsync(new SetChannelLinks([new LinkInfo(channelX, channelY), new LinkInfo(channelY, lobby)], [new LinkInfo(channelX, lobby)]));
        var linked = new[] { await guest.WaitForAsync<ChannelsLinked>(), await guest.WaitForAsync<ChannelsLinked>() };
        Assert.Equal(new[] { Norm(channelX, channelY), Norm(channelY, lobby) }.Order(), linked.Select(channelsLinked => Norm(channelsLinked.A, channelsLinked.B)).Order());
        var unlinked = await guest.WaitForAsync<ChannelsUnlinked>();
        Assert.Equal(Norm(lobby, channelX), Norm(unlinked.A, unlinked.B));
        Assert.Contains(server.Log, line => line.Contains("Links geändert von mod: 2 gesetzt, 1 entfernt"));

        await using var late = await TestClient.ConnectAsync(server, "spaet");
        Assert.Equal(2, late.Welcome.Snapshot.Links.Count);
    }

    [Fact]
    public async Task SetLinks_UnknownOrSelf_NothingChanged()
    {
        await admin.SendAsync(new SetChannelLinks([new LinkInfo(channelX, channelY), new LinkInfo(channelX, Guid.NewGuid())], []) { RequestId = "fremd" });
        Assert.Equal(ReasonCodes.NotFound, (await admin.ErrorAsync("fremd")).Code);
        await admin.SendAsync(new SetChannelLinks([new LinkInfo(channelX, channelY), new LinkInfo(channelY, channelY)], []) { RequestId = "selbst" });
        Assert.Equal(ReasonCodes.InvalidLink, (await admin.ErrorAsync("selbst")).Code);
        await guest.AssertNoMessageAsync<ChannelsLinked>();
    }

    [Fact]
    public async Task SetLinks_WithoutRight_Denied()
    {
        await guest.SendAsync(new SetChannelLinks([new LinkInfo(channelX, channelY)], []) { RequestId = "r" });
        Assert.Equal(ReasonCodes.PermissionDenied, (await guest.ErrorAsync("r")).Code);
    }
}
