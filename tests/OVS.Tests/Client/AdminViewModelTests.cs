using OVS.Client.Net;
using OVS.Client.ViewModels;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;
using OVS.Tests.TestSupport;
using P = OVS.Shared.Permissions.Permission;

namespace OVS.Tests.Client;

public class AdminViewModelTests
{
    static readonly Guid Lobby = Guid.NewGuid();
    static readonly Guid ModGroup = Guid.NewGuid();

    static (AdminViewModel Admin, ServerViewModel Server, List<Request> Sent) Create(P selfPerms, bool hasPassword = false, ServerLimits? limits = null,
        Dialogs? dialogs = null)
    {
        var snapshot = new ServerSnapshot(new ServerSettingsInfo("Server", "Hallo", hasPassword, null, limits), Lobby,
            [new ChannelInfo(Lobby, "Lobby", "", 0)], [],
            [
                new GroupInfo(WellKnownGroups.Guest, "Gast", P.Speak),
                new GroupInfo(ModGroup, "Moderator", P.Speak | P.UserKick),
                new GroupInfo(WellKnownGroups.Admin, "Admin", P.All),
            ],
            [new UserInfo(1, "fp1", "ich", Lobby, false, false, false, selfPerms, [])]);
        var sent = new List<Request>();
        var server = new ServerViewModel(new StateMirror(new Welcome(1, "", snapshot)), r =>
        {
            sent.Add(r);
            return Task.CompletedTask;
        }, new ManualTimeProvider(), dialogs);
        return (new AdminViewModel(server), server, sent);
    }

    static GroupEditViewModel Group(AdminViewModel vm, string name) => vm.Groups.Single(g => g.Name == name);

    [Fact]
    public void GroupEditor_DisablesUnownedPermissions()
    {
        var (vm, _, _) = Create(P.GroupsManage | P.GroupsDelete | P.Speak | P.UserKick);
        var mod = Group(vm, "Moderator");
        Assert.True(mod.Toggles.Single(t => t.Permission == P.UserKick).IsEnabled);
        Assert.False(mod.Toggles.Single(t => t.Permission == P.ServerConfig).IsEnabled);
        Assert.Equal(P.Speak | P.UserKick, mod.Permissions);
        Assert.True(mod.CanDelete);
    }

    [Fact]
    public void AdminGroup_ReadOnly_ProtectedGroupsNotDeletable()
    {
        var (vm, _, _) = Create(P.All);
        var admin = Group(vm, "Admin");
        Assert.True(admin.IsReadOnly);
        Assert.All(admin.Toggles, t => Assert.False(t.IsEnabled));
        Assert.False(admin.CanDelete);
        Assert.False(Group(vm, "Gast").CanDelete);
    }

    [Fact]
    public void GroupButtons_DisabledForProtectedAndStrongerGroups()
    {
        var (vm, _, _) = Create(P.GroupsManage | P.Speak);
        vm.SelectedGroup = Group(vm, "Admin");
        Assert.False(vm.SaveGroupCommand.CanExecute(null));
        Assert.False(vm.DeleteGroupCommand.CanExecute(null));

        vm.SelectedGroup = Group(vm, "Gast");
        Assert.True(vm.SaveGroupCommand.CanExecute(null));
        Assert.False(vm.DeleteGroupCommand.CanExecute(null));

        // Moderator can kick, the actor cannot: not editable, not deletable
        vm.SelectedGroup = Group(vm, "Moderator");
        Assert.True(vm.SelectedGroup.IsReadOnly);
        Assert.False(vm.SaveGroupCommand.CanExecute(null));
        Assert.False(vm.DeleteGroupCommand.CanExecute(null));
    }

    [Fact]
    public void BanDurations_AsPlanned()
    {
        Assert.Equal([("1 Stunde", (int?)60), ("1 Tag", 1440), ("7 Tage", 10080), ("Dauerhaft", null)], BanChoice.Durations);
    }

    /// <summary>Package 76: every tab has its own view right; acting rights alone open nothing.</summary>
    [Theory]
    [InlineData(P.GroupsView, true, false, false, false)]
    [InlineData(P.UsersView, false, true, false, false)]
    [InlineData(P.BansView, false, false, true, false)]
    [InlineData(P.ServerConfig, false, false, false, true)]
    [InlineData(P.GroupsAssign, false, false, false, false)]
    [InlineData(P.GroupsManage | P.GroupsCreate | P.GroupsDelete | P.UserBan | P.UserKick | P.UserDelete, false, false, false, false)]
    public void Tabs_VisibleByPermission(P perms, bool groups, bool users, bool bans, bool server)
    {
        var (vm, serverVm, _) = Create(perms);
        Assert.Equal((groups, users, bans, server), (vm.ShowGroups, vm.ShowUsers, vm.ShowBans, vm.ShowServer));
        Assert.Equal(groups || users || bans || server, serverVm.CanAdminister);
    }

