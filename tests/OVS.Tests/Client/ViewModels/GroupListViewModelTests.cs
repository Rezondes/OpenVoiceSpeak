using OVS.Client.Collections;
using OVS.Client.Localization;
using OVS.Client.Models;
using OVS.Client.ViewModels;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.Connection;
using OVS.Shared.Protocol.Messages.Groups;
using OVS.Shared.Protocol.Messages.Users;
using OVS.Tests.TestSupport;
using static OVS.Tests.TestSupport.HangGuard;

namespace OVS.Tests.Client.ViewModels;

/// <summary>The groups tab on its own: the rows and the selection, a new group, saving, the order, deleting and its place.</summary>
public sealed class GroupListViewModelTests
{
    static readonly Guid TeamId = Guid.NewGuid();
    static readonly GroupInfo Guest = new(WellKnownGroups.Guest, "Gast", Permission.Speak, true);
    static readonly GroupInfo Team = new(TeamId, "Team", Permission.Speak, true);
    static readonly GroupInfo Admin = new(WellKnownGroups.Admin, "Admin", Permission.All, true);

    readonly ManualTimeProvider time = new();
    readonly List<Request> sent = [];
    readonly List<AdminPlace> asked = [];
    bool leaveConfirmed = true;

    sealed record Rig(GroupListViewModel Section, ServerViewModel Server);

    /// <summary>A tab on a server that answers nothing by itself; like the page it hears every state change.</summary>
    Rig Create()
    {
        var server = FakeServers.Admin(sent, time);
        var list = new GroupListViewModel(server, place =>
        {
            asked.Add(place);
            return Task.FromResult(leaveConfirmed);
        });
        server.StateChanged += list.OnStateChanged; // the page tells the tab about every state change
        return new Rig(list, server);
    }

    static GroupEditViewModel Row(Rig rig, string name) => rig.Section.Groups.Live().Single(row => row.Name == name);

    static void ServerGroups(Rig rig, params GroupInfo[] groups) => rig.Server.Apply(new GroupsChanged(groups));

    static void OwnRights(Rig rig, Permission rights) =>
        rig.Server.Apply(new UserUpdated(new UserInfo(1, "fp1", "ich", FakeServers.Lobby, false, false, false, rights, [WellKnownGroups.Admin])));

    /// <summary>Everything the tab does here completes at once, so a test that is not async sees the end of it (no context moves it).</summary>
    static void Move(Rig rig, GroupEditViewModel source, GroupEditViewModel target, bool after) =>
        Assert.True(rig.Section.MoveGroupAsync(source, target, after).IsCompletedSuccessfully);

    static string[] Names(Rig rig) => rig.Section.Groups.Live().Select(row => row.Name).ToArray();

    // ---- the rows and the selection ----

    [Fact]
    public void Groups_AfterCreation_OneRowPerServerGroupInServerOrderAndTheFirstSelected()
    {
        var rig = Create();

        Assert.Equal(["Gast", "Admin"], Names(rig));
        Assert.Equal("Gast", rig.Section.SelectedGroup?.Name);
    }

    [Fact]
    public void OnStateChanged_GroupAdded_RowListedAndSelectionKept()
    {
        var rig = Create();
        rig.Section.SelectedGroup = Row(rig, "Admin");

        ServerGroups(rig, Guest, Team, Admin);

        Assert.Equal(["Gast", "Team", "Admin"], Names(rig));
        Assert.Equal(WellKnownGroups.Admin, rig.Section.SelectedGroup?.Id);
    }

    [Fact]
    public void OnStateChanged_SelectedGroupRemoved_FirstGroupSelected()
    {
        var rig = Create();
        ServerGroups(rig, Guest, Team, Admin);
        rig.Section.SelectedGroup = Row(rig, "Team");

        ServerGroups(rig, Guest, Admin);

        Assert.Equal("Gast", rig.Section.SelectedGroup?.Name);
    }

