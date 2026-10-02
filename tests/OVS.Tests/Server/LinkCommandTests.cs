using OVS.Shared.Identity;
using OVS.Shared.Protocol;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Server;

public sealed class LinkCommandTests : IAsyncLifetime
{
    readonly ClientIdentity adminId = ClientIdentity.Create();
    readonly ClientIdentity modId = ClientIdentity.Create();
    TestServer server = null!;
    TestClient a = null!, m = null!, g = null!;
    Guid lobby, x, y;

    public async Task InitializeAsync()
    {
        server = await TestServer.StartAsync(d =>
        {
            TestServer.Grant(adminId, "Admin")(d);
            TestServer.Grant(modId, "Moderator")(d);
        });
        a = await TestClient.ConnectAsync(server, "admin", adminId);
        m = await TestClient.ConnectAsync(server, "mod", modId);
        g = await TestClient.ConnectAsync(server, "gast");
        lobby = a.Welcome.Snapshot.DefaultChannelId;
        x = await CreateAsync("X");
        y = await CreateAsync("Y");
    }

    public async Task DisposeAsync()
    {
        foreach (var c in new[] { a, m, g }) await c.DisposeAsync();
        await server.DisposeAsync();
    }

    async Task<Guid> CreateAsync(string name)
    {
        await a.SendAsync(new CreateChannel(name, ""));
        return (await a.WaitForAsync<ChannelAdded>(c => c.Channel.Name == name)).Channel.Id;
    }

    static (Guid, Guid) Norm(Guid p, Guid q) => p.CompareTo(q) < 0 ? (p, q) : (q, p);

    /// <summary>
    /// Sends a request that surely fails and checks nothing unexpected arrives before its answer. The server answers
    /// in order, so the requests before it are done then, however slow the runner: the silence checks after it hold.
    /// </summary>
    static async Task ProbeAsync(TestClient c, Func<Message, bool> unexpected)
    {
        await c.SendAsync(new LinkChannels(Guid.Empty, Guid.Empty) { RequestId = "probe" });
        while (await c.NextAsync() is { } message and not Error { RequestId: "probe" })
            Assert.False(unexpected(message), $"unexpected {message}");
    }

    [Fact]
    public async Task Link_AsModerator_BroadcastsAndPersists()
    {
        await m.SendAsync(new LinkChannels(x, y));
        var linked = await g.WaitForAsync<ChannelsLinked>();
        Assert.Equal(Norm(x, y), (linked.A, linked.B));
        Assert.Equal(y, Assert.Single(server.State.LinkedChannels(x)));
    }

    /// <summary>Package 111: nothing is spoken in a separator, so it has no links, alone or in the link matrix.</summary>
    [Fact]
    public async Task LinkToSeparator_InvalidLink()
    {
        await a.SendAsync(new CreateChannel("", "", Kind: ChannelKind.Separator));
        var separator = (await a.WaitForAsync<ChannelAdded>(c => c.Channel.Kind == ChannelKind.Separator)).Channel.Id;
        await m.SendAsync(new LinkChannels(x, separator) { RequestId = "l" });
        Assert.Equal(Codes.InvalidLink, (await m.ErrorAsync("l")).Code);
        await m.SendAsync(new SetChannelLinks([new LinkInfo(separator, y)], []) { RequestId = "s" });
        Assert.Equal(Codes.InvalidLink, (await m.ErrorAsync("s")).Code);
        Assert.Empty(server.State.LinkedChannels(separator));
    }

    [Fact]
    public async Task Link_SameChannel_InvalidLink()
    {
        await m.SendAsync(new LinkChannels(x, x) { RequestId = "l" });
        Assert.Equal(Codes.InvalidLink, (await m.ErrorAsync("l")).Code);
    }

    [Fact]
    public async Task Link_UnknownChannel_NotFound()
    {
        await m.SendAsync(new LinkChannels(x, Guid.NewGuid()) { RequestId = "l" });
        Assert.Equal(Codes.NotFound, (await m.ErrorAsync("l")).Code);
    }

    [Fact]
    public async Task Link_ExistingReversed_NoOp()
    {
        await m.SendAsync(new LinkChannels(x, y));
        await g.WaitForAsync<ChannelsLinked>();
        await m.WaitForAsync<ChannelsLinked>(); // m's own copy of the real link, so the probe only sees what the no-op sends
        await m.SendAsync(new LinkChannels(y, x) { RequestId = "l" });
        await ProbeAsync(m, message => message is ChannelsLinked or Error);
        await g.AssertNoMessageAsync<ChannelsLinked>();
    }

    [Fact]
    public async Task Unlink_Broadcasts_MissingIsNoOp()
    {
        await m.SendAsync(new LinkChannels(x, y));
        await g.WaitForAsync<ChannelsLinked>();
        await m.SendAsync(new UnlinkChannels(y, x));
        Assert.Equal(Norm(x, y), await g.WaitForAsync<ChannelsUnlinked>() is var u ? (u.A, u.B) : default);
        await m.WaitForAsync<ChannelsUnlinked>(); // m's own copy of the real unlink, so the probe only sees what the no-op sends

        await m.SendAsync(new UnlinkChannels(y, x));
        await ProbeAsync(m, message => message is ChannelsUnlinked or Error);
        await g.AssertNoMessageAsync<ChannelsUnlinked>();
    }