    /// <summary>Package 76 (AC4): without the acting right the overview stays readable, only the action is locked.</summary>
    [Fact]
    public void GroupButtons_ByRight()
    {
        var (view, _, _) = Create(P.GroupsView | P.Speak);
        view.SelectedGroup = Group(view, "Gast");
        Assert.False(view.NewGroupCommand.CanExecute(null));
        Assert.False(view.SaveGroupCommand.CanExecute(null));
        Assert.False(view.DeleteGroupCommand.CanExecute(null));
        Assert.False(view.MoveGroupDownCommand.CanExecute(null));
        Assert.True(view.SelectedGroup.IsReadOnly);
        Assert.All(view.SelectedGroup.Toggles, t => Assert.False(t.IsEnabled));

        var (create, _, _) = Create(P.GroupsView | P.GroupsCreate | P.Speak);
        Assert.True(create.NewGroupCommand.CanExecute(null));
        create.SelectedGroup = Group(create, "Gast");
        Assert.False(create.SaveGroupCommand.CanExecute(null));
        create.NewGroupCommand.Execute(null); // a new group can be saved (created) and dropped again
        Assert.True(create.SaveGroupCommand.CanExecute(null));
        Assert.True(create.DeleteGroupCommand.CanExecute(null));

        var (edit, _, _) = Create(P.GroupsView | P.GroupsManage | P.Speak);
        edit.SelectedGroup = Group(edit, "Gast");
        Assert.False(edit.NewGroupCommand.CanExecute(null));
        Assert.True(edit.SaveGroupCommand.CanExecute(null));
        Assert.True(edit.MoveGroupDownCommand.CanExecute(null));
        Assert.False(edit.DeleteGroupCommand.CanExecute(null));

        var (delete, _, _) = Create(P.GroupsView | P.GroupsDelete | P.Speak | P.UserKick);
        delete.SelectedGroup = Group(delete, "Moderator");
        Assert.True(delete.DeleteGroupCommand.CanExecute(null));
        Assert.False(delete.SaveGroupCommand.CanExecute(null));
    }

    [Fact]
    public void UserTogglesAndUnban_ByRight()
    {
        var (view, server, _) = Create(P.UsersView | P.BansView | P.Speak);
        server.Apply(new UserList("r", [new KnownUserInfo("fpX", "Xaver", [WellKnownGroups.Guest])]));
        Assert.All(Assert.Single(view.Users).Toggles, t => Assert.False(t.IsEnabled));
        server.Apply(new BanList("b", [new BanInfo(Guid.NewGuid(), "fpX", "Xaver", null, "", "mod", null)]));
        Assert.False(Assert.Single(view.Bans).UnbanCommand.CanExecute(null));

        var (act, actServer, _) = Create(P.UsersView | P.BansView | P.GroupsAssign | P.UserBan | P.Speak);
        actServer.Apply(new UserList("r", [new KnownUserInfo("fpX", "Xaver", [WellKnownGroups.Guest])]));
        Assert.True(Assert.Single(act.Users).Toggles.Single(t => t.Name == "Gast").IsEnabled);
        actServer.Apply(new BanList("b", [new BanInfo(Guid.NewGuid(), "fpX", "Xaver", null, "", "mod", null)]));
        Assert.True(Assert.Single(act.Bans).UnbanCommand.CanExecute(null));
    }