    [Fact]
    public void OnStateChanged_DraftSelected_DraftStaysListedAndSelected()
    {
        var rig = Create();
        rig.Section.NewGroupCommand.Execute(null);
        var draft = rig.Section.SelectedGroup;

        ServerGroups(rig, Guest, Admin);

        Assert.Null(draft?.Id);
        Assert.Same(draft, rig.Section.SelectedGroup);
        Assert.Equal(["Gast", "Admin", Strings.Group_New], Names(rig));
    }

    [Fact]
    public void OnStateChanged_GroupUntouched_RowBuiltAnewWithTheServersRights()
    {
        var rig = Create();
        var before = Row(rig, "Gast");

        ServerGroups(rig, Guest with { Permissions = Permission.Speak | Permission.SpeakLinked }, Admin);

        Assert.NotSame(before, Row(rig, "Gast"));
        Assert.Equal(Permission.Speak | Permission.SpeakLinked, Row(rig, "Gast").Permissions);
    }

    [Fact]
    public void OnStateChanged_GroupEdited_SameRowKeepsTheNameAndFollowsTheRights()
    {
        var rig = Create();
        var edited = Row(rig, "Gast");
        edited.Name = "Neu";

        ServerGroups(rig, Guest with { Permissions = Permission.Speak | Permission.SpeakLinked }, Admin);

        Assert.Same(edited, Row(rig, "Neu"));
        Assert.Equal(Permission.Speak | Permission.SpeakLinked, edited.Permissions);
    }

    [Fact]
    public void OnStateChanged_GroupEditedAndOwnRightsChanged_NewRowCarriesTheEdit()
    {
        var rig = Create();
        var edited = Row(rig, "Gast");
        edited.Name = "Neu";

        OwnRights(rig, Permission.All & ~Permission.UserBan);

        Assert.NotSame(edited, Row(rig, "Neu"));
        Assert.True(Row(rig, "Neu").HasChanges);
    }

    [Fact]
    public void OnStateChanged_GroupEditedAndOwnManageRightLost_RowReadOnlyAndEditDropped()
    {
        var rig = Create();
        Row(rig, "Gast").Name = "Neu";

        OwnRights(rig, Permission.GroupsView);

        Assert.True(Row(rig, "Gast").IsReadOnly);
        Assert.False(Row(rig, "Gast").HasChanges);
    }

    [Fact]
    public void OnStateChanged_EditedGroupBecomesStrongerThanTheActor_RowReadOnlyAndEditDropped()
    {
        var rig = Create();
        OwnRights(rig, Permission.Speak | Permission.GroupsView | Permission.GroupsManage);
        Row(rig, "Gast").Name = "Neu";

        ServerGroups(rig, Guest with { Permissions = Permission.Speak | Permission.UserBan }, Admin);

        Assert.True(Row(rig, "Gast").IsReadOnly);
        Assert.False(Row(rig, "Gast").HasChanges);
    }

    // ---- a new group ----

    [Fact]
    public void OnStateChanged_CreateRightLostAndGained_NewGroupCanExecuteOnlyWithIt()
    {
        var rig = Create();

        OwnRights(rig, Permission.GroupsView);
        Assert.False(rig.Section.NewGroupCommand.CanExecute(null));
        OwnRights(rig, Permission.All);

        Assert.True(rig.Section.NewGroupCommand.CanExecute(null));
    }

    [Fact]
    public void OnStateChanged_ServerMovesTheEditedSelectedGroup_MoveUpDisabledAndAllThreeCommandsAskedAgain()
    {
        var rig = Create();
        ServerGroups(rig, Guest, Team, Admin);
        Row(rig, "Team").Name = "Neu";
        rig.Section.SelectedGroup = Row(rig, "Neu");
        Assert.True(rig.Section.MoveGroupUpCommand.CanExecute(null));
        var raised = new List<string>();
        rig.Section.NewGroupCommand.CanExecuteChanged += (_, _) => raised.Add("new");
        rig.Section.MoveGroupUpCommand.CanExecuteChanged += (_, _) => raised.Add("up");
        rig.Section.MoveGroupDownCommand.CanExecuteChanged += (_, _) => raised.Add("down");

        ServerGroups(rig, Team, Guest, Admin); // the edited row stays the selected one, now first

        Assert.False(rig.Section.MoveGroupUpCommand.CanExecute(null));
        Assert.Equal(["down", "new", "up"], raised.Order());
    }

