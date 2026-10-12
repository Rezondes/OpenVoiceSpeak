using OVS.Client.Dialogs;
using OVS.Client.ViewModels;
using OVS.Shared.Protocol;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.Connection;
using OVS.Shared.Protocol.Messages.Groups;
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

    static readonly KnownUserInfo Me = new("fp1", "ich", [WellKnownGroups.Admin]);

    readonly ManualTimeProvider time = new();
    readonly List<Request> sent = [];
    /// <summary>Each own group change mark the section gave, with the number of requests sent at that moment.</summary>
    readonly List<OwnGroupMark> marks = [];
    Action<bool>? markOverride;

    sealed record Rig(UsersAndBansViewModel Section, ServerViewModel Server);

    sealed record OwnGroupMark(bool Pending, int SentBefore);

    Rig Create(UsersAndBansDialogs? dialogs = null)
    {
        var server = FakeServers.Admin(sent, time);
        return new Rig(new UsersAndBansViewModel(server, pending =>
        {
            markOverride?.Invoke(pending);
            marks.Add(new OwnGroupMark(pending, sent.Count));
        }, dialogs), server);
    }

    /// <summary>Ticks the group <paramref name="groupName"/> on the card of <paramref name="nickname"/> and runs its command.</summary>
    static Task ToggleAsync(UsersAndBansViewModel section, string nickname, string groupName, bool on)
    {
        var toggle = section.Users.Single(user => user.Nickname == nickname).Toggles.Single(group => group.Name == groupName);
        toggle.IsChecked = on;
        return Bound(toggle.ToggleCommand.ExecuteAsync(null));
    }

    [Fact]
    public async Task ToggleUserGroup_OwnGroup_MarksBeforeTheRequestIsSent()
    {
        var (section, _) = Create();
        section.Apply(new UserList("r", [Me]));

        await ToggleAsync(section, "ich", "Gast", on: true);

        Assert.Equal(new OwnGroupMark(true, 0), marks.First(mark => mark.Pending)); // the first list already unmarked (nothing pending)
        Assert.IsType<AssignGroup>(sent[0]);
    }

    [Fact]
    public async Task ToggleUserGroup_OtherUsersGroup_NeverMarksTrue()
    {
        var (section, _) = Create();
        section.Apply(new UserList("r", [Yvonne]));

        await ToggleAsync(section, "Yvonne", "Admin", on: true);

        Assert.DoesNotContain(marks, mark => mark.Pending);
    }

    [Fact]
    public async Task Error_ForTheOwnGroupChange_Unmarks()
    {
        var (section, _) = Create();
        section.Apply(new UserList("r", [Me]));
        await ToggleAsync(section, "ich", "Gast", on: true);

        section.Apply(new Error(sent.LastRequestId<AssignGroup>(), ReasonCodes.LastAdmin));

        Assert.False(marks[^1].Pending);
    }

    [Fact]
    public async Task Error_UnknownRequestId_LeavesTheMark()
    {
        var (section, _) = Create();
        section.Apply(new UserList("r", [Me]));
        await ToggleAsync(section, "ich", "Gast", on: true);
        var before = marks.Count;

        section.Apply(new Error("unbekannt", ReasonCodes.LastAdmin));

        Assert.Equal(before, marks.Count);
        Assert.True(marks[^1].Pending);
    }

    [Fact]
    public async Task UserList_ShowsTheOwnChange_UnmarksAndPlaysTheTone()
    {
        var (section, server) = Create();
        var heard = new List<OVS.Client.Audio.Sounds.SoundEvent>();
        server.SoundRequested += heard.Add;
        section.Apply(new UserList("r", [Me]));
        await ToggleAsync(section, "ich", "Gast", on: true);

        section.Apply(new UserList(sent.LastRequestId<ListUsers>(), [Me with { GroupIds = [WellKnownGroups.Admin, WellKnownGroups.Guest] }]));

        Assert.False(marks[^1].Pending);
        Assert.Equal([OVS.Client.Audio.Sounds.SoundEvent.GroupChangedByMe], heard);
    }

    [Fact]
    public async Task UserList_OlderThanTheChange_KeepsTheMark()
    {
        var (section, _) = Create();
        section.Apply(new UserList("r", [Me]));
        await ToggleAsync(section, "ich", "Gast", on: true);

        section.Apply(new UserList("alt", [Me]));

        Assert.True(marks[^1].Pending);
    }

    /// <summary>Step 27h (QS1, decided by the user): a closed page can confirm no own group change, so it unmarks.</summary>
    [Fact]
    public async Task Detach_OwnGroupChangePending_Unmarks()
    {
        var (section, _) = Create();
        section.Apply(new UserList("r", [Me]));
        await ToggleAsync(section, "ich", "Gast", on: true);

        section.Detach();

        Assert.False(marks[^1].Pending);
    }

    [Fact]
    public void Detach_NothingPending_StaysUnmarked()
    {
        var (section, _) = Create();

        section.Detach();

        Assert.DoesNotContain(marks, mark => mark.Pending);
    }

    [Fact]
    public async Task Detach_ThenAnOlderUserList_DoesNotMarkAgain()
    {
        var (section, _) = Create();
        section.Apply(new UserList("r", [Me]));
        await ToggleAsync(section, "ich", "Gast", on: true);
        section.Detach();

        section.Apply(new UserList("alt", [Me]));

        Assert.False(marks[^1].Pending);
    }

    [Fact]
    public async Task ToggleUserGroup_MarkThrows_PropagatesAndSendsNothing()
    {
        var (section, _) = Create();
        section.Apply(new UserList("r", [Me]));
        markOverride = _ => throw new InvalidOperationException("Markierung kaputt");

        await Assert.ThrowsAsync<InvalidOperationException>(() => ToggleAsync(section, "ich", "Gast", on: true));

        Assert.Empty(sent);
    }

    [Fact]
    public async Task DeleteUser_NoDialogs_SendsNothing()
    {
        var (section, _) = Create();
        section.Apply(new UserList("r", [Yvonne]));

        await Bound(Assert.Single(section.Users).DeleteCommand.ExecuteAsync(null));

        Assert.Empty(sent);
    }

    [Fact]
    public async Task DeleteUser_ConfirmThrows_PropagatesAndSendsNothing()
    {
        var (section, _) = Create(new UsersAndBansDialogs { ConfirmDeleteUser = _ => throw new InvalidOperationException("Dialog kaputt") });
        section.Apply(new UserList("r", [Yvonne]));

        await Assert.ThrowsAsync<InvalidOperationException>(() => Bound(Assert.Single(section.Users).DeleteCommand.ExecuteAsync(null)));

        Assert.Empty(sent);
    }

    [Fact]
    public async Task Ban_GivenRecord_AsksItsBanDialogWithTheNickname()
    {
        var asked = new List<string>();
        var (section, _) = Create(new UsersAndBansDialogs { Ban = (nickname, _, _) => { asked.Add(nickname); return Task.FromResult<BanChoice?>(null); } });
        section.Apply(new UserList("r", [Yvonne]));

        await Bound(Assert.Single(section.Users).BanCommand.ExecuteAsync(null));

        Assert.Equal(["Yvonne"], asked);
        Assert.Empty(sent);
    }

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

        server.Apply(new UserList(sent.LastRequestId<ListUsers>(), [Yvonne]));

        Assert.Equal("Yvonne", Assert.Single(section.Users).Nickname);
        Assert.True(section.UsersLoad.HasSucceeded);
        Assert.False(section.BansLoad.HasSucceeded);
    }

    [Fact]
    public async Task Apply_BanListOfItsRequest_ShowsTheBanAndLeavesTheUsersLoading()
    {
        var (section, server) = Create();
        await Bound(section.RefreshAsync());

        server.Apply(new BanList(sent.LastRequestId<ListBans>(), [YvonnesBan]));

        Assert.Equal("Yvonne", Assert.Single(section.Bans).Nickname);
        Assert.True(section.BansLoad.HasSucceeded);
        Assert.False(section.UsersLoad.HasSucceeded);
    }

    [Fact]
    public async Task Apply_BanListAfterAnUnban_EndsTheBansBusyMark()
    {
        var (section, server) = Create();
        await Bound(section.RefreshAsync());
        server.Apply(new BanList(sent.LastRequestId<ListBans>(), [YvonnesBan]));
        var row = Assert.Single(section.Bans);

        await Bound(row.UnbanCommand.ExecuteAsync(null));
        Assert.True(row.Busy.IsRunning);
        server.Apply(new BanList(sent.LastRequestId<Unban>(), []));

        Assert.False(row.Busy.IsRunning);
    }

    [Fact]
    public async Task Apply_ErrorForTheUserListRequest_ReleasesTheNextRefresh()
    {
        var (section, server) = Create();
        await Bound(section.RefreshAsync());

        server.Apply(new Error(sent.LastRequestId<ListUsers>(), ReasonCodes.PermissionDenied));
        time.Advance(TimeSpan.FromSeconds(2));
        await Bound(section.RefreshAsync());

        Assert.Equal(2, sent.OfType<ListUsers>().Count());
    }

    [Fact]
    public async Task FollowServer_SomeoneGoesOffline_AsksForTheUserListAgain()
    {
        var (section, server) = Create();
        await Bound(section.RefreshAsync());
        server.Apply(new UserList(sent.LastRequestId<ListUsers>(), [Yvonne]));
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
        server.Apply(new UserList(sent.LastRequestId<ListUsers>(), [Yvonne]));
        time.Advance(TimeSpan.FromSeconds(2));

        section.FollowServer();

        Assert.Single(sent.OfType<ListUsers>());
    }

    [Fact]
    public async Task Detach_ListsAnswered_AsksForNothingMore()
    {
        var (section, server) = Create();
        await Bound(section.RefreshAsync());
        server.Apply(new UserList(sent.LastRequestId<ListUsers>(), [Yvonne]));
        server.Apply(new BanList(sent.LastRequestId<ListBans>(), []));
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
        server.Apply(new UserList(sent.LastRequestId<ListUsers>(), [Yvonne]));

        Assert.Empty(section.Users);
        Assert.All(sent, request => Assert.Null(server.Routes.Find(request.RequestId!)));
    }
}