    /// <summary>Package 76 (AC1, AC7): every right is a checkbox, named in both languages.</summary>
    [Theory]
    [InlineData("de-DE", "Nutzerübersicht sehen", "Bans sehen", "Gruppen sehen", "Gruppen anlegen", "Gruppen löschen", "Nutzer löschen", "Gruppen bearbeiten", "Nutzer bannen und entbannen")]
    [InlineData("en-US", "View user overview", "View bans", "View groups", "Create groups", "Delete groups", "Delete users", "Edit groups", "Ban and unban users")]
    public void GroupEditor_ListsNewRights(string culture, string users, string bans, string groups, string create, string delete,
        string userDelete, string manage, string ban)
    {
        var labels = TestCulture.With(culture, () => Create(P.All).Admin.Groups.First().Toggles.ToDictionary(t => t.Permission, t => t.Label));
        var single = Enum.GetValues<P>().Where(p => p != P.None && ((int)p & ((int)p - 1)) == 0).ToList();
        Assert.Equal(single.Order(), labels.Keys.Order());
        Assert.Equal(labels.Count, labels.Values.Distinct().Count());
        Assert.Equal([users, bans, groups, create, delete, userDelete, manage, ban],
            new[] { P.UsersView, P.BansView, P.GroupsView, P.GroupsCreate, P.GroupsDelete, P.UserDelete, P.GroupsManage, P.UserBan }.Select(p => labels[p]));
    }

    [Fact]
    public async Task RequestLists_OnlyAllowedOnes()
    {
        var (vm, _, sent) = Create(P.BansView | P.GroupsAssign);
        await vm.RequestListsAsync();
        Assert.IsType<ListBans>(Assert.Single(sent));
    }

    [Fact]
    public async Task AssignGroup_SendsRequest_DisablesHigherGroups()
    {
        var (vm, server, sent) = Create(P.GroupsAssign | P.Speak);
        server.Apply(new UserList("r", [new KnownUserInfo("fpX", "Xaver", [WellKnownGroups.Guest])]));

        var user = Assert.Single(vm.Users);
        var guest = user.Toggles.Single(t => t.Name == "Gast");
        Assert.True(guest.IsChecked);
        Assert.True(guest.IsEnabled);
        Assert.False(user.Toggles.Single(t => t.Name == "Admin").IsEnabled);
        Assert.False(user.Toggles.Single(t => t.Name == "Moderator").IsEnabled);

        guest.IsChecked = false; // what the checkbox does before running its command
        await guest.ToggleCommand.ExecuteAsync(null);
        Assert.Equal(new UnassignGroup("fpX", WellKnownGroups.Guest), sent[^2] with { RequestId = null });
        Assert.IsType<ListUsers>(sent[^1]);
    }

    /// <summary>Package 73: the one acting hears a tone once the next user list shows the change, also offline; an error gives none.</summary>
    [Fact]
    public async Task AssignGroup_SoundWhenListConfirms_NotOnError()
    {
        var (vm, server, sent) = Create(P.GroupsAssign | P.Speak | P.UserKick);
        var heard = new List<OVS.Client.Audio.SoundEvent>();
        server.SoundRequested += heard.Add;
        server.Apply(new UserList("r", [new KnownUserInfo("fpX", "Xaver", [WellKnownGroups.Guest])]));

        var mod = Assert.Single(vm.Users).Toggles.Single(t => t.Name == "Moderator");
        mod.IsChecked = true;
        await mod.ToggleCommand.ExecuteAsync(null);
        server.Apply(new UserList("r", [new KnownUserInfo("fpX", "Xaver", [WellKnownGroups.Guest])])); // an older answer: not yet
        Assert.Empty(heard);
        server.Apply(new UserList("r", [new KnownUserInfo("fpX", "Xaver", [WellKnownGroups.Guest, ModGroup])]));
        Assert.Equal([OVS.Client.Audio.SoundEvent.GroupChangedByMe], heard);
        server.Apply(new UserList("r", [new KnownUserInfo("fpX", "Xaver", [WellKnownGroups.Guest, ModGroup])])); // only once
        Assert.Single(heard);

        var guest = Assert.Single(vm.Users).Toggles.Single(t => t.Name == "Gast");
        guest.IsChecked = false;
        await guest.ToggleCommand.ExecuteAsync(null);
        server.Apply(new Error(sent[^2].RequestId, Codes.LastAdmin));
        server.Apply(new UserList("r", [new KnownUserInfo("fpX", "Xaver", [ModGroup])])); // even if it changed some other way
        Assert.Single(heard);
    }

    [Fact]
    public void BanList_ShowsBans_UnbanSends()
    {
        var (vm, server, sent) = Create(P.BansView | P.UserBan);
        var ban = new BanInfo(Guid.NewGuid(), "abcdef0123456789", "troll", "1.2.3.4", "spam", "mod", null);
        server.Apply(new BanList("r", [ban]));
        var entry = Assert.Single(vm.Bans);
        Assert.Contains("troll", entry.Text);
        Assert.Contains("dauerhaft", entry.Text);
        entry.UnbanCommand.Execute(null);
        Assert.Equal(new Unban(ban.Id), sent[^1] with { RequestId = null });
    }

