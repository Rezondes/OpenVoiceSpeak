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

    [Fact]
    public async Task Link_AsModerator_BroadcastsAndPersists()
    {
        await m.SendAsync(new LinkChannels(x, y));
        var linked = await g.WaitForAsync<ChannelsLinked>();
        Assert.Equal(Norm(x, y), (linked.A, linked.B));
        Assert.Equal(y, Assert.Single(server.State.LinkedChannels(x)));
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
        await m.SendAsync(new LinkChannels(y, x) { RequestId = "l" });
        await g.AssertNoMessageAsync<ChannelsLinked>();
        await m.AssertNoMessageAsync<Error>();
    }

    [Fact]
    public async Task Unlink_Broadcasts_MissingIsNoOp()
    {
        await m.SendAsync(new LinkChannels(x, y));
        await g.WaitForAsync<ChannelsLinked>();
        await m.SendAsync(new UnlinkChannels(y, x));
        Assert.Equal(Norm(x, y), await g.WaitForAsync<ChannelsUnlinked>() is var u ? (u.A, u.B) : default);

        await m.SendAsync(new UnlinkChannels(y, x));
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
        g = await TestClient.ConnectAsync(server, "gast");
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
}
