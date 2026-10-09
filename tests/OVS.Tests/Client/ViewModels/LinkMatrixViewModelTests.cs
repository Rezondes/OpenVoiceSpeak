using OVS.Client.ViewModels;
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.Links;
using OVS.Tests.TestSupport;
using static OVS.Tests.TestSupport.HangGuard;

namespace OVS.Tests.Client.ViewModels;

/// <summary>The links tab on its own: when it holds changes, how its place saves and throws them away.</summary>
public sealed class LinkMatrixViewModelTests
{
    readonly List<Request> sent = [];
    readonly ServerViewModel server;
    readonly LinkMatrixViewModel links;

    public LinkMatrixViewModelTests()
    {
        server = FakeServers.Admin(sent, new ManualTimeProvider());
        links = new LinkMatrixViewModel(server);
        server.StateChanged += links.Rebuild; // the page tells the tab about every state change
    }

    [Fact]
    public async Task Place_NothingMarked_NoChangesAndSavingSucceedsWithoutSending()
    {
        Assert.False(links.Place.HasChanges());
        Assert.True(await Bound(links.Place.SaveAsync()));
        Assert.Empty(sent);
    }

    [Fact]
    public async Task Place_LinkMarked_HasChangesAndSavingSendsItAndWaitsForTheServer()
    {
        links.UnlinkAll();
        Assert.True(links.Place.HasChanges());

        var saving = links.Place.SaveAsync();

        Assert.Single(Assert.IsType<SetChannelLinks>(Assert.Single(sent)).Remove);
        Assert.False(saving.IsCompleted);
        server.Apply(new ChannelsUnlinked(FakeServers.Lobby, FakeServers.Raid));
        Assert.True(await Bound(saving));
        Assert.False(links.Place.HasChanges());
    }

    [Fact]
    public async Task Place_SaveOnItsWay_SavingWaitsForItAndSendsNoSecondOne()
    {
        links.UnlinkAll();
        links.ApplyCommand.Execute(null);

        var saving = links.Place.SaveAsync();

        Assert.False(saving.IsCompleted);
        server.Apply(new ChannelsUnlinked(FakeServers.Lobby, FakeServers.Raid));
        Assert.True(await Bound(saving));
        Assert.Single(sent.OfType<SetChannelLinks>());
    }

    [Fact]
    public void PlaceDiscard_LinkMarked_LinkBackAndNoChanges()
    {
        links.UnlinkAll();

        links.Place.Discard();

        Assert.False(links.Place.HasChanges());
        Assert.True(links.IsLinked(FakeServers.Lobby, FakeServers.Raid));
    }
}