    [Fact]
    public async Task NewGroup_SaveSendsCreate()
    {
        var (vm, _, sent) = Create(P.All);
        vm.NewGroupCommand.Execute(null);
        vm.SelectedGroup!.Name = "Team";
        vm.SelectedGroup.Toggles.Single(t => t.Permission == P.SpeakLinked).IsChecked = true;
        await vm.SaveGroupCommand.ExecuteAsync(null);
        Assert.Equal(new CreateGroup("Team", P.Speak | P.SpeakLinked), sent[^1] with { RequestId = null });
    }

    [Theory]
    [InlineData("", false, null)]
    [InlineData("neu", false, "neu")]
    [InlineData("egal", true, "")]
    public async Task SaveServerSettings_PasswordSemantics(string newPassword, bool remove, string? expected)
    {
        var (vm, _, sent) = Create(P.ServerConfig, hasPassword: true);
        vm.ServerName = "Neu";
        vm.NewPassword = newPassword;
        vm.RemovePassword = remove;
        await vm.SaveServerSettingsCommand.ExecuteAsync(null);
        Assert.Equal(new UpdateServerSettings("Neu", "Hallo", expected), sent[^1] with { RequestId = null });
    }

    /// <summary>Package 69: the limits travel with name, welcome text and password.</summary>
    [Fact]
    public async Task SaveServerSettings_SendsAllFields()
    {
        var (vm, server, sent) = Create(P.ServerConfig, limits: new ServerLimits(50, 30, true, false, new TimeOnly(4, 0)));
        Assert.Equal((50m, 30m, true, false, new TimeSpan(4, 0, 0)), (vm.MaxUsers, vm.LogDays, vm.LogRotateDaily, vm.AutoRestart, vm.AutoRestartTime));

        vm.MaxUsers = 12;
        vm.LogDays = 0;
        vm.LogRotateDaily = false;
        vm.AutoRestart = true;
        vm.AutoRestartTime = new TimeSpan(3, 30, 0);
        await vm.SaveServerSettingsCommand.ExecuteAsync(null);
        Assert.Equal(new UpdateServerSettings("Server", "Hallo", null, new ServerLimits(12, 0, false, true, new TimeOnly(3, 30))),
            sent[^1] with { RequestId = null });
    }

    [Fact]
    public async Task SaveServerSettings_LimitsUnknown_LeftUnchanged()
    {
        var (vm, server, sent) = Create(P.ServerConfig);
        await vm.SaveServerSettingsCommand.ExecuteAsync(null);
        Assert.Null(((UpdateServerSettings)sent[^1]).Limits);

        // they arrive later, e.g. after the right was granted
        server.Apply(new ServerSettingsChanged(new ServerSettingsInfo("Server", "Hallo", false, null, new ServerLimits(8, 1, true, true, new TimeOnly(2, 0)))));
        Assert.Equal(8m, vm.MaxUsers);
        Assert.Equal(new TimeSpan(2, 0, 0), vm.AutoRestartTime);
    }

    // ---- Package 37: group order ----

    [Fact]
    public async Task MoveGroupUpDown_SendsFullOrder()
    {
        var (vm, _, sent) = Create(P.All);
        vm.SelectedGroup = Group(vm, "Gast");
        Assert.False(vm.MoveGroupUpCommand.CanExecute(null));
        await vm.MoveGroupDownCommand.ExecuteAsync(null);
        Assert.Equal(new[] { ModGroup, WellKnownGroups.Guest, WellKnownGroups.Admin }, ((ReorderGroups)sent[^1]).GroupIds);

        vm.SelectedGroup = Group(vm, "Admin");
        Assert.False(vm.MoveGroupDownCommand.CanExecute(null));
        await vm.MoveGroupUpCommand.ExecuteAsync(null);
        Assert.Equal(new[] { WellKnownGroups.Guest, WellKnownGroups.Admin, ModGroup }, ((ReorderGroups)sent[^1]).GroupIds);
    }

