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
    static readonly GroupInfo Team = new(TeamId, "Team", Permission.Speak, true);

    readonly ManualTimeProvider time = new();
    readonly List<Request> sent = [];
    readonly List<AdminPlace> asked = [];
    bool leaveConfirmed = true;

    /// <summary>A tab on a server that answers nothing by itself; like the page it hears every state change.</summary>
    Rig Create() => Build(FakeServers.Admin(sent, time));

    /// <summary>A tab built for an actor who holds exactly these rights (and no group) on a server with exactly these groups.</summary>
    Rig CreateFor(Permission rights, params GroupInfo[] groups)
    {
        var server = FakeServers.Admin(sent, time);
        FakeServers.SetOwnRightsAndGroups(server, rights, []);
        server.Apply(new GroupsChanged(groups));
        return Build(server);
    }

    Rig Build(ServerViewModel server)
    {
        var list = new GroupListViewModel(server, place =>
        {
            asked.Add(place);
            return Task.FromResult(leaveConfirmed);
        });
        server.StateChanged += list.FollowServer; // the page tells the tab about every state change
        return new Rig(list, server);
    }

    static GroupEditViewModel Row(Rig rig, string name) => rig.Section.Groups.Live().Single(row => row.Name == name);

    static void ServerGroups(Rig rig, params GroupInfo[] groups) => rig.Server.Apply(new GroupsChanged(groups));

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
    public void FollowServer_GroupAdded_RowListedAndSelectionKept()
    {
        var rig = Create();
        rig.Section.SelectedGroup = Row(rig, "Admin");

        ServerGroups(rig, FakeServers.GuestGroup, Team, FakeServers.AdminGroup);

        Assert.Equal(["Gast", "Team", "Admin"], Names(rig));
        Assert.Equal(WellKnownGroups.Admin, rig.Section.SelectedGroup?.Id);
    }

    [Fact]
    public void FollowServer_SelectedGroupRemoved_FirstGroupSelected()
    {
        var rig = Create();
        ServerGroups(rig, FakeServers.GuestGroup, Team, FakeServers.AdminGroup);
        rig.Section.SelectedGroup = Row(rig, "Team");

        ServerGroups(rig, FakeServers.GuestGroup, FakeServers.AdminGroup);

        Assert.Equal("Gast", rig.Section.SelectedGroup?.Name);
    }

    [Fact]
    public void FollowServer_DraftSelected_DraftStaysListedAndSelected()
    {
        var rig = Create();
        rig.Section.NewGroupCommand.Execute(null);
        var draft = rig.Section.SelectedGroup;

        ServerGroups(rig, FakeServers.GuestGroup, FakeServers.AdminGroup);

        Assert.Null(draft?.Id);
        Assert.Same(draft, rig.Section.SelectedGroup);
        Assert.Equal(["Gast", "Admin", Strings.Group_New], Names(rig));
    }

    [Fact]
    public void FollowServer_GroupUntouched_RowBuiltAnewWithTheServersRights()
    {
        var rig = Create();
        var before = Row(rig, "Gast");

        ServerGroups(rig, FakeServers.GuestGroup with { Permissions = Permission.Speak | Permission.SpeakLinked }, FakeServers.AdminGroup);

        Assert.NotSame(before, Row(rig, "Gast"));
        Assert.Equal(Permission.Speak | Permission.SpeakLinked, Row(rig, "Gast").Permissions);
    }

    [Fact]
    public void FollowServer_GroupEdited_SameRowKeepsTheNameFollowsTheRightsAndStillHasChanges()
    {
        var rig = Create();
        var edited = Row(rig, "Gast");
        edited.Name = "Neu";

        ServerGroups(rig, FakeServers.GuestGroup with { Permissions = Permission.Speak | Permission.SpeakLinked }, FakeServers.AdminGroup);

        Assert.Same(edited, Row(rig, "Neu"));
        Assert.Equal(Permission.Speak | Permission.SpeakLinked, edited.Permissions);
        Assert.True(rig.Section.Place.HasChanges());
    }

    [Fact]
    public void FollowServer_OwnMuteChanged_EditedRowStaysSelectedWithItsEdit()
    {
        var rig = Create();
        var edited = Row(rig, "Gast");
        edited.Name = "Neu";
        var self = rig.Server.Mirror.Users[rig.Server.Mirror.SelfId];

        rig.Server.Apply(new UserUpdated(self with { SelfMuted = !self.SelfMuted }));

        Assert.Same(edited, rig.Section.SelectedGroup);
        Assert.Equal("Neu", rig.Section.SelectedGroup!.Name);
        Assert.True(rig.Section.Place.HasChanges());
    }

    [Fact]
    public void FollowServer_GroupEditedAndOwnRightsChanged_NewRowCarriesTheEdit()
    {
        var rig = Create();
        var edited = Row(rig, "Gast");
        edited.Name = "Neu";

        FakeServers.SetOwnRightsAndGroups(rig.Server, Permission.All & ~Permission.UserBan, [WellKnownGroups.Admin]);

        Assert.NotSame(edited, Row(rig, "Neu"));
        Assert.True(Row(rig, "Neu").HasChanges);
    }

    [Fact]
    public void FollowServer_GroupEditedAndOwnManageRightLost_RowReadOnlyAndEditDropped()
    {
        var rig = Create();
        Row(rig, "Gast").Name = "Neu";

        FakeServers.SetOwnRightsAndGroups(rig.Server, Permission.GroupsView, [WellKnownGroups.Admin]);

        Assert.True(Row(rig, "Gast").IsReadOnly);
        Assert.False(Row(rig, "Gast").HasChanges);
    }

    [Fact]
    public void FollowServer_EditedGroupBecomesStrongerThanTheActor_RowReadOnlyAndEditDropped()
    {
        var rig = Create();
        FakeServers.SetOwnRightsAndGroups(rig.Server, Permission.Speak | Permission.GroupsView | Permission.GroupsManage, [WellKnownGroups.Admin]);
        Row(rig, "Gast").Name = "Neu";

        ServerGroups(rig, FakeServers.GuestGroup with { Permissions = Permission.Speak | Permission.UserBan }, FakeServers.AdminGroup);

        Assert.True(Row(rig, "Gast").IsReadOnly);
        Assert.False(Row(rig, "Gast").HasChanges);
    }

    // ---- the rows and the commands by the actor's rights ----

    [Fact]
    public void Groups_ActorHoldsEveryRightOfTheGroupButNotAll_OnlyTheHeldRightsToggleTheRowShowsTheGroupsRightsAndItCanBeDeleted()
    {
        var rig = CreateFor(Permission.GroupsManage | Permission.GroupsDelete | Permission.Speak | Permission.UserKick,
            FakeServers.GuestGroup, FakeServers.ModeratorGroup, FakeServers.AdminGroup);
        var moderator = Row(rig, "Moderator");

        Assert.True(moderator.Toggles.Single(toggle => toggle.Permission == Permission.UserKick).IsEnabled);
        Assert.False(moderator.Toggles.Single(toggle => toggle.Permission == Permission.ServerConfig).IsEnabled);
        Assert.Equal(Permission.Speak | Permission.UserKick, moderator.Permissions);
        Assert.True(moderator.CanDelete);
    }

    [Fact]
    public void Groups_ActorHoldsAllRights_AdminGroupReadOnlyWithDisabledTogglesAndNeitherProtectedGroupDeletable()
    {
        var rig = CreateFor(Permission.All, FakeServers.GuestGroup, FakeServers.AdminGroup);
        var admin = Row(rig, "Admin");

        Assert.True(admin.IsReadOnly);
        Assert.All(admin.Toggles, toggle => Assert.False(toggle.IsEnabled));
        Assert.False(admin.CanDelete);
        Assert.False(Row(rig, "Gast").CanDelete);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Moderator")]
    public void SelectedGroup_ProtectedOrStrongerGroupAndTheManageRightOnly_ReadOnlyAndNeitherSavableNorDeletable(string name)
    {
        var rig = CreateFor(Permission.GroupsManage | Permission.Speak, FakeServers.GuestGroup, FakeServers.ModeratorGroup, FakeServers.AdminGroup);

        rig.Section.SelectedGroup = Row(rig, name);

        Assert.False(rig.Section.SaveGroupCommand.CanExecute(null));
        Assert.False(rig.Section.DeleteGroupCommand.CanExecute(null));
        Assert.True(rig.Section.SelectedGroup.IsReadOnly);
    }

    [Fact]
    public void Commands_ViewRightOnly_NoCommandCanExecuteAndTheSelectedRowIsReadOnlyWithDisabledToggles()
    {
        var rig = CreateFor(Permission.GroupsView | Permission.Speak, FakeServers.GuestGroup, FakeServers.ModeratorGroup, FakeServers.AdminGroup);

        rig.Section.SelectedGroup = Row(rig, "Gast");

        Assert.False(rig.Section.NewGroupCommand.CanExecute(null));
        Assert.False(rig.Section.SaveGroupCommand.CanExecute(null));
        Assert.False(rig.Section.DeleteGroupCommand.CanExecute(null));
        Assert.False(rig.Section.MoveGroupDownCommand.CanExecute(null));
        Assert.True(rig.Section.SelectedGroup.IsReadOnly);
        Assert.All(rig.Section.SelectedGroup.Toggles, toggle => Assert.False(toggle.IsEnabled));
    }

    [Fact]
    public void Commands_CreateRightOnly_NewGroupAllowedAndSavedGroupNotSavable()
    {
        var rig = CreateFor(Permission.GroupsView | Permission.GroupsCreate | Permission.Speak, FakeServers.GuestGroup, FakeServers.ModeratorGroup, FakeServers.AdminGroup);

        rig.Section.SelectedGroup = Row(rig, "Gast");

        Assert.True(rig.Section.NewGroupCommand.CanExecute(null));
        Assert.False(rig.Section.SaveGroupCommand.CanExecute(null));
    }

    [Fact]
    public void Commands_CreateRightOnlyDraftAdded_DraftSavableAndDeletable()
    {
        var rig = CreateFor(Permission.GroupsView | Permission.GroupsCreate | Permission.Speak, FakeServers.GuestGroup, FakeServers.ModeratorGroup, FakeServers.AdminGroup);

        rig.Section.NewGroupCommand.Execute(null);

        Assert.True(rig.Section.SaveGroupCommand.CanExecute(null));
        Assert.True(rig.Section.DeleteGroupCommand.CanExecute(null));
    }

    [Fact]
    public void Commands_ManageRightOnly_SavedGroupSavableAndMovableButNeitherNewNorDeletable()
    {
        var rig = CreateFor(Permission.GroupsView | Permission.GroupsManage | Permission.Speak, FakeServers.GuestGroup, FakeServers.ModeratorGroup, FakeServers.AdminGroup);

        rig.Section.SelectedGroup = Row(rig, "Gast");

        Assert.False(rig.Section.NewGroupCommand.CanExecute(null));
        Assert.True(rig.Section.SaveGroupCommand.CanExecute(null));
        Assert.True(rig.Section.MoveGroupDownCommand.CanExecute(null));
        Assert.False(rig.Section.DeleteGroupCommand.CanExecute(null));
    }

    [Fact]
    public void Commands_DeleteRightAndTheModeratorsRights_ModeratorDeletableButNotSavable()
    {
        var rig = CreateFor(Permission.GroupsView | Permission.GroupsDelete | Permission.Speak | Permission.UserKick,
            FakeServers.GuestGroup, FakeServers.ModeratorGroup, FakeServers.AdminGroup);

        rig.Section.SelectedGroup = Row(rig, "Moderator");

        Assert.True(rig.Section.DeleteGroupCommand.CanExecute(null));
        Assert.False(rig.Section.SaveGroupCommand.CanExecute(null));
    }

    /// <summary>Every right is a checkbox, named in both languages.</summary>
    [Theory]
    [InlineData("de-DE", "Nutzerübersicht sehen", "Bans sehen", "Gruppen sehen", "Gruppen anlegen", "Gruppen löschen", "Nutzer löschen", "Gruppen bearbeiten", "Nutzer bannen und entbannen", "Passwort-Lock umgehen")]
    [InlineData("en-US", "View user overview", "View bans", "View groups", "Create groups", "Delete groups", "Delete users", "Edit groups", "Ban and unban users", "Bypass password lock")]
    public void Toggles_FirstRowInTheCulture_OneUniqueLabelPerSingleRightWithTheNewRightsNamed(string culture, string users, string bans, string groups,
        string create, string delete, string userDelete, string manage, string ban, string bypass)
    {
        var labels = TestCulture.With(culture, () => Create().Section.Groups.First().Toggles.ToDictionary(toggle => toggle.Permission, toggle => toggle.Label));
        var singleRights = Enum.GetValues<Permission>().Where(right => right != Permission.None && ((int)right & ((int)right - 1)) == 0 && right.IsSubsetOf(Permission.All)).ToList(); // Package 181: the rank is no toggle
        Assert.Equal(singleRights.Order(), labels.Keys.Order());
        Assert.Equal(labels.Count, labels.Values.Distinct().Count());
        Assert.Equal([users, bans, groups, create, delete, userDelete, manage, ban, bypass],
            new[] { Permission.UsersView, Permission.BansView, Permission.GroupsView, Permission.GroupsCreate, Permission.GroupsDelete, Permission.UserDelete,
                Permission.GroupsManage, Permission.UserBan, Permission.ChannelPasswordBypass }.Select(right => labels[right]));
    }

    // ---- a new group ----

    [Fact]
    public void FollowServer_CreateRightLostAndGained_NewGroupCanExecuteOnlyWithIt()
    {
        var rig = Create();

        FakeServers.SetOwnRightsAndGroups(rig.Server, Permission.GroupsView, [WellKnownGroups.Admin]);
        Assert.False(rig.Section.NewGroupCommand.CanExecute(null));
        FakeServers.SetOwnRightsAndGroups(rig.Server, Permission.All, [WellKnownGroups.Admin]);

        Assert.True(rig.Section.NewGroupCommand.CanExecute(null));
    }

    [Fact]
    public void FollowServer_ServerMovesTheEditedSelectedGroup_MoveUpDisabledAndAllThreeCommandsAskedAgain()
    {
        var rig = Create();
        ServerGroups(rig, FakeServers.GuestGroup, Team, FakeServers.AdminGroup);
        Row(rig, "Team").Name = "Neu";
        rig.Section.SelectedGroup = Row(rig, "Neu");
        Assert.True(rig.Section.MoveGroupUpCommand.CanExecute(null));
        var raised = new List<string>();
        rig.Section.NewGroupCommand.CanExecuteChanged += (_, _) => raised.Add("new");
        rig.Section.MoveGroupUpCommand.CanExecuteChanged += (_, _) => raised.Add("up");
        rig.Section.MoveGroupDownCommand.CanExecuteChanged += (_, _) => raised.Add("down");

        ServerGroups(rig, Team, FakeServers.GuestGroup, FakeServers.AdminGroup); // the edited row stays the selected one, now first

        Assert.False(rig.Section.MoveGroupUpCommand.CanExecute(null));
        Assert.Equal(["down", "new", "up"], raised.Order());
    }

    [Fact]
    public void FollowServer_SelectionMovesByCode_IsSelectingInCodeOnlyWhileItMoves()
    {
        var rig = Create();
        ServerGroups(rig, FakeServers.GuestGroup, Team, FakeServers.AdminGroup);
        rig.Section.SelectedGroup = Row(rig, "Team");
        var duringChange = new List<bool>();
        rig.Section.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(GroupListViewModel.SelectedGroup)) duringChange.Add(rig.Section.IsSelectingInCode);
        };

        ServerGroups(rig, FakeServers.GuestGroup, FakeServers.AdminGroup);
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
    public void SaveGroup_DraftNamedAndOneRightAdded_SendsTheCreateWithBothRights()
    {
        var rig = Create();
        rig.Section.NewGroupCommand.Execute(null);
        rig.Section.SelectedGroup!.Name = "Team";
        rig.Section.SelectedGroup.Toggles.Single(toggle => toggle.Permission == Permission.SpeakLinked).IsChecked = true;

        rig.Section.SaveGroupCommand.Execute(null);

        Assert.Equal(new CreateGroup("Team", Permission.Speak | Permission.SpeakLinked), Assert.Single(sent) with { RequestId = null });
    }

    [Fact]
    public void SaveGroup_DraftNameWithADirectionMark_ShowsTheErrorAndSendsNothing()
    {
        var rig = Create();
        rig.Section.NewGroupCommand.Execute(null);
        rig.Section.SelectedGroup!.Name = "Team" + (char)0x202E;

        rig.Section.SaveGroupCommand.Execute(null);

        Assert.Equal(Strings.Dlg_NameInvalid, rig.Section.GroupError);
        Assert.Empty(sent);
    }

    [Fact]
    public void SaveGroup_DraftNameCorrectedAfterAnError_ClearsTheErrorAndSendsTheCreate()
    {
        var rig = Create();
        rig.Section.NewGroupCommand.Execute(null);
        rig.Section.SelectedGroup!.Name = "Team" + (char)0x202E;
        rig.Section.SaveGroupCommand.Execute(null);
        rig.Section.SelectedGroup.Name = "Team";

        rig.Section.SaveGroupCommand.Execute(null);

        Assert.Null(rig.Section.GroupError);
        Assert.Equal(new CreateGroup("Team", Permission.Speak), Assert.Single(sent) with { RequestId = null });
    }

    [Fact]
    public void SaveGroup_DraftSent_ShowsBusyAfterTheDelayUntilTheServersGroupsArrive()
    {
        var rig = Create();
        rig.Section.NewGroupCommand.Execute(null);
        rig.Section.SelectedGroup!.Name = "Team";
        rig.Section.SaveGroupCommand.Execute(null);

        time.Advance(PendingActionViewModel.ShowAfter);
        Assert.True(rig.Section.GroupSave.IsBusy);
        ServerGroups(rig, FakeServers.GuestGroup, FakeServers.AdminGroup, Team);

        Assert.False(rig.Section.GroupSave.IsBusy);
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

        ServerGroups(rig, FakeServers.GuestGroup, Team, FakeServers.AdminGroup);

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

        ServerGroups(rig, FakeServers.GuestGroup, Team with { Name = Strings.Group_New }, FakeServers.AdminGroup);

        Assert.DoesNotContain(rig.Section.Groups.Live(), row => row.Id is null);
        Assert.Equal(WellKnownGroups.Admin, rig.Section.SelectedGroup?.Id);
    }

    // ---- order ----

    [Fact]
    public void MoveGroupCommands_FirstMiddleLastSelected_UpExceptAtTheTopDownExceptAtTheBottom()
    {
        var rig = Create();
        ServerGroups(rig, FakeServers.GuestGroup, Team, FakeServers.AdminGroup);

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
        ServerGroups(rig, FakeServers.GuestGroup, Team, FakeServers.AdminGroup);
        rig.Section.SelectedGroup = Row(rig, "Team");

        FakeServers.SetOwnRightsAndGroups(rig.Server, Permission.All & ~Permission.GroupsManage, [WellKnownGroups.Admin]);

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
        ServerGroups(rig, FakeServers.GuestGroup, Team, FakeServers.AdminGroup);
        rig.Section.SelectedGroup = Row(rig, "Gast");

        rig.Section.MoveGroupDownCommand.Execute(null);

        Assert.Equal([TeamId, WellKnownGroups.Guest, WellKnownGroups.Admin], Assert.IsType<ReorderGroups>(Assert.Single(sent)).GroupIds);
        Assert.Equal(["Team", "Gast", "Admin"], Names(rig));
    }

    [Fact]
    public void MoveGroupUp_GroupSelected_SendsTheFullOrder()
    {
        var rig = Create();
        ServerGroups(rig, FakeServers.GuestGroup, Team, FakeServers.AdminGroup);
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
        ServerGroups(rig, FakeServers.GuestGroup, Team, FakeServers.AdminGroup);
        var folding = Row(rig, "Team");
        rig.Server.Leave.Delay = TimeSpan.FromMinutes(1);
        ServerGroups(rig, FakeServers.GuestGroup, FakeServers.AdminGroup);
        Assert.True(CollectionSync.IsLeaving(rig.Section.Groups, folding));

        Move(rig, folding, Row(rig, "Gast"), after: false);

        Assert.Empty(sent);
    }

    [Fact]
    public void MoveGroupAsync_WithoutTheManageRight_SendsNothing()
    {
        var rig = Create();
        FakeServers.SetOwnRightsAndGroups(rig.Server, Permission.All & ~Permission.GroupsManage, [WellKnownGroups.Admin]);

        Move(rig, Row(rig, "Admin"), Row(rig, "Gast"), after: false);

        Assert.Empty(sent);
    }

    [Fact]
    public void MoveGroupAsync_RefusedByTheServer_OrderGoesBackAndTheBusyMarkShowsAfterTheDelayThenEnds()
    {
        var rig = Create();
        Move(rig, Row(rig, "Admin"), Row(rig, "Gast"), after: false);
        time.Advance(PendingActionViewModel.ShowAfter);
        Assert.True(rig.Section.GroupReorder.IsBusy);

        rig.Server.Apply(new Error(sent[^1].RequestId, ReasonCodes.PermissionDenied));

        Assert.Equal(["Gast", "Admin"], Names(rig));
        Assert.False(rig.Section.GroupReorder.IsRunning);
        Assert.False(rig.Section.GroupReorder.IsBusy);
    }

    [Fact]
    public void MoveGroupAsync_GroupDroppedBeforeTheFirst_SendsTheFullOrderWithItFirst()
    {
        var rig = Create();
        ServerGroups(rig, FakeServers.GuestGroup, Team, FakeServers.AdminGroup);

        Move(rig, Row(rig, "Admin"), Row(rig, "Gast"), after: false);

        Assert.Equal([WellKnownGroups.Admin, WellKnownGroups.Guest, TeamId], Assert.IsType<ReorderGroups>(Assert.Single(sent)).GroupIds);
    }

    [Fact]
    public async Task MoveGroupAsync_TwoOrdersSent_OlderAnswerChangesNothingAndNewerRefusalBringsTheServerOrderBack()
    {
        var rig = Create();
        ServerGroups(rig, FakeServers.GuestGroup, Team, FakeServers.AdminGroup);
        Move(rig, Row(rig, "Admin"), Row(rig, "Gast"), after: false);
        var olderRequestId = sent[^1].RequestId;
        Move(rig, Row(rig, "Team"), Row(rig, "Admin"), after: false);
        var newerRequestId = sent[^1].RequestId;

        rig.Server.Apply(new Error(olderRequestId, ReasonCodes.PermissionDenied));
        Assert.Equal(["Team", "Admin", "Gast"], Names(rig));
        rig.Server.Apply(new Error(newerRequestId, ReasonCodes.PermissionDenied));

        // both orders wait on one completion: the later waiter, which puts the order back, is queued to the pool
        await AsyncWait.UntilAsync(() => Names(rig) is ["Gast", "Team", "Admin"]);
    }

    // ---- deleting ----

    [Fact]
    public void DeleteGroup_SavedGroupSelected_SendsItsDeletion()
    {
        var rig = Create();
        ServerGroups(rig, FakeServers.GuestGroup, Team, FakeServers.AdminGroup);
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
        Assert.True(await BoundAsync(rig.Section.Place.SaveAsync()));
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
        ServerGroups(rig, FakeServers.GuestGroup with { Name = "Neu" }, FakeServers.AdminGroup);
        Assert.True(await BoundAsync(saving));
        Assert.False(rig.Section.Place.HasChanges());
    }

    [Fact]
    public async Task Place_OtherGroupEdited_SavingSendsItAndSucceedsOnceConfirmed()
    {
        var rig = Create();
        ServerGroups(rig, FakeServers.GuestGroup, Team, FakeServers.AdminGroup);
        Row(rig, "Team").Name = "Neu";
        rig.Section.SelectedGroup = Row(rig, "Gast");
        Assert.True(rig.Section.Place.HasChanges());

        var saving = rig.Section.Place.SaveAsync();

        Assert.Equal(new UpdateGroup(TeamId, "Neu", Permission.Speak), Assert.Single(sent) with { RequestId = null });
        Assert.False(saving.IsCompleted);
        ServerGroups(rig, FakeServers.GuestGroup, Team with { Name = "Neu" }, FakeServers.AdminGroup);
        Assert.True(await BoundAsync(saving));
        Assert.False(rig.Section.Place.HasChanges());
    }

    /// <summary>
    /// The saving of several groups runs one after the other, each step answered by hand. A task whose only waiter is the
    /// place completes inside the answer (a task with two waiters posts its later continuation through the test context),
    /// so these facts are not async and read the end of the chain at once.
    /// </summary>
    static bool AssertFinished(Task<bool> saving)
    {
        Assert.True(saving.IsCompletedSuccessfully);
        return saving.Result;
    }

    /// <summary>The server has Gast, Team and Admin; Gast is renamed Neu and Team is renamed Zwei.</summary>
    static void EditTwoGroups(Rig rig)
    {
        ServerGroups(rig, FakeServers.GuestGroup, Team, FakeServers.AdminGroup);
        Row(rig, "Gast").Name = "Neu";
        Row(rig, "Team").Name = "Zwei";
    }

    static Task<bool> EditTwoGroupsAndSaveAsync(Rig rig)
    {
        EditTwoGroups(rig);
        return rig.Section.Place.SaveAsync();
    }

    [Fact]
    public void Place_TwoGroupsEdited_SendsTheSecondOnlyOnceTheFirstIsConfirmedAndSucceedsAfterBoth()
    {
        var rig = Create();

        var saving = EditTwoGroupsAndSaveAsync(rig);

        Assert.Equal(new UpdateGroup(WellKnownGroups.Guest, "Neu", Permission.Speak), Assert.Single(sent) with { RequestId = null });
        Assert.False(saving.IsCompleted);
        ServerGroups(rig, FakeServers.GuestGroup with { Name = "Neu" }, Team, FakeServers.AdminGroup);
        Assert.Equal(new UpdateGroup(TeamId, "Zwei", Permission.Speak), sent[^1] with { RequestId = null });
        Assert.Equal(2, sent.Count);
        Assert.False(saving.IsCompleted);
        ServerGroups(rig, FakeServers.GuestGroup with { Name = "Neu" }, Team with { Name = "Zwei" }, FakeServers.AdminGroup);
        Assert.True(AssertFinished(saving));
        Assert.False(rig.Section.Place.HasChanges());
    }

    [Fact]
    public void Place_SecondGroupRefused_FailsSelectsItAndItKeepsItsEdit()
    {
        var rig = Create();
        var saving = EditTwoGroupsAndSaveAsync(rig);
        ServerGroups(rig, FakeServers.GuestGroup with { Name = "Neu" }, Team, FakeServers.AdminGroup);

        rig.Server.Apply(new Error(sent[^1].RequestId, ReasonCodes.PermissionDenied));

        Assert.False(AssertFinished(saving));
        Assert.True(Row(rig, "Zwei").HasChanges);
        Assert.Same(Row(rig, "Zwei"), rig.Section.SelectedGroup);
        Assert.Equal(2, sent.Count);
    }

    [Fact]
    public void Place_FirstGroupRefused_FailsAndTheSecondIsNeverSentAndBothKeepTheirEdits()
    {
        var rig = Create();
        var saving = EditTwoGroupsAndSaveAsync(rig);

        rig.Server.Apply(new Error(sent[^1].RequestId, ReasonCodes.PermissionDenied));

        Assert.False(AssertFinished(saving));
        Assert.Single(sent);
        Assert.True(Row(rig, "Neu").HasChanges);
        Assert.True(Row(rig, "Zwei").HasChanges);
    }

    [Fact]
    public void Place_FirstGroupGetsNoAnswer_FailsAfterTheTimeoutAndTheSecondIsNeverSent()
    {
        var rig = Create();
        var saving = EditTwoGroupsAndSaveAsync(rig);

        time.Advance(PendingActionViewModel.Timeout);

        Assert.False(AssertFinished(saving));
        Assert.Single(sent);
        Assert.True(Row(rig, "Zwei").HasChanges);
    }

    [Fact]
    public void Place_SavedAgainAfterATimeout_OneAnswerConfirmsOnlyTheFirstGroup()
    {
        var rig = Create();
        var timedOut = EditTwoGroupsAndSaveAsync(rig);
        time.Advance(PendingActionViewModel.Timeout);
        Assert.False(AssertFinished(timedOut));

        var again = rig.Section.Place.SaveAsync();
        ServerGroups(rig, FakeServers.GuestGroup with { Name = "Neu" }, Team, FakeServers.AdminGroup);

        Assert.Equal(new UpdateGroup(TeamId, "Zwei", Permission.Speak), sent[^1] with { RequestId = null });
        Assert.Equal(3, sent.Count);
        Assert.False(again.IsCompleted);
    }

    [Fact]
    public void Place_GroupRebuiltWhileOnItsWayThenRefused_FailsAndSelectsTheRowThatIsInTheList()
    {
        var rig = Create();
        ServerGroups(rig, FakeServers.GuestGroup, Team, FakeServers.AdminGroup);
        Row(rig, "Gast").Name = "Neu";
        rig.Section.SelectedGroup = Row(rig, "Team");
        var saving = rig.Section.Place.SaveAsync();
        FakeServers.SetOwnRightsAndGroups(rig.Server, Permission.GroupsView | Permission.GroupsManage | Permission.Speak, []);

        rig.Server.Apply(new Error(sent[^1].RequestId, ReasonCodes.PermissionDenied));

        Assert.False(AssertFinished(saving));
        Assert.Contains(rig.Section.SelectedGroup, rig.Section.Groups.Live());
        Assert.Equal("Neu", rig.Section.SelectedGroup?.Name);
    }

    [Fact]
    public void Place_SecondGroupNameBlank_FailsSelectsItWithTheNameErrorAfterTheFirstWasSaved()
    {
        var rig = Create();
        ServerGroups(rig, FakeServers.GuestGroup, Team, FakeServers.AdminGroup);
        Row(rig, "Gast").Name = "Neu";
        Row(rig, "Team").Name = "   ";

        var saving = rig.Section.Place.SaveAsync();
        ServerGroups(rig, FakeServers.GuestGroup with { Name = "Neu" }, Team, FakeServers.AdminGroup);

        Assert.False(AssertFinished(saving));
        Assert.Single(sent);
        Assert.False(Row(rig, "Neu").HasChanges);
        Assert.Same(Row(rig, "   "), rig.Section.SelectedGroup);
        Assert.Equal(Strings.Dlg_NameInvalid, rig.Section.GroupError);
    }

    [Fact]
    public void Place_RightsLostWhileTheFirstGroupIsOnItsWay_TheRebuiltSecondGroupIsNotSentStale()
    {
        var rig = Create();
        var saving = EditTwoGroupsAndSaveAsync(rig);
        FakeServers.SetOwnRightsAndGroups(rig.Server, Permission.GroupsView, [WellKnownGroups.Admin]);

        ServerGroups(rig, FakeServers.GuestGroup with { Name = "Neu" }, Team, FakeServers.AdminGroup);

        Assert.Single(sent);
        Assert.True(AssertFinished(saving));
        Assert.False(rig.Section.Place.HasChanges());
    }

    [Fact]
    public void Place_DraftAndEditedSavedGroup_SendsTheUpdateThenTheCreateAndSucceedsAfterBoth()
    {
        var rig = Create();
        Row(rig, "Gast").Name = "Neu";
        rig.Section.NewGroupCommand.Execute(null);
        rig.Section.SelectedGroup!.Name = "Team";

        var saving = rig.Section.Place.SaveAsync();

        Assert.Equal(new UpdateGroup(WellKnownGroups.Guest, "Neu", Permission.Speak), Assert.Single(sent) with { RequestId = null });
        ServerGroups(rig, FakeServers.GuestGroup with { Name = "Neu" }, FakeServers.AdminGroup);
        Assert.Equal(new CreateGroup("Team", Permission.Speak), sent[^1] with { RequestId = null });
        Assert.False(saving.IsCompleted);
        ServerGroups(rig, FakeServers.GuestGroup with { Name = "Neu" }, Team, FakeServers.AdminGroup);
        Assert.True(AssertFinished(saving));
        Assert.DoesNotContain(rig.Section.Groups.Live(), row => row.Id is null);
        Assert.False(rig.Section.Place.HasChanges());
    }

    [Fact]
    public void SaveGroup_OtherGroupEditedToo_SendsOnlyTheSelectedGroup()
    {
        var rig = Create();
        EditTwoGroups(rig);
        rig.Section.SelectedGroup = Row(rig, "Zwei");

        rig.Section.SaveGroupCommand.Execute(null);

        Assert.Equal(new UpdateGroup(TeamId, "Zwei", Permission.Speak), Assert.Single(sent) with { RequestId = null });
        Assert.True(Row(rig, "Neu").HasChanges);
    }

    [Fact]
    public async Task Place_GroupSaveOnItsWay_SavingWaitsForItAndSendsNoSecondOne()
    {
        var rig = Create();
        Row(rig, "Gast").Name = "Neu";
        rig.Section.SaveGroupCommand.Execute(null);

        var saving = rig.Section.Place.SaveAsync();

        Assert.False(saving.IsCompleted);
        ServerGroups(rig, FakeServers.GuestGroup with { Name = "Neu" }, FakeServers.AdminGroup);
        Assert.True(await BoundAsync(saving));
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

    sealed record Rig(GroupListViewModel Section, ServerViewModel Server);
}
