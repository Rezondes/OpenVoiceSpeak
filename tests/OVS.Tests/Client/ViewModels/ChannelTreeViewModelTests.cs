using OVS.Client.Collections;
using OVS.Client.ViewModels;
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.Channels;
using OVS.Shared.Protocol.Messages.Users;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.ViewModels;

/// <summary>What the move of the channel tree out of the server view model adds (Step 27e): the row lookups and the order's guards.</summary>
public sealed class ChannelTreeViewModelTests
{
    readonly List<Request> sent = [];
    readonly ServerViewModel server;

    public ChannelTreeViewModelTests()
    {
        server = FakeServers.Admin(sent, new ManualTimeProvider());
        server.Post = action => action();
    }

    [Fact]
    public void FindUser_UnknownSession_IsNull() => Assert.Null(server.Tree.FindUser(999));

    [Fact]
    public void FindChannel_UnknownId_IsNull() => Assert.Null(server.Tree.FindChannel(Guid.NewGuid()));

    [Fact]
    public void FindUser_FoldingAway_IsTheLeavingRow()
    {
        server.Leave.Delay = TimeSpan.FromMinutes(1);
        var anna = server.Tree.FindUser(2);

        server.Apply(new UserLeft(2));

        Assert.NotNull(anna);
        Assert.Same(anna, server.Tree.FindUser(2));
        Assert.Contains(server.Tree.Channels, channel => CollectionSync.IsLeaving(channel.Users, anna));
    }

    [Fact]
    public async Task MoveChannelAsync_SameSourceAndTarget_SendsNothing()
    {
        var lobby = server.Tree.Channels.Single(channel => channel.Name == "Lobby");

        await HangGuard.BoundAsync(server.Tree.MoveChannelAsync(lobby, lobby, after: true));

        Assert.Empty(sent.OfType<ReorderChannels>());
    }

    [Fact]
    public async Task MoveChannelAsync_SameOrder_SendsNothing()
    {
        var first = server.Tree.Channels[0];
        var second = server.Tree.Channels[1];

        await HangGuard.BoundAsync(server.Tree.MoveChannelAsync(first, second, after: false));

        Assert.Empty(sent.OfType<ReorderChannels>());
    }
}