    [Fact]
    public async Task DropGroup_SendsOrder_IgnoresUnsaved()
    {
        var (vm, _, sent) = Create(P.All);
        await vm.MoveGroupAsync(Group(vm, "Admin"), Group(vm, "Gast"), after: false);
        Assert.Equal(new[] { WellKnownGroups.Admin, WellKnownGroups.Guest, ModGroup }, ((ReorderGroups)sent[^1]).GroupIds);

        vm.NewGroupCommand.Execute(null); // not saved yet: cannot be moved, is not in the order
        var count = sent.Count;
        await vm.MoveGroupAsync(vm.SelectedGroup!, Group(vm, "Gast"), after: false);
        Assert.False(vm.MoveGroupUpCommand.CanExecute(null));
        Assert.Equal(count, sent.Count);
    }

    // ---- Package 71: user overview ----

    static readonly DateTimeOffset T0 = new(2026, 9, 1, 18, 30, 0, TimeSpan.Zero);

    static KnownUserInfo Known(string fp, string nick, Guid[]? groups = null, DateTimeOffset? lastLogin = null, int logins = 3,
        TimeSpan? online = null, string? ip = "10.0.0.7", string[]? previous = null, BanInfo[]? bans = null) =>
        new(fp, nick, groups ?? [WellKnownGroups.Guest], T0.AddDays(-30), lastLogin ?? T0, logins, online ?? TimeSpan.FromMinutes(90), ip,
            previous ?? [], TimeSpan.FromSeconds(75), 12, false, null, bans);

    static UserInfo Online(uint id, string fp, string nick) => new(id, fp, nick, Lobby, false, false, false, P.Speak, [WellKnownGroups.Guest]);

    [Fact]
    public void UserDetails_FromKnownUserInfo()
    {
        var (vm, server, _) = Create(P.UsersView | P.GroupsAssign | P.Speak);
        var ban = new BanInfo(Guid.NewGuid(), "fpX", "Xaver", null, "Spam", "mod", T0.AddDays(2));
        server.Apply(new UserJoined(Online(2, "fpX0123456789abcdef", "Xaver")));
        server.Apply(new UserList("r",
        [
            Known("fpX0123456789abcdef", "Xaver", [WellKnownGroups.Guest, ModGroup], previous: ["Xav", "X"], bans: [ban]),
            new KnownUserInfo("fpOld", "Otto", [WellKnownGroups.Guest]), // saved before Package 70
        ]));

        var x = vm.Users.Single(u => u.Nickname == "Xaver");
        Assert.True(x.IsOnline);
        Assert.Equal(["Gast", "Moderator"], x.Toggles.Where(t => t.IsChecked).Select(t => t.Name));
        Assert.Equal(T0.AddDays(-30).ToLocalTime().ToString("g"), x.FirstSeenText);
        Assert.Equal(T0.ToLocalTime().ToString("g"), x.LastLoginText);
        Assert.Equal("3", x.LoginCountText);
        Assert.Equal("1 h 30 min", x.OnlineTimeText);
        Assert.Equal("1 min 15 s", x.SpeechTimeText);
        Assert.Equal("12", x.ChatMessagesText);
        Assert.Equal("10.0.0.7", x.LastIpText);
        Assert.Equal("Xav, X", x.PreviousNicknamesText);
        Assert.Equal("fpX0123456789abc", x.ShortFingerprint);
        Assert.Equal("fpX0123456789abcdef", x.Fingerprint);
        Assert.True(x.IsBanned);
        Assert.Contains("Spam", x.BanText);
        Assert.Contains(T0.AddDays(2).ToLocalTime().ToString("dd.MM.yyyy HH:mm"), x.BanText);

        var otto = vm.Users.Single(u => u.Nickname == "Otto");
        Assert.False(otto.IsOnline);
        Assert.False(otto.IsBanned);
        Assert.All(new[] { otto.FirstSeenText, otto.LastLoginText, otto.LoginCountText, otto.OnlineTimeText, otto.SpeechTimeText,
            otto.ChatMessagesText, otto.LastIpText, otto.PreviousNicknamesText }, t => Assert.Equal("unbekannt", t));
    }

    [Theory]
    [InlineData("xav", "Xaver")]
    [InlineData("ALTNAME", "Xaver")]
    [InlineData("ab12", "Berta")]
    [InlineData("192.168.", "Berta")]
    [InlineData("", "Berta,Xaver")]
    [InlineData("gibtsnicht", "")]
    public void Search_MatchesNicknamePreviousFingerprintIp(string search, string expected)
    {
        var (vm, server, _) = Create(P.UsersView);
        server.Apply(new UserList("r",
        [
            Known("ffff0000", "Xaver", previous: ["Altname"], ip: "10.0.0.7"),
            Known("00AB12cd", "Berta", ip: "192.168.1.20"),
        ]));
        vm.SearchText = search;
        Assert.Equal(expected, string.Join(",", vm.Users.Select(u => u.Nickname)));
    }