    [Fact]
    public void OnStateChanged_SelectionMovesByCode_IsSelectingInCodeOnlyWhileItMoves()
    {
        var rig = Create();
        ServerGroups(rig, Guest, Team, Admin);
        rig.Section.SelectedGroup = Row(rig, "Team");
        var duringChange = new List<bool>();
        rig.Section.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(GroupListViewModel.SelectedGroup)) duringChange.Add(rig.Section.IsSelectingInCode);
        };

        ServerGroups(rig, Guest, Admin);
        rig.Section.SelectedGroup = Row(rig, "Admin");

        Assert.Equal([true, false], duringChange);
    }

    [Fact]
    public void NewGroup_LeaveRefused_AddsNothingAndAsksAboutThePlace()
    {
        var rig = Create();
        leaveConfirmed = false;

        rig.Section.NewGroupCommand.Execute(null);

        Assert.Equal(["Gast", "Admin"], Names(rig));
        Assert.Same(rig.Section.Place, Assert.Single(asked));
    }

    [Fact]
    public void NewGroup_LeaveConfirmed_SelectedUnsavedDraftNamedNewWithTheSpeakRight()
    {
        var rig = Create();

        rig.Section.NewGroupCommand.Execute(null);

        var draft = rig.Section.SelectedGroup!;
        Assert.Null(draft.Id);
        Assert.Equal(Strings.Group_New, draft.Name);
        Assert.Equal(Permission.Speak, draft.Permissions);
        Assert.Contains(draft, rig.Section.Groups.Live());
    }

    // ---- saving ----

    [Fact]
    public void SaveGroup_SavedGroupEdited_SendsItsUpdateAndClearsTheError()
    {
        var rig = Create();
        rig.Section.GroupError = "alt";
        Row(rig, "Gast").Name = "Neu";

        rig.Section.SaveGroupCommand.Execute(null);

        Assert.Equal(new UpdateGroup(WellKnownGroups.Guest, "Neu", Permission.Speak), Assert.Single(sent) with { RequestId = null });
        Assert.Null(rig.Section.GroupError);
    }

    [Fact]
    public void SaveGroup_NameInvalid_ShowsTheErrorAndSendsNothing()
    {
        var rig = Create();
        Row(rig, "Gast").Name = "   ";

        rig.Section.SaveGroupCommand.Execute(null);

        Assert.Equal(Strings.Dlg_NameInvalid, rig.Section.GroupError);
        Assert.Empty(sent);
    }

    [Fact]
    public void SaveGroup_SaveOnItsWay_SendsNoSecondOne()
    {
        var rig = Create();
        Row(rig, "Gast").Name = "Neu";
        rig.Section.SaveGroupCommand.Execute(null);

        rig.Section.SaveGroupCommand.Execute(null);

        Assert.Single(sent);
    }

    [Fact]
    public void SaveGroup_ReadOnlyGroupSelected_CannotExecute()
    {
        var rig = Create();

        rig.Section.SelectedGroup = Row(rig, "Admin");

        Assert.False(rig.Section.SaveGroupCommand.CanExecute(null));
    }

    [Fact]
    public void SaveGroup_DraftAnswered_DraftGivesWayToTheSavedGroupWhichIsSelected()
    {
        var rig = Create();
        rig.Section.NewGroupCommand.Execute(null);
        rig.Section.SelectedGroup!.Name = "Team";
        rig.Section.SaveGroupCommand.Execute(null);
        Assert.Equal(new CreateGroup("Team", Permission.Speak), Assert.Single(sent) with { RequestId = null });
        Assert.Contains(rig.Section.Groups.Live(), row => row.Id is null);

        ServerGroups(rig, Guest, Team, Admin);

        Assert.DoesNotContain(rig.Section.Groups.Live(), row => row.Id is null);
        Assert.Equal(TeamId, rig.Section.SelectedGroup?.Id);
    }

    [Fact]
    public void SaveGroup_DraftRefused_DraftStaysListedAndSelected()
    {
        var rig = Create();
        rig.Section.NewGroupCommand.Execute(null);
        var draft = rig.Section.SelectedGroup!;
        rig.Section.SaveGroupCommand.Execute(null);

        rig.Server.Apply(new Error(sent[^1].RequestId, ReasonCodes.PermissionDenied));

        Assert.Contains(draft, rig.Section.Groups.Live());
        Assert.Same(draft, rig.Section.SelectedGroup);
    }

    [Fact]
    public void SaveGroup_DraftAnsweredAfterAnotherGroupWasSelected_SelectionStaysThere()
    {
        var rig = Create();
        rig.Section.NewGroupCommand.Execute(null);
        rig.Section.SaveGroupCommand.Execute(null);
        rig.Section.SelectedGroup = Row(rig, "Admin");

        ServerGroups(rig, Guest, Team with { Name = Strings.Group_New }, Admin);

        Assert.DoesNotContain(rig.Section.Groups.Live(), row => row.Id is null);
        Assert.Equal(WellKnownGroups.Admin, rig.Section.SelectedGroup?.Id);
    }

    // ---- order ----

    [Fact]
    public void MoveGroupCommands_FirstMiddleLastSelected_UpExceptAtTheTopDownExceptAtTheBottom()
    {
        var rig = Create();
        ServerGroups(rig, Guest, Team, Admin);

        rig.Section.SelectedGroup = Row(rig, "Gast");
        Assert.False(rig.Section.MoveGroupUpCommand.CanExecute(null));
        Assert.True(rig.Section.MoveGroupDownCommand.CanExecute(null));
        rig.Section.SelectedGroup = Row(rig, "Team");
        Assert.True(rig.Section.MoveGroupUpCommand.CanExecute(null));
        Assert.True(rig.Section.MoveGroupDownCommand.CanExecute(null));
        rig.Section.SelectedGroup = Row(rig, "Admin");
        Assert.True(rig.Section.MoveGroupUpCommand.CanExecute(null));
        Assert.False(rig.Section.MoveGroupDownCommand.CanExecute(null));
    }

    [Fact]
    public void MoveGroupCommands_ManageRightLost_CannotExecute()
    {
        var rig = Create();
        ServerGroups(rig, Guest, Team, Admin);
        rig.Section.SelectedGroup = Row(rig, "Team");

        OwnRights(rig, Permission.All & ~Permission.GroupsManage);

        Assert.False(rig.Section.MoveGroupUpCommand.CanExecute(null));
        Assert.False(rig.Section.MoveGroupDownCommand.CanExecute(null));
    }

    [Fact]
    public void MoveGroupCommands_DraftSelected_CannotExecute()
    {
        var rig = Create();
        rig.Section.NewGroupCommand.Execute(null);

        Assert.False(rig.Section.MoveGroupUpCommand.CanExecute(null));
        Assert.False(rig.Section.MoveGroupDownCommand.CanExecute(null));
    }

    [Fact]
    public void MoveGroupDown_GroupSelected_SendsTheFullOrderAndShowsItAtOnce()
    {
        var rig = Create();
        ServerGroups(rig, Guest, Team, Admin);
        rig.Section.SelectedGroup = Row(rig, "Gast");

        rig.Section.MoveGroupDownCommand.Execute(null);

        Assert.Equal([TeamId, WellKnownGroups.Guest, WellKnownGroups.Admin], Assert.IsType<ReorderGroups>(Assert.Single(sent)).GroupIds);
        Assert.Equal(["Team", "Gast", "Admin"], Names(rig));
    }

    [Fact]
    public void MoveGroupUp_GroupSelected_SendsTheFullOrder()
    {
        var rig = Create();
        ServerGroups(rig, Guest, Team, Admin);
        rig.Section.SelectedGroup = Row(rig, "Admin");

        rig.Section.MoveGroupUpCommand.Execute(null);

        Assert.Equal([WellKnownGroups.Guest, WellKnownGroups.Admin, TeamId], Assert.IsType<ReorderGroups>(Assert.Single(sent)).GroupIds);
    }

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(0, 1, false)]
    [InlineData(1, 0, true)]
    public void MoveGroupAsync_SameGroupOrOrderUnchanged_SendsNothing(int sourceIndex, int targetIndex, bool after)
    {
        var rig = Create();
        var rows = rig.Section.Groups.Live().ToList();

        Move(rig, rows[sourceIndex], rows[targetIndex], after);

        Assert.Empty(sent);
    }

    [Fact]
    public void MoveGroupAsync_UnsavedGroup_SendsNothing()
    {
        var rig = Create();
        rig.Section.NewGroupCommand.Execute(null);

        Move(rig, rig.Section.SelectedGroup!, Row(rig, "Gast"), after: false);

        Assert.Empty(sent);
    }

    [Fact]
    public void MoveGroupAsync_GroupFoldingAway_SendsNothing()
    {
        var rig = Create();
        ServerGroups(rig, Guest, Team, Admin);
        var folding = Row(rig, "Team");
        rig.Server.Leave.Delay = TimeSpan.FromMinutes(1);
        ServerGroups(rig, Guest, Admin);
        Assert.True(CollectionSync.IsLeaving(rig.Section.Groups, folding));

        Move(rig, folding, Row(rig, "Gast"), after: false);

        Assert.Empty(sent);
    }

    [Fact]
    public void MoveGroupAsync_WithoutTheManageRight_SendsNothing()
    {
        var rig = Create();
        OwnRights(rig, Permission.All & ~Permission.GroupsManage);

        Move(rig, Row(rig, "Admin"), Row(rig, "Gast"), after: false);

        Assert.Empty(sent);
    }

    [Fact]
    public void MoveGroupAsync_RefusedByTheServer_OrderGoesBackAndTheBusyMarkEnds()
    {
        var rig = Create();
        Move(rig, Row(rig, "Admin"), Row(rig, "Gast"), after: false);
        Assert.Equal(["Admin", "Gast"], Names(rig));

        rig.Server.Apply(new Error(sent[^1].RequestId, ReasonCodes.PermissionDenied));

        Assert.Equal(["Gast", "Admin"], Names(rig));
        Assert.False(rig.Section.GroupReorder.IsRunning);
    }

    [Fact]
    public async Task MoveGroupAsync_TwoOrdersSent_OlderAnswerChangesNothingAndNewerRefusalBringsTheServerOrderBack()
    {
        var rig = Create();
        ServerGroups(rig, Guest, Team, Admin);
        Move(rig, Row(rig, "Admin"), Row(rig, "Gast"), after: false);
        var olderRequestId = sent[^1].RequestId;
        Move(rig, Row(rig, "Team"), Row(rig, "Admin"), after: false);
        var newerRequestId = sent[^1].RequestId;

        rig.Server.Apply(new Error(olderRequestId, ReasonCodes.PermissionDenied));
        Assert.Equal(["Team", "Admin", "Gast"], Names(rig));
        rig.Server.Apply(new Error(newerRequestId, ReasonCodes.PermissionDenied));

        // both orders wait on one completion: the later waiter, which puts the order back, is queued to the pool
        await AsyncWait.Until(() => Names(rig) is ["Gast", "Team", "Admin"]);
    }

    // ---- deleting ----

    [Fact]
    public void DeleteGroup_SavedGroupSelected_SendsItsDeletion()
    {
        var rig = Create();
        ServerGroups(rig, Guest, Team, Admin);
        rig.Section.SelectedGroup = Row(rig, "Team");

        rig.Section.DeleteGroupCommand.Execute(null);

        Assert.Equal(new DeleteGroup(TeamId), Assert.Single(sent) with { RequestId = null });
    }

    [Fact]
    public void DeleteGroup_DraftSelected_DraftDroppedWithoutSendingAndNothingSelected()
    {
        var rig = Create();
        rig.Section.NewGroupCommand.Execute(null);

        rig.Section.DeleteGroupCommand.Execute(null);

        Assert.Equal(["Gast", "Admin"], Names(rig));
        Assert.Null(rig.Section.SelectedGroup);
        Assert.Empty(sent);
    }

    [Fact]
    public void DeleteGroup_ProtectedGroupSelected_CannotExecute()
    {
        var rig = Create();

        rig.Section.SelectedGroup = Row(rig, "Admin");

        Assert.False(rig.Section.DeleteGroupCommand.CanExecute(null));
    }

    // ---- the place ----

    [Fact]
    public async Task Place_NothingEdited_NoChangesAndSavingSucceedsWithoutSending()
    {
        var rig = Create();

        Assert.False(rig.Section.Place.HasChanges());
        Assert.True(await Bound(rig.Section.Place.SaveAsync()));
        Assert.Empty(sent);
    }

    [Fact]
    public async Task Place_SelectedGroupEdited_HasChangesAndSavingSendsItAndWaitsForTheServer()
    {
        var rig = Create();
        Row(rig, "Gast").Name = "Neu";
        Assert.True(rig.Section.Place.HasChanges());

        var saving = rig.Section.Place.SaveAsync();

        Assert.IsType<UpdateGroup>(Assert.Single(sent));
        Assert.False(saving.IsCompleted);
        ServerGroups(rig, Guest with { Name = "Neu" }, Admin);
        Assert.True(await Bound(saving));
        Assert.False(rig.Section.Place.HasChanges());
    }

    [Fact]
    public async Task Place_OtherGroupEdited_HasChangesButSavingSendsNothingAndFails()
    {
        var rig = Create();
        ServerGroups(rig, Guest, Team, Admin);
        Row(rig, "Team").Name = "Neu";
        rig.Section.SelectedGroup = Row(rig, "Gast");

        Assert.True(rig.Section.Place.HasChanges());
        Assert.False(await Bound(rig.Section.Place.SaveAsync()));
        Assert.Empty(sent);
    }

    [Fact]
    public async Task Place_GroupSaveOnItsWay_SavingWaitsForItAndSendsNoSecondOne()
    {
        var rig = Create();
        Row(rig, "Gast").Name = "Neu";
        rig.Section.SaveGroupCommand.Execute(null);

        var saving = rig.Section.Place.SaveAsync();

        Assert.False(saving.IsCompleted);
        ServerGroups(rig, Guest with { Name = "Neu" }, Admin);
        Assert.True(await Bound(saving));
        Assert.Single(sent);
    }

    [Fact]
    public void Place_DraftPresent_HasChanges()
    {
        var rig = Create();

        rig.Section.NewGroupCommand.Execute(null);

        Assert.True(rig.Section.Place.HasChanges());
    }

    [Fact]
    public void PlaceDiscard_GroupEdited_EditRevertedAndSelectionKept()
    {
        var rig = Create();
        var edited = Row(rig, "Gast");
        edited.Name = "Neu";

        rig.Section.Place.Discard();

        Assert.Equal("Gast", edited.Name);
        Assert.Same(edited, rig.Section.SelectedGroup);
        Assert.False(rig.Section.Place.HasChanges());
    }

    [Fact]
    public void PlaceDiscard_DraftSelected_DraftDroppedAndFirstGroupSelected()
    {
        var rig = Create();
        rig.Section.NewGroupCommand.Execute(null);

        rig.Section.Place.Discard();

        Assert.Equal(["Gast", "Admin"], Names(rig));
        Assert.Equal("Gast", rig.Section.SelectedGroup?.Name);
    }

    [Fact]
    public void PlaceDiscard_DraftNotSelected_DraftDroppedAndSelectionKept()
    {
        var rig = Create();
        rig.Section.NewGroupCommand.Execute(null);
        rig.Section.SelectedGroup = Row(rig, "Admin");

        rig.Section.Place.Discard();

        Assert.Equal(["Gast", "Admin"], Names(rig));
        Assert.Equal("Admin", rig.Section.SelectedGroup?.Name);
    }
}