    [Fact]
    public async Task DeleteChannel_UnlinksBeforeRemove()
    {
        await m.SendAsync(new LinkChannels(x, y));
        await m.SendAsync(new LinkChannels(x, lobby));
        await g.WaitForAsync<ChannelsLinked>();
        await g.WaitForAsync<ChannelsLinked>();

        await a.SendAsync(new DeleteChannel(x));
        var order = new List<Message>();
        while (order.LastOrDefault() is not ChannelRemoved) order.Add((await g.NextAsync())!);
        Assert.Equal(2, order.OfType<ChannelsUnlinked>().Count());
        Assert.Empty(server.State.LinkedChannels(y));
        Assert.Empty(server.State.LinkedChannels(lobby));
    }

    [Fact]
    public async Task LinkedChannels_OnlyDirectNeighbours()
    {
        // lobby - x - y
        await m.SendAsync(new LinkChannels(lobby, x));
        await m.SendAsync(new LinkChannels(x, y));
        await g.WaitForAsync<ChannelsLinked>();
        await g.WaitForAsync<ChannelsLinked>();

        Assert.Equal(x, Assert.Single(server.State.LinkedChannels(lobby)));
        Assert.Equal(new HashSet<Guid> { lobby, y }, server.State.LinkedChannels(x));
        Assert.Empty(server.State.LinkedChannels(Guid.NewGuid()));
    }

    [Fact]
    public async Task Snapshot_ContainsLinks_AfterRestart()
    {
        await m.SendAsync(new LinkChannels(x, y));
        await g.WaitForAsync<ChannelsLinked>();
        foreach (var c in new[] { a, m, g }) await c.DisposeAsync();

        server = await server.RestartAsync();
        a = await TestClient.ConnectAsync(server, "admin", adminId);
        m = await TestClient.ConnectAsync(server, "mod", modId);
        g = await TestClient.ConnectAsync(server, "gast", g.Identity); // Package 83: the stored name belongs to this identity
        var link = Assert.Single(g.Welcome.Snapshot.Links);
        Assert.Equal(Norm(x, y), (link.A, link.B));
    }

    [Fact]
    public async Task Link_AsGuest_PermissionDenied()
    {
        await g.SendAsync(new LinkChannels(x, y) { RequestId = "l" });
        Assert.Equal(Codes.PermissionDenied, (await g.ErrorAsync("l")).Code);
        await g.SendAsync(new UnlinkChannels(x, y) { RequestId = "u" });
        Assert.Equal(Codes.PermissionDenied, (await g.ErrorAsync("u")).Code);
    }

    // ---- Package 38: link matrix ----

    [Fact]
    public async Task SetLinks_AddAndRemove_OneSaveAllBroadcasts()
    {
        await a.SendAsync(new LinkChannels(lobby, x));
        await g.WaitForAsync<ChannelsLinked>();

        await m.SendAsync(new SetChannelLinks([new LinkInfo(x, y), new LinkInfo(y, lobby), new LinkInfo(lobby, x)], [new LinkInfo(x, lobby)]) { RequestId = "beides" });
        Assert.Equal(Codes.InvalidValue, (await m.ErrorAsync("beides")).Code); // the same link set and removed

        await m.SendAsync(new SetChannelLinks([new LinkInfo(x, y), new LinkInfo(y, lobby)], [new LinkInfo(x, lobby)]));
        var linked = new[] { await g.WaitForAsync<ChannelsLinked>(), await g.WaitForAsync<ChannelsLinked>() };
        Assert.Equal(new[] { Norm(x, y), Norm(y, lobby) }.Order(), linked.Select(l => Norm(l.A, l.B)).Order());
        var unlinked = await g.WaitForAsync<ChannelsUnlinked>();
        Assert.Equal(Norm(lobby, x), Norm(unlinked.A, unlinked.B));
        Assert.Contains(server.Log, l => l.Contains("Links geändert von mod: 2 gesetzt, 1 entfernt"));

        await using var late = await TestClient.ConnectAsync(server, "spaet");
        Assert.Equal(2, late.Welcome.Snapshot.Links.Count);
    }

    [Fact]
    public async Task SetLinks_UnknownOrSelf_NothingChanged()
    {
        await a.SendAsync(new SetChannelLinks([new LinkInfo(x, y), new LinkInfo(x, Guid.NewGuid())], []) { RequestId = "fremd" });
        Assert.Equal(Codes.NotFound, (await a.ErrorAsync("fremd")).Code);
        await a.SendAsync(new SetChannelLinks([new LinkInfo(x, y), new LinkInfo(y, y)], []) { RequestId = "selbst" });
        Assert.Equal(Codes.InvalidLink, (await a.ErrorAsync("selbst")).Code);
        await g.AssertNoMessageAsync<ChannelsLinked>();
    }

    [Fact]
    public async Task SetLinks_WithoutRight_Denied()
    {
        await g.SendAsync(new SetChannelLinks([new LinkInfo(x, y)], []) { RequestId = "r" });
        Assert.Equal(Codes.PermissionDenied, (await g.ErrorAsync("r")).Code);
    }
}