    [Fact]
    public void Filter_StatusGroup_Sort_Count()
    {
        var (vm, server, _) = Create(P.UsersView);
        var ban = new BanInfo(Guid.NewGuid(), "fpC", "Cora", null, "", "mod", null);
        server.Apply(new UserJoined(Online(2, "fpB", "Bert")));
        server.Apply(new UserList("r",
        [
            Known("fpA", "anton", lastLogin: T0.AddDays(-1), online: TimeSpan.FromHours(5)),
            Known("fpB", "Bert", [WellKnownGroups.Guest, ModGroup], lastLogin: T0, online: TimeSpan.FromHours(1)),
            Known("fpC", "Cora", lastLogin: T0.AddDays(-5), online: TimeSpan.FromHours(9), bans: [ban]),
            new KnownUserInfo("fpD", "Dora", [WellKnownGroups.Guest]),
        ]));
        string Names() => string.Join(",", vm.Users.Select(u => u.Nickname));

        Assert.Equal("anton,Bert,Cora,Dora", Names()); // by name, case-insensitive
        Assert.Equal("4 von 4 Nutzern", vm.UserCountText);

        vm.SelectedStatusFilter = vm.StatusFilters.Single(f => f.Value == UserStatusFilter.Online);
        Assert.Equal("Bert", Names());
        vm.SelectedStatusFilter = vm.StatusFilters.Single(f => f.Value == UserStatusFilter.Offline);
        Assert.Equal("anton,Cora,Dora", Names());
        vm.SelectedStatusFilter = vm.StatusFilters.Single(f => f.Value == UserStatusFilter.Banned);
        Assert.Equal("Cora", Names());
        Assert.Equal("1 von 4 Nutzern", vm.UserCountText);
        vm.SelectedStatusFilter = vm.StatusFilters[0];

        Assert.Equal(["Alle Gruppen", "Gast", "Moderator", "Admin"], vm.GroupFilters.Select(f => f.Label));
        vm.SelectedGroupFilter = vm.GroupFilters.Single(f => f.Label == "Moderator");
        Assert.Equal("Bert", Names());
        vm.SelectedGroupFilter = vm.GroupFilters[0];

        vm.SelectedSortOrder = vm.SortOrders.Single(s => s.Value == UserSortOrder.LastLogin);
        Assert.Equal("Bert,anton,Cora,Dora", Names()); // newest first, never logged in last
        vm.SelectedSortOrder = vm.SortOrders.Single(s => s.Value == UserSortOrder.OnlineTime);
        Assert.Equal("Cora,anton,Bert,Dora", Names());
    }

    [Fact]
    public void UsersViewOnly_SearchWorks_TogglesLocked()
    {
        var (vm, server, _) = Create(P.UsersView);
        Assert.True(vm.ShowUsers);
        server.Apply(new UserList("r", [Known("fpA", "Anton"), Known("fpB", "Berta")]));
        vm.SearchText = "ber";
        var berta = Assert.Single(vm.Users);
        Assert.Equal("10.0.0.7", berta.LastIpText);
        Assert.All(berta.Toggles, t => Assert.False(t.IsEnabled));
        Assert.True(berta.Toggles.Single(t => t.Name == "Gast").IsChecked);
    }

    [Fact]
    public async Task UserJoins_ListRefreshed_FilterKept()
    {
        var (vm, server, sent) = Create(P.UsersView);
        var time = (ManualTimeProvider)server.Time;
        server.Apply(new UserList("r", [Known("fpA", "Anton"), Known("fpB", "Berta")]));
        vm.SearchText = "ber";
        vm.SelectedStatusFilter = vm.StatusFilters.Single(f => f.Value == UserStatusFilter.Online);
        Assert.Empty(vm.Users);

        server.Apply(new UserJoined(Online(2, "fpB", "Berta")));
        Assert.Equal("Berta", Assert.Single(vm.Users).Nickname); // online at once, from the live state
        Assert.Single(sent.OfType<ListUsers>()); // and the stored data is asked for again

        server.Apply(new UserJoined(Online(3, "fpC", "Carl")));
        server.Apply(new UserLeft(2));
        Assert.Single(sent.OfType<ListUsers>()); // at most once per second
        Assert.Empty(vm.Users);
        time.Advance(TimeSpan.FromSeconds(1));
        for (var i = 0; i < 100 && sent.OfType<ListUsers>().Count() < 2; i++) await Task.Delay(10);
        Assert.Equal(2, sent.OfType<ListUsers>().Count());

        server.Apply(new UserList("r", [Known("fpA", "Anton"), Known("fpB", "Berta"), Known("fpC", "Carla")]));
        Assert.Equal("ber", vm.SearchText);
        Assert.Equal(UserStatusFilter.Online, vm.SelectedStatusFilter.Value);
        Assert.Empty(vm.Users);
        vm.SelectedStatusFilter = vm.StatusFilters[0];
        Assert.Equal("Berta", Assert.Single(vm.Users).Nickname);
    }

