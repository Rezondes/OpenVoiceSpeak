using OVS.Client.ViewModels;
using OVS.Shared.Protocol;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.Connection;
using OVS.Shared.Protocol.Messages.Moderation;
using OVS.Shared.Protocol.Messages.Users;
using OVS.Tests.TestSupport;
using static OVS.Tests.TestSupport.HangGuard;

namespace OVS.Tests.Client.ViewModels;

/// <summary>The users and bans tabs on their own: what they ask the server for, whom they listen to, and when they stop.</summary>
public sealed class UsersAndBansViewModelTests
{
    static readonly KnownUserInfo Yvonne = new("fpY", "Yvonne", [WellKnownGroups.Guest], CanBeModeratedByMe: true);
    static readonly BanInfo YvonnesBan = new(Guid.NewGuid(), "fpY", "Yvonne", null, "Spam", "ich", null);

    readonly ManualTimeProvider time = new();
    readonly List<Request> sent = [];

    sealed record Rig(UsersAndBansViewModel Section, ServerViewModel Server);

    Rig Create()
    {
        var server = FakeServers.Admin(sent, time);
        return new Rig(new UsersAndBansViewModel(server), server);
    }

    string IdOf<T>() where T : Request => sent.OfType<T>().Last().RequestId!;

    [Fact]
    public async Task RefreshAsync_BothTabsAllowed_AsksForTheUserListAndTheBanList()
    {
        var (section, _) = Create();

        await Bound(section.RefreshAsync());

        Assert.Single(sent.OfType<ListUsers>());
        Assert.Single(sent.OfType<ListBans>());
    }

    [Fact]
    public async Task Apply_UserListOfItsRequest_ShowsTheCardAndLeavesTheBansLoading()
    {
        var (section, server) = Create();
        await Bound(section.RefreshAsync());

        server.Apply(new UserList(IdOf<ListUsers>(), [Yvonne]));

        Assert.Equal("Yvonne", Assert.Single(section.Users).Nickname);
        Assert.True(section.UsersLoad.HasSucceeded);
        Assert.False(section.BansLoad.HasSucceeded);
    }

    [Fact]
    public async Task Apply_BanListOfItsRequest_ShowsTheBanAndLeavesTheUsersLoading()
    {
        var (section, server) = Create();
        await Bound(section.RefreshAsync());

        server.Apply(new BanList(IdOf<ListBans>(), [YvonnesBan]));

        Assert.Equal("Yvonne", Assert.Single(section.Bans).Nickname);
        Assert.True(section.BansLoad.HasSucceeded);
        Assert.False(section.UsersLoad.HasSucceeded);
    }

    [Fact]
    public async Task Apply_BanListAfterAnUnban_EndsTheBansBusyMark()
    {
        var (section, server) = Create();
        await Bound(section.RefreshAsync());
        server.Apply(new BanList(IdOf<ListBans>(), [YvonnesBan]));
        var row = Assert.Single(section.Bans);

        await Bound(row.UnbanCommand.ExecuteAsync(null));
        Assert.True(row.Busy.IsRunning);
        server.Apply(new BanList(IdOf<Unban>(), []));

        Assert.False(row.Busy.IsRunning);
    }

    [Fact]
    public async Task Apply_ErrorForTheUserListRequest_ReleasesTheNextRefresh()
    {
        var (section, server) = Create();
        await Bound(section.RefreshAsync());

        server.Apply(new Error(IdOf<ListUsers>(), ReasonCodes.PermissionDenied));
        time.Advance(TimeSpan.FromSeconds(2));
        await Bound(section.RefreshAsync());

        Assert.Equal(2, sent.OfType<ListUsers>().Count());
    }

    [Fact]
    public async Task FollowServer_SomeoneGoesOffline_AsksForTheUserListAgain()
    {
        var (section, server) = Create();
        await Bound(section.RefreshAsync());
        server.Apply(new UserList(IdOf<ListUsers>(), [Yvonne]));
        server.Apply(new UserLeft(2)); // anna, online until now
        time.Advance(TimeSpan.FromSeconds(2));

        section.FollowServer();

        Assert.Equal(2, sent.OfType<ListUsers>().Count());
    }

    [Fact]
    public async Task FollowServer_NothingChanged_AsksForNothing()
    {
        var (section, server) = Create();
        await Bound(section.RefreshAsync());
        server.Apply(new UserList(IdOf<ListUsers>(), [Yvonne]));
        time.Advance(TimeSpan.FromSeconds(2));

        section.FollowServer();

        Assert.Single(sent.OfType<ListUsers>());
    }

    [Fact]
    public async Task Detach_ListsAnswered_AsksForNothingMore()
    {
        var (section, server) = Create();
        await Bound(section.RefreshAsync());
        server.Apply(new UserList(IdOf<ListUsers>(), [Yvonne]));
        server.Apply(new BanList(IdOf<ListBans>(), []));
        var askedBefore = sent.Count;

        section.Detach();
        server.Apply(new UserLeft(2)); // would ask for the user list again
        time.Advance(TimeSpan.FromSeconds(2));
        await Bound(section.RefreshAsync());
        section.FollowServer();

        Assert.Equal(askedBefore, sent.Count);
    }

    [Fact]
    public async Task Detach_WithListsInFlight_IgnoresTheirAnswersAndKeepsNoRoute()
    {
        var (section, server) = Create();
        await Bound(section.RefreshAsync());

        section.Detach();
        server.Apply(new UserList(IdOf<ListUsers>(), [Yvonne]));

        Assert.Empty(section.Users);
        Assert.All(sent, request => Assert.Null(server.Routes.Find(request.RequestId!)));
    }
}