    // ---- Package 72: ban, unban and delete from the user card ----

    [Fact]
    public async Task DeleteUser_AsksFirst_SendsOnlyAfterConfirm()
    {
        var answer = false;
        string? asked = null;
        var dialogs = new Dialogs
        {
            ConfirmDeleteUser = name =>
            {
                asked = name;
                return Task.FromResult(answer);
            },
        };
        var (vm, server, sent) = Create(P.UsersView | P.BansView | P.UserDelete | P.Speak, dialogs: dialogs);
        server.Apply(new UserList("r", [Known("fpX", "Xaver")]));
        var user = Assert.Single(vm.Users);

        await user.DeleteCommand.ExecuteAsync(null);
        Assert.Equal("Xaver", asked);
        Assert.Empty(sent.OfType<DeleteUser>());

        answer = true;
        await user.DeleteCommand.ExecuteAsync(null);
        var index = sent.FindIndex(r => r is DeleteUser);
        Assert.Equal(new DeleteUser("fpX"), sent[index] with { RequestId = null });
        Assert.Equal([typeof(ListUsers), typeof(ListBans)], sent.Skip(index + 1).Select(r => r.GetType())); // both tabs follow
    }

    [Fact]
    public async Task BanUnbanDeleteButtons_ByStateAndRight()
    {
        var ban1 = new BanInfo(Guid.NewGuid(), "fpB", "Bert", null, "", "mod", null);
        var ban2 = new BanInfo(Guid.NewGuid(), "fpB", "Bert", "10.0.0.7", "", "mod", T0.AddDays(3));
        KnownUserInfo[] users =
        [
            Known("fpX", "Xaver"),
            Known("fpB", "Bert", bans: [ban1, ban2]),
            Known("fpM", "Mod", [WellKnownGroups.Guest, ModGroup]),
            Known("fpA", "Anna", [WellKnownGroups.Admin]),
            Known("fp1", "ich", [ModGroup]),
            new KnownUserInfo("fpOld", "Otto", [WellKnownGroups.Guest]),
        ];
        static (bool Ban, bool Unban, bool Delete) Buttons(AdminViewModel vm, string nick)
        {
            var u = vm.Users.Single(u => u.Nickname == nick);
            return (u.CanBan, u.CanUnban, u.CanDelete);
        }

        var (view, viewServer, _) = Create(P.UsersView | P.Speak | P.UserKick);
        viewServer.Apply(new UserList("r", users));
        Assert.All(view.Users, u => Assert.Equal((false, false, false), (u.CanBan, u.CanUnban, u.CanDelete)));

        var (banOnly, banServer, _) = Create(P.UsersView | P.UserBan | P.Speak | P.UserKick);
        banServer.Apply(new UserList("r", users));
        Assert.Equal((true, false, false), Buttons(banOnly, "Xaver"));

        BanChoice? choice = new("spam", 60, true);
        bool? ipOffered = null;
        var dialogs = new Dialogs
        {
            Ban = (_, ipKnown) =>
            {
                ipOffered = ipKnown;
                return Task.FromResult<BanChoice?>(choice);
            },
        };
        var (vm, server, sent) = Create(P.UsersView | P.UserBan | P.UserDelete | P.Speak | P.UserKick, dialogs: dialogs);
        server.Apply(new UserList("r", users));
        Assert.Equal((true, false, true), Buttons(vm, "Xaver"));
        Assert.Equal((false, true, true), Buttons(vm, "Bert"));   // banned: unban instead of ban
        Assert.Equal((true, false, true), Buttons(vm, "Mod"));    // Speak and UserKick are a subset
        Assert.Equal((false, false, false), Buttons(vm, "Anna")); // stronger (Admin)
        Assert.Equal((false, false, false), Buttons(vm, "ich"));  // never oneself

        await vm.Users.Single(u => u.Nickname == "Xaver").BanCommand.ExecuteAsync(null);
        Assert.True(ipOffered);
        Assert.Equal(new BanUser("fpX", "spam", 60, true), sent.OfType<BanUser>().Last() with { RequestId = null });
        Assert.IsType<ListUsers>(sent[^1]);

        await vm.Users.Single(u => u.Nickname == "Otto").BanCommand.ExecuteAsync(null); // no IP known: the option is locked
        Assert.False(ipOffered);
        Assert.Equal(new BanUser("fpOld", "spam", 60, false), sent.OfType<BanUser>().Last() with { RequestId = null });

        await vm.Users.Single(u => u.Nickname == "Bert").UnbanCommand.ExecuteAsync(null);
        Assert.Equal([ban1.Id, ban2.Id], sent.OfType<Unban>().Select(u => u.BanId));

        choice = null; // cancelled
        var count = sent.Count;
        await vm.Users.Single(u => u.Nickname == "Xaver").BanCommand.ExecuteAsync(null);
        Assert.Equal(count, sent.Count);
    }

    // ---- Package 38: link matrix ----

    static (LinkMatrixViewModel Matrix, ServerViewModel Server, List<Request> Sent, Guid[] Channels) Matrix(int channelCount, P perms = P.All)
    {
        var ids = Enumerable.Range(0, channelCount).Select(_ => Guid.NewGuid()).ToArray();
        var snapshot = new ServerSnapshot(new ServerSettingsInfo("Server", "", false), ids[0],
            ids.Select((id, i) => new ChannelInfo(id, $"K{i + 1}", "", i)).ToList(), [],
            [new GroupInfo(WellKnownGroups.Guest, "Gast", P.Speak)],
            [new UserInfo(1, "fp1", "ich", ids[0], false, false, false, perms, [])]);
        var sent = new List<Request>();
        var server = new ServerViewModel(new StateMirror(new Welcome(1, "", snapshot)), r =>
        {
            sent.Add(r);
            return Task.CompletedTask;
        }, new ManualTimeProvider());
        var admin = new AdminViewModel(server);
        return (admin.Links, server, sent, ids);
    }

    [Fact]
    public async Task LinkMatrix_MeshOfEight_Sends28Links()
    {
        var (m, _, sent, _) = Matrix(8);
        foreach (var row in m.Rows) row.IsSelected = true;
        m.LinkSelectedCommand.Execute(null);
        Assert.Equal(28, m.PendingCount);
        await m.ApplyCommand.ExecuteAsync(null);
        var request = Assert.IsType<SetChannelLinks>(sent[^1]);
        Assert.Equal((28, 0), (request.Add.Count, request.Remove.Count));
        Assert.Equal(28, request.Add.Select(l => (l.A, l.B)).Distinct().Count());
    }

    [Fact]
    public void LinkMatrix_ToggleDiscardAndRemoteChange()
    {
        var (m, server, _, ids) = Matrix(3);
        var cell = m.Rows[0].Cells[1];
        cell.IsLinked = true;
        Assert.True(m.Rows[1].Cells[0].IsLinked); // the mirrored field follows
        Assert.True(m.Rows[1].Cells[0].IsPending);
        Assert.True(m.Rows[0].Cells[0].IsDiagonal);

        m.DiscardCommand.Execute(null);
        Assert.False(m.Rows[0].Cells[1].IsLinked);
        Assert.False(m.HasPending);

        m.Rows[0].Cells[1].IsLinked = true; // pending K1-K2
        m.Rows[0].Cells[2].IsLinked = true; // pending K1-K3
        server.Apply(new ChannelsLinked(ids[0], ids[1])); // another admin sets K1-K2
        Assert.Equal(1, m.PendingCount); // K1-K2 is real now, K1-K3 still pending
        Assert.True(m.Rows[0].Cells[1].IsLinked);
        Assert.False(m.Rows[0].Cells[1].IsPending);
        Assert.True(m.Rows[0].Cells[2].IsPending);
    }

    [Fact]
    public void LinkTab_OnlyWithChannelLink()
    {
        Assert.False(Create(P.GroupsManage).Admin.ShowLinks);
        var (admin, server, _) = Create(P.ChannelLink);
        Assert.True(admin.ShowLinks);
        Assert.True(server.CanAdminister); // the administration opens for this right alone
    }
}
