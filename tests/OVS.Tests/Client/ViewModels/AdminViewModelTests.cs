using System.Globalization;
using OVS.Client.Collections;
using OVS.Client.Dialogs;
using OVS.Client.Localization;
using OVS.Client.Models;
using OVS.Client.ViewModels;
using OVS.Shared.Moderation;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.Channels;
using OVS.Shared.Protocol.Messages.Connection;
using OVS.Shared.Protocol.Messages.Groups;
using OVS.Shared.Protocol.Messages.Links;
using OVS.Shared.Protocol.Messages.LogAccess;
using OVS.Shared.Protocol.Messages.Moderation;
using OVS.Shared.Protocol.Messages.ServerBackups;
using OVS.Shared.Protocol.Messages.ServerInfo;
using OVS.Shared.Protocol.Messages.Users;
using OVS.Tests.TestSupport;
using P = OVS.Shared.Permissions.Permission;

namespace OVS.Tests.Client.ViewModels;

public class AdminViewModelTests
{
    static readonly Guid Lobby = Guid.NewGuid();
    static readonly Guid ModGroup = Guid.NewGuid();

    /// <param name="reply">Package 75: the server's answer to a request, applied at once (before SendAsync returns, the hardest order).</param>
    /// <param name="groupIds">Package 89: the own groups; upload and restore need the Admin group.</param>
    static AdminPageRig Create(P selfPerms, bool hasPassword = false,
        AdminDialogs? dialogs = null, Func<Request, Message?>? reply = null, IReadOnlyList<Guid>? groupIds = null)
    {
        var snapshot = new ServerSnapshot(new ServerSettingsInfo("Server", "Hallo", hasPassword), Lobby,
            [new ChannelInfo(Lobby, "Lobby", "", 0)], [],
            [
                new GroupInfo(WellKnownGroups.Guest, "Gast", P.Speak, Assignable(P.Speak)),
                new GroupInfo(ModGroup, "Moderator", P.Speak | P.UserKick, Assignable(P.Speak | P.UserKick)),
                new GroupInfo(WellKnownGroups.Admin, "Admin", P.All, Assignable(P.All)),
            ],
            [new UserInfo(1, "fp1", "ich", Lobby, false, false, false, selfPerms, groupIds ?? [])]);
        var sent = new List<Request>();
        ServerViewModel? server = null;
        server = new ServerViewModel(Snapshots.Mirror(snapshot), request =>
        {
            // A coalesced list request comes from the pool (a delay on the manual clock): sending and its answer are one
            // step under the lock, so a test that saw the request (Sent) also sees its answer applied.
            lock (sent)
            {
                sent.Add(request);
                if (reply?.Invoke(request) is { } answer) server!.Apply(answer);
            }
            return Task.CompletedTask;
        }, new ManualTimeProvider());
        return new(new AdminViewModel(server, dialogs), server, sent);

        // Package 92: the server's judgement (PermissionRules.CanAssign), sent with every group
        bool Assignable(P group) => selfPerms.Has(P.GroupsAssign) && group.IsSubsetOf(selfPerms);
    }

    static GroupEditViewModel Group(AdminViewModel admin, string name) => admin.GroupList.Groups.Single(groupEdit => groupEdit.Name == name);

    /// <summary>
    /// Waits until that many user lists were asked for, at most 10 s by the clock: a request held back by the one-second
    /// limit is sent from the pool once the manual clock passes it, which may come late on a busy runner.
    /// </summary>
    static async Task ListUsersSentAsync(List<Request> sent, int count)
    {
        int Count()
        {
            lock (sent) return sent.OfType<ListUsers>().Count();
        }
        await AsyncWait.UntilAsync(() => Count() >= count);
    }

    [Fact]
    public void BanDurations_Offered_AsPlanned()
    {
        Assert.Equal([new Choice<int?>(60, "1 Stunde"), new(1440, "1 Tag"), new(10080, "7 Tage"), new(null, "Dauerhaft")], BanChoice.Durations);
    }

    /// <summary>Package 76: every tab has its own view right; acting rights alone open nothing.</summary>
    [Theory]
    [InlineData(P.GroupsView, true, false, false, false, false)]
    [InlineData(P.UsersView, false, true, false, false, false)]
    [InlineData(P.BansView, false, false, true, false, false)]
    [InlineData(P.ServerConfig, false, false, false, true, false)]
    [InlineData(P.LogsView, false, false, false, false, true)] // Package 81
    [InlineData(P.BackupsManage, false, false, false, true, false)] // Package 89: the server tab with the backups card only
    [InlineData(P.GroupsAssign, false, false, false, false, false)]
    [InlineData(P.GroupsManage | P.GroupsCreate | P.GroupsDelete | P.UserBan | P.UserKick | P.UserDelete, false, false, false, false, false)]
    public async Task Tabs_Rights_VisibleByPermission(P perms, bool groups, bool users, bool bans, bool server, bool logs)
    {
        var (admin, serverVm, sent) = Create(perms);
        Assert.Equal((groups, users, bans, server, logs), (admin.ShowGroups, admin.ShowUsers, admin.ShowBans, admin.ShowServerSettings || admin.ShowBackups, admin.ShowLogs));
        Assert.Equal(groups || users || bans || server || logs, serverVm.CanAdminister);
        await admin.RequestListsAsync();
        Assert.Equal(logs, sent.OfType<ListLogs>().Any());
    }

    [Fact]
    public void UserTogglesAndUnban_Rights_EnabledAccordingly()
    {
        var (view, server, _) = Create(P.UsersView | P.BansView | P.Speak);
        view.UsersAndBans.Apply(new UserList("r", [new KnownUserInfo("fpX", "Xaver", [WellKnownGroups.Guest], CanBeModeratedByMe: true)])); // weaker, no right to act
        Assert.All(Assert.Single(view.UsersAndBans.Users).Toggles, groupAssignment => Assert.False(groupAssignment.IsEnabled));
        view.UsersAndBans.Apply(new BanList("b", [new BanInfo(Guid.NewGuid(), "fpX", "Xaver", null, "", "mod", null)]));
        Assert.False(Assert.Single(view.UsersAndBans.Bans).UnbanCommand.CanExecute(null));

        var (act, actServer, _) = Create(P.UsersView | P.BansView | P.GroupsAssign | P.UserBan | P.Speak);
        act.UsersAndBans.Apply(new UserList("r", [new KnownUserInfo("fpX", "Xaver", [WellKnownGroups.Guest], CanBeModeratedByMe: true)]));
        Assert.True(Assert.Single(act.UsersAndBans.Users).Toggles.Single(groupAssignment => groupAssignment.Name == "Gast").IsEnabled);
        act.UsersAndBans.Apply(new BanList("b", [new BanInfo(Guid.NewGuid(), "fpX", "Xaver", null, "", "mod", null)]));
        Assert.True(Assert.Single(act.UsersAndBans.Bans).UnbanCommand.CanExecute(null));
    }

    [Fact]
    public async Task RequestLists_Rights_OnlyAllowedOnesAsked()
    {
        var (admin, _, sent) = Create(P.BansView | P.GroupsAssign);
        await admin.RequestListsAsync();
        Assert.IsType<ListBans>(Assert.Single(sent));
    }

    [Fact]
    public async Task AssignGroup_SendsRequest_DisablesHigherGroups()
    {
        var (admin, server, sent) = Create(P.UsersView | P.GroupsAssign | P.Speak);
        admin.UsersAndBans.Apply(new UserList("r", [new KnownUserInfo("fpX", "Xaver", [WellKnownGroups.Guest], CanBeModeratedByMe: true)]));

        var user = Assert.Single(admin.UsersAndBans.Users);
        var guest = user.Toggles.Single(groupAssignment => groupAssignment.Name == "Gast");
        Assert.True(guest.IsChecked);
        Assert.True(guest.IsEnabled);
        Assert.False(user.Toggles.Single(groupAssignment => groupAssignment.Name == "Admin").IsEnabled);
        Assert.False(user.Toggles.Single(groupAssignment => groupAssignment.Name == "Moderator").IsEnabled);

        guest.IsChecked = false; // what the checkbox does before running its command
        await guest.ToggleCommand.ExecuteAsync(null);
        Assert.Equal(new UnassignGroup("fpX", WellKnownGroups.Guest), sent[^2] with { RequestId = null });
        Assert.IsType<ListUsers>(sent[^1]);
    }

    /// <summary>
    /// Package 92: without GroupsView the groups come without rights; the toggles follow the server's AssignableByMe
    /// and the user's rank flag, never a guess from missing bits.
    /// </summary>
    [Fact]
    public void GroupToggles_WithoutGroupsView_FollowServerFlags()
    {
        var (admin, server, _) = Create(P.UsersView | P.GroupsAssign | P.Speak | P.UserKick);
        server.Apply(new GroupsChanged(
        [
            new GroupInfo(WellKnownGroups.Guest, "Gast", P.None, true),
            new GroupInfo(ModGroup, "Moderator", P.None, false),
            new GroupInfo(WellKnownGroups.Admin, "Admin", P.None, false),
        ]));
        admin.UsersAndBans.Apply(new UserList("r", [Known("fpX", "Xaver") with { CanBeModeratedByMe = true }, Known("fpA", "Anna", [WellKnownGroups.Admin])]));
        var xaver = admin.UsersAndBans.Users.Single(knownUser => knownUser.Nickname == "Xaver");
        Assert.Equal([true, false, false], xaver.Toggles.Select(groupAssignment => groupAssignment.IsEnabled));
        Assert.All(admin.UsersAndBans.Users.Single(knownUser => knownUser.Nickname == "Anna").Toggles, groupAssignment => Assert.False(groupAssignment.IsEnabled)); // stronger: nothing
        Assert.False(admin.ShowGroups);
    }

    /// <summary>Package 92 (AC3): the list's rank flags were judged by the old rights; new own or group rights ask for it again.</summary>
    [Fact]
    public async Task UserList_AskedAgain_WhenOwnRightsOrGroupsChange()
    {
        var (_, server, sent) = Create(P.UsersView | P.GroupsAssign | P.Speak);
        var time = (ManualTimeProvider)server.Time;
        server.Apply(new UserUpdated(new UserInfo(1, "fp1", "ich", Lobby, true, false, false, P.UsersView | P.GroupsAssign | P.Speak, [])));
        Assert.Empty(sent.OfType<ListUsers>()); // nothing about the rights changed

        server.Apply(new UserUpdated(new UserInfo(1, "fp1", "ich", Lobby, false, false, false, P.UsersView | P.GroupsAssign | P.Speak | P.UserKick, [ModGroup])));
        Assert.Single(sent.OfType<ListUsers>());
        server.Apply(new UserList(sent.OfType<ListUsers>().Single().RequestId, [])); // answered: the next one may go

        time.Advance(TimeSpan.FromSeconds(1));
        server.Apply(new GroupsChanged([new GroupInfo(WellKnownGroups.Guest, "Gast", P.None, true)]));
        await ListUsersSentAsync(sent, 2);
        Assert.Equal(2, sent.OfType<ListUsers>().Count());
    }

    /// <summary>Package 73: the one acting hears a tone once the next user list shows the change, also offline; an error gives none.</summary>
    [Fact]
    public async Task AssignGroup_SoundWhenListConfirms_NotOnError()
    {
        var (admin, server, sent) = Create(P.UsersView | P.GroupsAssign | P.Speak | P.UserKick);
        var heard = new List<OVS.Client.Audio.Sounds.SoundEvent>();
        server.SoundRequested += heard.Add;
        admin.UsersAndBans.Apply(new UserList("r", [new KnownUserInfo("fpX", "Xaver", [WellKnownGroups.Guest])]));

        var mod = Assert.Single(admin.UsersAndBans.Users).Toggles.Single(groupAssignment => groupAssignment.Name == "Moderator");
        mod.IsChecked = true;
        await mod.ToggleCommand.ExecuteAsync(null);
        admin.UsersAndBans.Apply(new UserList("r", [new KnownUserInfo("fpX", "Xaver", [WellKnownGroups.Guest])])); // an older answer: not yet
        Assert.Empty(heard);
        admin.UsersAndBans.Apply(new UserList("r", [new KnownUserInfo("fpX", "Xaver", [WellKnownGroups.Guest, ModGroup])]));
        Assert.Equal([OVS.Client.Audio.Sounds.SoundEvent.GroupChangedByMe], heard);
        admin.UsersAndBans.Apply(new UserList("r", [new KnownUserInfo("fpX", "Xaver", [WellKnownGroups.Guest, ModGroup])])); // only once
        Assert.Single(heard);

        var guest = Assert.Single(admin.UsersAndBans.Users).Toggles.Single(groupAssignment => groupAssignment.Name == "Gast");
        guest.IsChecked = false;
        await guest.ToggleCommand.ExecuteAsync(null);
        server.Apply(new Error(sent.OfType<UnassignGroup>().Single().RequestId, ReasonCodes.LastAdmin));
        admin.UsersAndBans.Apply(new UserList("r", [new KnownUserInfo("fpX", "Xaver", [ModGroup])])); // even if it changed some other way
        Assert.Single(heard);
    }

    /// <summary>Package 74: the backup list is asked for with BackupsManage and not otherwise (the card: BackupCardViewModelTests).</summary>
    [Theory]
    [InlineData(P.All, true)]
    [InlineData(P.BackupsManage | P.Speak, true)]
    [InlineData(P.Speak | P.UsersView, false)]
    [InlineData(P.ServerConfig, false)]
    public async Task RequestListsAsync_ByRights_AsksForTheBackupListOnlyWithBackupsManage(P perms, bool asks)
    {
        var (admin, _, sent) = Create(perms);

        await HangGuard.BoundAsync(admin.RequestListsAsync());

        Assert.Equal(asks, sent.Any(request => request is ListBackups));
    }

    /// <summary>The page tells the backup card about every state change (the card itself: BackupCardViewModelTests).</summary>
    [Fact]
    public void OnServerStateChanged_AdminGroupLeft_BackupRowsLoseRestoreAndUploadRestore()
    {
        var (admin, server, _) = Create(P.All, groupIds: [WellKnownGroups.Admin]);
        admin.BackupCard.Apply(new BackupList("r", [new BackupInfo("2026-02-01_10-00-00.ovsbackup", new DateTimeOffset(2026, 2, 1, 10, 0, 0, TimeSpan.Zero), 800, "dev.0000")]));
        Assert.True(admin.BackupCard.Backups[0].CanRestore);

        server.Apply(new UserUpdated(new UserInfo(1, "fp1", "ich", Lobby, false, false, false, P.All, [])));

        Assert.False(admin.BackupCard.CanUploadRestore);
        Assert.False(admin.BackupCard.Backups[0].CanRestore);
    }

    /// <summary>The page tells the groups tab about every state change (the tab itself: GroupListViewModelTests).</summary>
    [Fact]
    public void OnServerStateChanged_ManageRightLostWhileAGroupIsEdited_RowReadOnlyAndNothingLeftToSave()
    {
        var (admin, server, _) = Create(P.All);
        var edited = Group(admin, "Gast");
        admin.GroupList.SelectedGroup = edited;
        edited.Name = "Umbenannt";
        Assert.True(admin.GroupList.Place.HasChanges());

        FakeServers.SetOwnRightsAndGroups(server, P.All & ~P.GroupsManage, []);

        Assert.True(admin.GroupList.Groups.Single(group => group.Id == WellKnownGroups.Guest).IsReadOnly);
        Assert.False(admin.GroupList.Place.HasChanges());
    }

    /// <summary>
    /// Package 89 (A101): the server tab opens with BackupsManage alone (the backups card only) or with ServerConfig alone
    /// (the settings card only).
    /// </summary>
    [Theory]
    [InlineData(P.BackupsManage | P.Speak, true, false)]
    [InlineData(P.ServerConfig, false, true)]
    public void ServerTab_OneCardRight_ShowsTheTabAndOnlyThatCard(P perms, bool backups, bool settings)
    {
        var (admin, _, _) = Create(perms);

        Assert.Equal((backups, settings), (admin.ShowBackups, admin.ShowServerSettings)); // Package 154: Backups and Übersicht are pages of their own
    }

    // ---- Package 80: ban overview with details, history, search and filter ----

    static BanInfo BanOf(string nick, DateTimeOffset? expires = null, string? ip = null, string reason = "", string by = "mod",
        DateTimeOffset? created = null, int? minutes = null, DateTimeOffset? liftedAt = null, string? liftedBy = null, int attempts = 0,
        DateTimeOffset? lastAttempt = null, string? lastIp = null, string? fp = null) =>
        new(Guid.NewGuid(), fp ?? "fp" + nick, nick, ip, reason, by, expires, created, created is null ? null : "fpMod", minutes,
            liftedAt, liftedBy, attempts, lastAttempt, lastIp);

    static string Local(DateTimeOffset time) => time.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    static void ShowAllBans(AdminViewModel admin) => admin.UsersAndBans.SelectedBanStatusFilter = admin.UsersAndBans.BanStatusFilters.Single(choice => choice.Value == BanStatusFilter.All);

    [Fact]
    public void BanCards_ShowAllDetails_UnknownForMissing()
    {
        var (admin, server, _) = Create(P.BansView);
        var now = server.Time.GetUtcNow();
        admin.UsersAndBans.Apply(new BanList("r",
        [
            BanOf("Troll", now.AddMinutes(90), "10.0.0.9", "Spam", created: now.AddMinutes(-30), minutes: 120, attempts: 3,
                lastAttempt: now.AddMinutes(-5), lastIp: "10.0.0.8", fp: "abcdef0123456789abcdef"),
            BanOf("Otto", now.AddDays(2)), // saved before Package 80
            BanOf("Lisa", created: now.AddDays(-1), liftedAt: now.AddHours(-2), liftedBy: "admin"),
            BanOf("Emil", now.AddMinutes(-1), created: now.AddMinutes(-61), minutes: 60),
        ]));
        ShowAllBans(admin);

        var ban = admin.UsersAndBans.Bans.Single(banViewModel => banViewModel.Nickname == "Troll");
        Assert.Equal(("abcdef0123456789", "abcdef0123456789abcdef"), (ban.ShortFingerprint, ban.Fingerprint));
        Assert.Equal((true, "10.0.0.9"), (ban.HasIp, ban.Ip));
        Assert.Equal("Spam", ban.ReasonText);
        Assert.Equal(("mod", "fpMod", Local(now.AddMinutes(-30))), (ban.CreatedBy, ban.CreatedByFingerprintText, ban.CreatedAtText));
        Assert.Equal(("2 h", Local(now.AddMinutes(90))), (ban.DurationText, ban.EndText));
        Assert.Equal((true, "1 h 30 min"), (ban.HasRemaining, ban.RemainingText));
        Assert.Equal((BanStatus.Active, "aktiv"), (ban.Status, ban.StatusText));
        Assert.Equal($"3, zuletzt {Local(now.AddMinutes(-5))} von 10.0.0.8", ban.AttemptsText);

        var otherBan = admin.UsersAndBans.Bans.Single(banViewModel => banViewModel.Nickname == "Otto");
        Assert.Equal(("unbekannt", "unbekannt", "unbekannt"), (otherBan.CreatedAtText, otherBan.CreatedByFingerprintText, otherBan.DurationText));
        Assert.Equal((false, "ohne Grund", "keine"), (otherBan.HasIp, otherBan.ReasonText, otherBan.AttemptsText));
        Assert.Equal((BanStatus.Active, Local(now.AddDays(2))), (otherBan.Status, otherBan.EndText));

        var lisa = admin.UsersAndBans.Bans.Single(banViewModel => banViewModel.Nickname == "Lisa");
        Assert.Equal((BanStatus.Lifted, $"aufgehoben von admin am {Local(now.AddHours(-2))}"), (lisa.Status, lisa.StatusText));
        Assert.Equal(("dauerhaft", "dauerhaft", false), (lisa.DurationText, lisa.EndText, lisa.HasRemaining));

        var emil = admin.UsersAndBans.Bans.Single(banViewModel => banViewModel.Nickname == "Emil");
        Assert.Equal((BanStatus.Expired, "abgelaufen", "1 h", false), (emil.Status, emil.StatusText, emil.DurationText, emil.HasRemaining));
    }

    [Theory]
    [InlineData("TROLL", "Troll")]
    [InlineData("abc12", "Troll")]
    [InlineData("10.0.0", "Troll")]
    [InlineData("werbung", "Troll")]
    [InlineData("chef", "Lisa")]
    [InlineData("aufheber", "Lisa")]
    [InlineData("", "Lisa,Troll")]
    [InlineData("gibtsnicht", "")]
    public void BanSearch_Text_MatchesAllTextFields(string search, string expected)
    {
        var (admin, server, _) = Create(P.BansView);
        var now = server.Time.GetUtcNow();
        admin.UsersAndBans.Apply(new BanList("r",
        [
            BanOf("Troll", ip: "10.0.0.9", reason: "Werbung", fp: "ffabc12ff", created: now),
            BanOf("Lisa", by: "Chef", liftedAt: now, liftedBy: "Aufheber", created: now.AddDays(-1)),
        ]));
        ShowAllBans(admin);
        admin.UsersAndBans.BanSearchText = search;
        Assert.Equal(expected, string.Join(",", admin.UsersAndBans.Bans.Select(ban => ban.Nickname).Order()));
    }

    [Fact]
    public void BanFilter_StatusTypeAndSort_FilteredSortedAndCounted()
    {
        var (admin, server, _) = Create(P.BansView);
        var now = server.Time.GetUtcNow();
        admin.UsersAndBans.Apply(new BanList("r",
        [
            BanOf("Anna", created: now.AddDays(-1), attempts: 5),
            BanOf("bert", now.AddHours(1), "10.0.0.2", created: now.AddHours(-2), minutes: 180),
            BanOf("Cleo", now.AddHours(-1), created: now.AddDays(-3), minutes: 60, attempts: 1),
            BanOf("Dino", now.AddHours(5), created: now.AddMinutes(-10), minutes: 315, liftedAt: now, liftedBy: "admin", attempts: 9),
            BanOf("Emil", now.AddMinutes(30)), // old: no creation time
        ]));
        string Names() => string.Join(",", admin.UsersAndBans.Bans.Select(ban => ban.Nickname));

        Assert.Equal((BanStatusFilter.Active, BanTypeFilter.All, BanSortOrder.Newest),
            (admin.UsersAndBans.SelectedBanStatusFilter.Value, admin.UsersAndBans.SelectedBanTypeFilter.Value, admin.UsersAndBans.SelectedBanSortOrder.Value));
        Assert.Equal("bert,Anna,Emil", Names()); // active only, newest first, unknown creation last
        Assert.Equal("3 von 5 Bans", admin.UsersAndBans.BanCountText);
        admin.UsersAndBans.SelectedBanStatusFilter = admin.UsersAndBans.BanStatusFilters.Single(choice => choice.Value == BanStatusFilter.Expired);
        Assert.Equal("Cleo", Names());
        admin.UsersAndBans.SelectedBanStatusFilter = admin.UsersAndBans.BanStatusFilters.Single(choice => choice.Value == BanStatusFilter.Lifted);
        Assert.Equal("Dino", Names());
        Assert.Equal("1 von 5 Bans", admin.UsersAndBans.BanCountText);
        ShowAllBans(admin);
        Assert.Equal("Dino,bert,Anna,Cleo,Emil", Names());

        admin.UsersAndBans.SelectedBanTypeFilter = admin.UsersAndBans.BanTypeFilters.Single(choice => choice.Value == BanTypeFilter.Permanent);
        Assert.Equal("Anna", Names());
        admin.UsersAndBans.SelectedBanTypeFilter = admin.UsersAndBans.BanTypeFilters.Single(choice => choice.Value == BanTypeFilter.Temporary);
        Assert.Equal("Dino,bert,Cleo,Emil", Names());
        admin.UsersAndBans.SelectedBanTypeFilter = admin.UsersAndBans.BanTypeFilters.Single(choice => choice.Value == BanTypeFilter.WithIp);
        Assert.Equal("bert", Names());
        admin.UsersAndBans.SelectedBanTypeFilter = admin.UsersAndBans.BanTypeFilters[0];

        admin.UsersAndBans.SelectedBanSortOrder = admin.UsersAndBans.BanSortOrders.Single(choice => choice.Value == BanSortOrder.EndingSoonest);
        Assert.Equal("Cleo,Emil,bert,Dino,Anna", Names()); // permanent last
        admin.UsersAndBans.SelectedBanSortOrder = admin.UsersAndBans.BanSortOrders.Single(choice => choice.Value == BanSortOrder.MostAttempts);
        Assert.Equal("Dino,Anna,Cleo,bert,Emil", Names());
        admin.UsersAndBans.SelectedBanSortOrder = admin.UsersAndBans.BanSortOrders.Single(choice => choice.Value == BanSortOrder.Name);
        Assert.Equal("Anna,bert,Cleo,Dino,Emil", Names());
        Assert.Equal("5 von 5 Bans", admin.UsersAndBans.BanCountText);
    }

    [Fact]
    public void Unban_ActiveBanWithRight_OnlyThenOfferedAndFilterKept()
    {
        var (view, viewServer, _) = Create(P.BansView);
        var now = viewServer.Time.GetUtcNow();
        BanInfo[] bans =
        [
            BanOf("Anna", created: now),
            BanOf("bert", now.AddHours(1), created: now),
            BanOf("Cleo", now.AddHours(-1), created: now.AddDays(-1)),
            BanOf("Dino", created: now.AddDays(-1), liftedAt: now, liftedBy: "admin"),
        ];
        view.UsersAndBans.Apply(new BanList("r", bans));
        ShowAllBans(view);
        Assert.All(view.UsersAndBans.Bans, ban => Assert.False(ban.CanUnban)); // read-only without UserBan
        Assert.All(view.UsersAndBans.Bans, ban => Assert.False(ban.UnbanCommand.CanExecute(null)));

        var (admin, server, sent) = Create(P.BansView | P.UserBan);
        admin.UsersAndBans.Apply(new BanList("r", bans));
        ShowAllBans(admin);
        Assert.Equal(["Anna", "bert"], admin.UsersAndBans.Bans.Where(ban => ban.CanUnban).Select(ban => ban.Nickname).Order());
        Assert.False(admin.UsersAndBans.Bans.Single(ban => ban.Nickname == "Dino").UnbanCommand.CanExecute(null));

        admin.UsersAndBans.BanSearchText = "bert";
        admin.UsersAndBans.SelectedBanSortOrder = admin.UsersAndBans.BanSortOrders.Single(choice => choice.Value == BanSortOrder.Name);
        admin.UsersAndBans.Bans.Single().UnbanCommand.Execute(null);
        Assert.Equal(new Unban(bans[1].Id), sent.OfType<Unban>().Single() with { RequestId = null });

        admin.UsersAndBans.Apply(new BanList("u", [.. bans[..1], bans[1] with { LiftedAt = now, LiftedBy = "ich" }, .. bans[2..]]));
        Assert.Equal(("bert", BanStatusFilter.All, BanSortOrder.Name), (admin.UsersAndBans.BanSearchText, admin.UsersAndBans.SelectedBanStatusFilter.Value, admin.UsersAndBans.SelectedBanSortOrder.Value));
        var bert = Assert.Single(admin.UsersAndBans.Bans);
        Assert.Equal((BanStatus.Lifted, false), (bert.Status, bert.CanUnban));
    }

    // ---- Package 71: user overview ----

    static readonly DateTimeOffset T0 = new(2026, 9, 1, 18, 30, 0, TimeSpan.Zero);

    static KnownUserInfo Known(string fp, string nick, Guid[]? groups = null, DateTimeOffset? lastLogin = null, int logins = 3,
        TimeSpan? online = null, string? ip = "10.0.0.7", string[]? previous = null, BanInfo[]? bans = null) =>
        new(fp, nick, groups ?? [WellKnownGroups.Guest], T0.AddDays(-30), lastLogin ?? T0, logins, online ?? TimeSpan.FromMinutes(90), ip,
            previous ?? [], TimeSpan.FromSeconds(75), 12, false, null, bans);

    static UserInfo Online(uint id, string fp, string nick) => new(id, fp, nick, Lobby, false, false, false, P.Speak, [WellKnownGroups.Guest]);

    [Fact]
    public void UserDetails_KnownUser_FilledFromItsInfo()
    {
        var (admin, server, _) = Create(P.UsersView | P.GroupsAssign | P.Speak);
        var ban = new BanInfo(Guid.NewGuid(), "fpX", "Xaver", null, "Spam", "mod", T0.AddDays(2));
        server.Apply(new UserJoined(Online(2, "fpX0123456789abcdef", "Xaver")));
        admin.UsersAndBans.Apply(new UserList("r",
        [
            Known("fpX0123456789abcdef", "Xaver", [WellKnownGroups.Guest, ModGroup], previous: ["Xav", "X"], bans: [ban]),
            new KnownUserInfo("fpOld", "Otto", [WellKnownGroups.Guest]), // saved before Package 70
        ]));

        var knownUser = admin.UsersAndBans.Users.Single(knownUserViewModel => knownUserViewModel.Nickname == "Xaver");
        Assert.True(knownUser.IsOnline);
        Assert.Equal(["Gast", "Moderator"], knownUser.Toggles.Where(groupAssignment => groupAssignment.IsChecked).Select(groupAssignment => groupAssignment.Name));
        Assert.Equal(T0.AddDays(-30).ToLocalTime().ToString("g", CultureInfo.CurrentCulture), knownUser.FirstSeenText);
        Assert.Equal(T0.ToLocalTime().ToString("g", CultureInfo.CurrentCulture), knownUser.LastLoginText);
        Assert.Equal("3", knownUser.LoginCountText);
        Assert.Equal("1 h 30 min", knownUser.OnlineTimeText);
        Assert.Equal("1 min 15 s", knownUser.SpeechTimeText);
        Assert.Equal("12", knownUser.ChatMessagesText);
        Assert.Equal("10.0.0.7", knownUser.LastIpText);
        Assert.Equal("Xav, X", knownUser.PreviousNicknamesText);
        Assert.Equal("fpX0123456789abc", knownUser.ShortFingerprint);
        Assert.Equal("fpX0123456789abcdef", knownUser.Fingerprint);
        Assert.True(knownUser.IsBanned);
        Assert.Contains("Spam", knownUser.BanText, StringComparison.Ordinal);
        Assert.Contains(T0.AddDays(2).ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.CurrentCulture), knownUser.BanText, StringComparison.Ordinal);

        var otto = admin.UsersAndBans.Users.Single(knownUserViewModel => knownUserViewModel.Nickname == "Otto");
        Assert.False(otto.IsOnline);
        Assert.False(otto.IsBanned);
        Assert.All(new[] { otto.FirstSeenText, otto.LastLoginText, otto.LoginCountText, otto.OnlineTimeText, otto.SpeechTimeText,
            otto.ChatMessagesText, otto.LastIpText, otto.PreviousNicknamesText }, text => Assert.Equal("unbekannt", text));
    }

    [Theory]
    [InlineData("xav", "Xaver")]
    [InlineData("ALTNAME", "Xaver")]
    [InlineData("ab12", "Berta")]
    [InlineData("192.168.", "Berta")]
    [InlineData("", "Berta,Xaver")]
    [InlineData("gibtsnicht", "")]
    public void UserSearch_Text_MatchesNicknamePreviousFingerprintAndIp(string search, string expected)
    {
        var (admin, server, _) = Create(P.UsersView);
        admin.UsersAndBans.Apply(new UserList("r",
        [
            Known("ffff0000", "Xaver", previous: ["Altname"], ip: "10.0.0.7"),
            Known("00AB12cd", "Berta", ip: "192.168.1.20"),
        ]));
        admin.UsersAndBans.SearchText = search;
        Assert.Equal(expected, string.Join(",", admin.UsersAndBans.Users.Select(knownUser => knownUser.Nickname)));
    }

    [Fact]
    public void UserFilter_StatusGroupAndSort_FilteredSortedAndCounted()
    {
        var (admin, server, _) = Create(P.UsersView);
        var ban = new BanInfo(Guid.NewGuid(), "fpC", "Cora", null, "", "mod", null);
        server.Apply(new UserJoined(Online(2, "fpB", "Bert")));
        admin.UsersAndBans.Apply(new UserList("r",
        [
            Known("fpA", "anton", lastLogin: T0.AddDays(-1), online: TimeSpan.FromHours(5)),
            Known("fpB", "Bert", [WellKnownGroups.Guest, ModGroup], lastLogin: T0, online: TimeSpan.FromHours(1)),
            Known("fpC", "Cora", lastLogin: T0.AddDays(-5), online: TimeSpan.FromHours(9), bans: [ban]),
            new KnownUserInfo("fpD", "Dora", [WellKnownGroups.Guest]),
        ]));
        string Names() => string.Join(",", admin.UsersAndBans.Users.Select(knownUser => knownUser.Nickname));

        Assert.Equal("anton,Bert,Cora,Dora", Names()); // by name, case-insensitive
        Assert.Equal("4 von 4 Nutzern", admin.UsersAndBans.UserCountText);

        admin.UsersAndBans.SelectedStatusFilter = admin.UsersAndBans.StatusFilters.Single(choice => choice.Value == UserStatusFilter.Online);
        Assert.Equal("Bert", Names());
        admin.UsersAndBans.SelectedStatusFilter = admin.UsersAndBans.StatusFilters.Single(choice => choice.Value == UserStatusFilter.Offline);
        Assert.Equal("anton,Cora,Dora", Names());
        admin.UsersAndBans.SelectedStatusFilter = admin.UsersAndBans.StatusFilters.Single(choice => choice.Value == UserStatusFilter.Banned);
        Assert.Equal("Cora", Names());
        Assert.Equal("1 von 4 Nutzern", admin.UsersAndBans.UserCountText);
        admin.UsersAndBans.SelectedStatusFilter = admin.UsersAndBans.StatusFilters[0];

        Assert.Equal(["Alle Gruppen", "Gast", "Moderator", "Admin"], admin.UsersAndBans.GroupFilters.Select(choice => choice.Label));
        admin.UsersAndBans.SelectedGroupFilter = admin.UsersAndBans.GroupFilters.Single(choice => choice.Label == "Moderator");
        Assert.Equal("Bert", Names());
        admin.UsersAndBans.SelectedGroupFilter = admin.UsersAndBans.GroupFilters[0];

        admin.UsersAndBans.SelectedSortOrder = admin.UsersAndBans.SortOrders.Single(choice => choice.Value == UserSortOrder.LastLogin);
        Assert.Equal("Bert,anton,Cora,Dora", Names()); // newest first, never logged in last
        admin.UsersAndBans.SelectedSortOrder = admin.UsersAndBans.SortOrders.Single(choice => choice.Value == UserSortOrder.OnlineTime);
        Assert.Equal("Cora,anton,Bert,Dora", Names());
    }

    [Fact]
    public void UsersViewOnly_SearchWorks_TogglesLocked()
    {
        var (admin, server, _) = Create(P.UsersView);
        Assert.True(admin.ShowUsers);
        admin.UsersAndBans.Apply(new UserList("r", [Known("fpA", "Anton"), Known("fpB", "Berta")]));
        admin.UsersAndBans.SearchText = "ber";
        var berta = Assert.Single(admin.UsersAndBans.Users);
        Assert.Equal("10.0.0.7", berta.LastIpText);
        Assert.All(berta.Toggles, groupAssignment => Assert.False(groupAssignment.IsEnabled));
        Assert.True(berta.Toggles.Single(groupAssignment => groupAssignment.Name == "Gast").IsChecked);
    }

    [Fact]
    public async Task UserJoins_ListRefreshed_FilterKept()
    {
        var (admin, server, sent) = Create(P.UsersView);
        var time = (ManualTimeProvider)server.Time;
        admin.UsersAndBans.Apply(new UserList("r", [Known("fpA", "Anton"), Known("fpB", "Berta")]));
        admin.UsersAndBans.SearchText = "ber";
        admin.UsersAndBans.SelectedStatusFilter = admin.UsersAndBans.StatusFilters.Single(choice => choice.Value == UserStatusFilter.Online);
        Assert.Empty(admin.UsersAndBans.Users);

        server.Apply(new UserJoined(Online(2, "fpB", "Berta")));
        Assert.Equal("Berta", Assert.Single(admin.UsersAndBans.Users).Nickname); // online at once, from the live state
        Assert.Single(sent.OfType<ListUsers>()); // and the stored data is asked for again

        server.Apply(new UserJoined(Online(3, "fpC", "Carl")));
        server.Apply(new UserLeft(2));
        Assert.Single(sent.OfType<ListUsers>()); // at most once per second
        Assert.Empty(admin.UsersAndBans.Users);
        server.Apply(new UserList(sent.OfType<ListUsers>().Single().RequestId, [Known("fpA", "Anton"), Known("fpB", "Berta")])); // and one at a time
        time.Advance(TimeSpan.FromSeconds(1));
        await ListUsersSentAsync(sent, 2);
        Assert.Equal(2, sent.OfType<ListUsers>().Count());

        admin.UsersAndBans.Apply(new UserList("r", [Known("fpA", "Anton"), Known("fpB", "Berta"), Known("fpC", "Carla")]));
        Assert.Equal("ber", admin.UsersAndBans.SearchText);
        Assert.Equal(UserStatusFilter.Online, admin.UsersAndBans.SelectedStatusFilter.Value);
        Assert.Empty(admin.UsersAndBans.Users);
        admin.UsersAndBans.SelectedStatusFilter = admin.UsersAndBans.StatusFilters[0];
        Assert.Equal("Berta", Assert.Single(admin.UsersAndBans.Users).Nickname);
    }

    // ---- Package 72: ban, unban and delete from the user card ----

    [Fact]
    public async Task DeleteUser_AsksFirst_SendsOnlyAfterConfirm()
    {
        var answer = false;
        string? asked = null;
        var dialogs = new AdminDialogs
        {
            UsersAndBans = new()
            {
                ConfirmDeleteUser = name =>
                {
                    asked = name;
                    return Task.FromResult(answer);
                },
            },
        };
        var (admin, server, sent) = Create(P.UsersView | P.BansView | P.UserDelete | P.Speak, dialogs: dialogs);
        admin.UsersAndBans.Apply(new UserList("r", [Known("fpX", "Xaver")]));
        var user = Assert.Single(admin.UsersAndBans.Users);

        await user.DeleteCommand.ExecuteAsync(null);
        Assert.Equal("Xaver", asked);
        Assert.Empty(sent.OfType<DeleteKnownUser>());

        answer = true;
        await user.DeleteCommand.ExecuteAsync(null);
        var index = sent.FindIndex(request => request is DeleteKnownUser);
        Assert.Equal(new DeleteKnownUser("fpX"), sent[index] with { RequestId = null });
        Assert.Equal([typeof(ListUsers), typeof(ListBans)], sent.Skip(index + 1).Select(request => request.GetType())); // both tabs follow
    }

    [Fact]
    public async Task BanButtons_StateAndRight_ShownAccordingly()
    {
        var ban1 = new BanInfo(Guid.NewGuid(), "fpB", "Bert", null, "", "mod", null);
        var ban2 = new BanInfo(Guid.NewGuid(), "fpB", "Bert", "10.0.0.7", "", "mod", T0.AddDays(3));
        KnownUserInfo[] users =
        [
            // Package 92: the server's rank judgement for an actor with Speak and UserKick
            Known("fpX", "Xaver") with { CanBeModeratedByMe = true },
            Known("fpB", "Bert", bans: [ban1, ban2]) with { CanBeModeratedByMe = true },
            Known("fpM", "Mod", [WellKnownGroups.Guest, ModGroup]) with { CanBeModeratedByMe = true },
            Known("fpA", "Anna", [WellKnownGroups.Admin]),
            Known("fp1", "ich", [ModGroup]),
            new KnownUserInfo("fpOld", "Otto", [WellKnownGroups.Guest], CanBeModeratedByMe: true),
        ];
        static BanButtonsShown Buttons(AdminViewModel admin, string nick)
        {
            var knownUser = admin.UsersAndBans.Users.Single(knownUserViewModel => knownUserViewModel.Nickname == nick);
            return new(knownUser.CanBan, knownUser.CanUnban, knownUser.CanDelete);
        }

        var (view, viewServer, _) = Create(P.UsersView | P.Speak | P.UserKick);
        view.UsersAndBans.Apply(new UserList("r", users));
        Assert.All(view.UsersAndBans.Users, knownUser => Assert.Equal((false, false, false), (knownUser.CanBan, knownUser.CanUnban, knownUser.CanDelete)));

        var (banOnly, banServer, _) = Create(P.UsersView | P.UserBan | P.Speak | P.UserKick);
        banOnly.UsersAndBans.Apply(new UserList("r", users));
        Assert.Equal(new BanButtonsShown(true, false, false), Buttons(banOnly, "Xaver"));

        BanChoice? choice = new("spam", 60, true);
        bool? ipOffered = null;
        var dialogs = new AdminDialogs
        {
            UsersAndBans = new()
            {
                Ban = (_, ipKnown, _) =>
                {
                    ipOffered = ipKnown;
                    return Task.FromResult<BanChoice?>(choice);
                },
            },
        };
        var (viewModel, server, sent) = Create(P.UsersView | P.UserBan | P.UserDelete | P.Speak | P.UserKick, dialogs: dialogs);
        viewModel.UsersAndBans.Apply(new UserList("r", users));
        Assert.Equal(new BanButtonsShown(true, false, true), Buttons(viewModel, "Xaver"));
        Assert.Equal(new BanButtonsShown(false, true, true), Buttons(viewModel, "Bert"));   // banned: unban instead of ban
        Assert.Equal(new BanButtonsShown(true, false, true), Buttons(viewModel, "Mod"));    // Speak and UserKick are a subset (judged by the server)
        Assert.Equal(new BanButtonsShown(false, false, false), Buttons(viewModel, "Anna")); // stronger (Admin)
        Assert.Equal(new BanButtonsShown(false, false, false), Buttons(viewModel, "ich"));  // never oneself

        await viewModel.UsersAndBans.Users.Single(knownUser => knownUser.Nickname == "Xaver").BanCommand.ExecuteAsync(null);
        Assert.True(ipOffered);
        Assert.Equal(new BanKnownUser("fpX", "spam", 60, true), sent.OfType<BanKnownUser>().Last() with { RequestId = null });
        Assert.IsType<ListUsers>(sent[^1]);

        await viewModel.UsersAndBans.Users.Single(knownUser => knownUser.Nickname == "Otto").BanCommand.ExecuteAsync(null); // no IP known: the option is locked
        Assert.False(ipOffered);
        Assert.Equal(new BanKnownUser("fpOld", "spam", 60, false), sent.OfType<BanKnownUser>().Last() with { RequestId = null });

        await viewModel.UsersAndBans.Users.Single(knownUser => knownUser.Nickname == "Bert").UnbanCommand.ExecuteAsync(null);
        Assert.Equal([ban1.Id, ban2.Id], sent.OfType<Unban>().Select(request => request.BanId));

        choice = null; // cancelled
        var count = sent.Count;
        await viewModel.UsersAndBans.Users.Single(knownUser => knownUser.Nickname == "Xaver").BanCommand.ExecuteAsync(null);
        Assert.Equal(count, sent.Count);
    }

    /// <summary>
    /// Package 84 (A102): user cards, group toggles and ban cards offer exactly what the server allows, by the same rule.
    /// Package 92: the server sends the rule's result with the list (StateSyncTests); here the stored rights become that flag.
    /// </summary>
    [Fact]
    public void UserActions_StoredRights_MatchServerRule()
    {
        var banX = new BanInfo(Guid.NewGuid(), "fpX", "Xaver", null, "", "mod", null);
        var banA = new BanInfo(Guid.NewGuid(), "fpA", "Anna", null, "", "mod", null);
        KnownUserInfo[] users =
        [
            Known("fpX", "Xaver", bans: [banX]),
            Known("fpA", "Anna", [WellKnownGroups.Admin], bans: [banA]), // two admins: equal rights
            Known("fp1", "ich", [WellKnownGroups.Admin]),
            Known("fpM", "Mod", [WellKnownGroups.Guest, ModGroup]),
        ];
        static P Rights(KnownUserInfo user) =>
            user.GroupIds.Contains(WellKnownGroups.Admin) ? P.All : user.GroupIds.Contains(ModGroup) ? P.Speak | P.UserKick : P.Speak;
        var (admin, server, _) = Create(P.All, groupIds: [WellKnownGroups.Admin]);
        admin.UsersAndBans.Apply(new UserList("r", users.Select(knownUser => knownUser with { CanBeModeratedByMe = P.All.CanModerate("fp1", Rights(knownUser), knownUser.Fingerprint) }).ToList()));
        admin.UsersAndBans.Apply(new BanList("b", [banX, banA]));
        foreach (var user in users)
        {
            var allowed = P.All.CanModerate("fp1", Rights(user), user.Fingerprint);
            var card = admin.UsersAndBans.Users.Single(knownUser => knownUser.Fingerprint == user.Fingerprint);
            Assert.Equal(allowed && card.IsBanned is false, card.CanBan);
            Assert.Equal(allowed && card.IsBanned, card.CanUnban);
            Assert.Equal(allowed, card.CanDelete);
            Assert.All(card.Toggles, groupAssignment => Assert.Equal(allowed, groupAssignment.IsEnabled));
        }
        Assert.Equal((true, false), (admin.UsersAndBans.Users.Single(knownUser => knownUser.Nickname == "Xaver").CanUnban, admin.UsersAndBans.Users.Single(knownUser => knownUser.Nickname == "Anna").CanUnban));
        Assert.False(admin.UsersAndBans.Users.Single(knownUser => knownUser.Nickname == "ich").CanDelete);
        Assert.Equal((true, false), (admin.UsersAndBans.Bans.Single(ban => ban.Nickname == "Xaver").CanUnban, admin.UsersAndBans.Bans.Single(ban => ban.Nickname == "Anna").CanUnban));
    }

    /// <summary>Package 85: the stored server mute on the card, lifted from there with UserMute, offline too, by the rank rule.</summary>
    [Fact]
    public async Task UserCard_ShowsServerMute_LiftOffline()
    {
        KnownUserInfo[] users =
        [
            Known("fpX", "Xaver") with { ServerMuted = true, CanBeModeratedByMe = true }, // Package 92: as the server judges it
            Known("fpA", "Anna", [WellKnownGroups.Admin]) with { ServerMuted = true },
            Known("fpY", "Yvonne") with { CanBeModeratedByMe = true },
        ];
        var (view, viewServer, _) = Create(P.UsersView | P.Speak | P.UserKick);
        view.UsersAndBans.Apply(new UserList("r", users));
        var seen = view.UsersAndBans.Users.Single(knownUser => knownUser.Nickname == "Xaver");
        Assert.True(seen.IsServerMuted);
        Assert.False(seen.CanLiftMute); // shown, but without UserMute not offered

        var (admin, server, sent) = Create(P.UsersView | P.UserMute | P.Speak | P.UserKick);
        admin.UsersAndBans.Apply(new UserList("r", users));
        var knownUserViewModel = admin.UsersAndBans.Users.Single(knownUser => knownUser.Nickname == "Xaver");
        Assert.Equal((true, true, true), (knownUserViewModel.IsServerMuted, knownUserViewModel.CanLiftMute, knownUserViewModel.HasActions));
        Assert.Equal((true, false), (admin.UsersAndBans.Users.Single(knownUser => knownUser.Nickname == "Anna").IsServerMuted, admin.UsersAndBans.Users.Single(knownUser => knownUser.Nickname == "Anna").CanLiftMute));
        Assert.Equal((false, false), (admin.UsersAndBans.Users.Single(knownUser => knownUser.Nickname == "Yvonne").IsServerMuted, admin.UsersAndBans.Users.Single(knownUser => knownUser.Nickname == "Yvonne").CanLiftMute));

        await knownUserViewModel.LiftMuteCommand.ExecuteAsync(null);
        Assert.Equal(new SetKnownUserServerMute("fpX", false), sent[^2] with { RequestId = null });
        Assert.IsType<ListUsers>(sent[^1]);
    }

    // ---- Package 38: link matrix ----

    static MatrixRig Matrix(int channelCount, P perms = P.All)
    {
        var ids = Enumerable.Range(0, channelCount).Select(_ => Guid.NewGuid()).ToArray();
        var snapshot = Snapshots.Snapshot("Server", "", ids[0],
            ids.Select((id, index) => new ChannelInfo(id, $"K{index + 1}", "", index)).ToList(),
            [new GroupInfo(WellKnownGroups.Guest, "Gast", P.Speak)],
            [new UserInfo(1, "fp1", "ich", ids[0], false, false, false, perms, [])]);
        var sent = new List<Request>();
        var server = new ServerViewModel(Snapshots.Mirror(snapshot), request =>
        {
            sent.Add(request);
            return Task.CompletedTask;
        }, new ManualTimeProvider());
        var admin = new AdminViewModel(server);
        return new(admin.Links, server, sent, ids);
    }

    [Fact]
    public async Task LinkMatrix_MeshOfEight_Sends28Links()
    {
        var (linkMatrix, _, sent, _) = Matrix(8);
        foreach (var row in linkMatrix.Rows) row.IsSelected = true;
        linkMatrix.LinkSelectedCommand.Execute(null);
        Assert.Equal(28, linkMatrix.PendingCount);
        await linkMatrix.ApplyCommand.ExecuteAsync(null);
        var request = Assert.IsType<SetChannelLinks>(sent[^1]);
        Assert.Equal((28, 0), (request.Add.Count, request.Remove.Count));
        Assert.Equal(28, request.Add.Select(link => (link.A, link.B)).Distinct().Count());
    }

    [Fact]
    public void LinkMatrix_ToggleDiscardOrRemoteChange_Followed()
    {
        var (linkMatrix, server, _, ids) = Matrix(3);
        var cell = linkMatrix.Rows[0].Cells[1];
        cell.IsLinked = true;
        Assert.True(linkMatrix.Rows[1].Cells[0].IsLinked); // the mirrored field follows
        Assert.True(linkMatrix.Rows[1].Cells[0].IsPending);
        Assert.True(linkMatrix.Rows[0].Cells[0].IsDiagonal);

        linkMatrix.DiscardCommand.Execute(null);
        Assert.False(linkMatrix.Rows[0].Cells[1].IsLinked);
        Assert.False(linkMatrix.HasPending);

        linkMatrix.Rows[0].Cells[1].IsLinked = true; // pending K1-K2
        linkMatrix.Rows[0].Cells[2].IsLinked = true; // pending K1-K3
        server.Apply(new ChannelsLinked(ids[0], ids[1])); // another admin sets K1-K2
        Assert.Equal(1, linkMatrix.PendingCount); // K1-K2 is real now, K1-K3 still pending
        Assert.True(linkMatrix.Rows[0].Cells[1].IsLinked);
        Assert.False(linkMatrix.Rows[0].Cells[1].IsPending);
        Assert.True(linkMatrix.Rows[0].Cells[2].IsPending);
    }

    [Fact]
    public void LinkTab_WithoutChannelLink_Hidden()
    {
        Assert.False(Create(P.GroupsManage).Admin.ShowLinks);
        var (admin, server, _) = Create(P.ChannelLink);
        Assert.True(admin.ShowLinks);
        Assert.True(server.CanAdminister); // the administration opens for this right alone
    }

    /// <summary>Package 87 (AC4): a long list arrives in pages; the page asks for the rest and shows it once complete.</summary>
    [Fact]
    public void UserList_LongList_AllPagesFetched()
    {
        var users = Enumerable.Range(0, 5).Select(index => new KnownUserInfo($"fp{index}", $"Nutzer{index}", [WellKnownGroups.Guest])).ToList();
        UserList PageAt(string? id, int offset) => new(id, users.Skip(offset).Take(2).ToList(), offset, users.Count);
        var (admin, server, sent) = Create(P.UsersView | P.Speak, reply: request => request is ListUsers listUsers && listUsers.Offset > 0 ? PageAt(listUsers.RequestId, listUsers.Offset) : null);

        admin.UsersAndBans.Apply(PageAt("r", 0));
        Assert.Equal([2, 4], sent.OfType<ListUsers>().Select(request => request.Offset));
        Assert.Equal(users.Select(knownUser => knownUser.Fingerprint), admin.UsersAndBans.Users.Select(knownUser => knownUser.Fingerprint).Order());

        // a stale page of an older round is ignored
        admin.UsersAndBans.Apply(PageAt("alt", 4));
        Assert.Equal(5, admin.UsersAndBans.Users.Count);
    }

    /// <summary>A user joins while a long list is still fetched: the pages of both rounds must not mix.</summary>
    [Fact]
    public void UserList_JoinMidFetch_NoUserMissingOrTwice()
    {
        var before = Enumerable.Range(0, 450).Select(index => Known($"fp{index:D3}", $"Nutzer{index:D3}")).ToList();
        var after = before.Prepend(Known("fpNew", "Aaron")).ToList(); // shifts every later page by one
        UserList PageOf(List<KnownUserInfo> all, string? id, int offset) => new(id, all.Skip(offset).Take(200).ToList(), offset, all.Count);
        var (admin, server, sent) = Create(P.UsersView | P.Speak);

        admin.UsersAndBans.Apply(PageOf(before, "r0", 0));
        server.Apply(new UserJoined(Online(2, "fpNew", "Aaron")));
        var fresh = sent.OfType<ListUsers>().Single(request => request.Offset == 0).RequestId;
        server.Apply(PageOf(after, fresh, 0));
        server.Apply(PageOf(before, "r0", 200)); // the old round's answer arrives late
        server.Apply(PageOf(after, fresh, 200));
        Assert.Contains(sent.OfType<ListUsers>(), request => request.Offset == 400 && request.RequestId == fresh);
        server.Apply(PageOf(after, fresh, 400));

        Assert.Equal(after.Select(knownUser => knownUser.Fingerprint).Order(), admin.UsersAndBans.Users.Select(knownUser => knownUser.Fingerprint).Order());
    }

    /// <summary>Quickly ticked group boxes: the list refreshes are coalesced, so the server's list limit (2/s, burst 4) is never hit.</summary>
    [Fact]
    public async Task QuickGroupToggles_CoalesceUserLists_NoRateLimit()
    {
        var groups = new List<Guid> { WellKnownGroups.Guest };
        ManualTimeProvider? clock = null;
        double tokens = 4;
        DateTimeOffset? refilled = null;
        Message? Serve(Request request)
        {
            switch (request)
            {
                case AssignGroup assignGroup: groups.Add(assignGroup.GroupId); return null;
                case UnassignGroup unassignGroup: groups.Remove(unassignGroup.GroupId); return null;
                case ListUsers listUsers:
                    var now = clock!.GetUtcNow();
                    if (refilled is { } at) tokens = Math.Min(4, tokens + (now - at).TotalSeconds * 2);
                    refilled = now;
                    if (tokens < 1) return new Error(listUsers.RequestId, ReasonCodes.RateLimited);
                    tokens--;
                    return new UserList(listUsers.RequestId, [new KnownUserInfo("fpX", "Xaver", [.. groups], CanBeModeratedByMe: true)]);
                default: return null;
            }
        }
        var (admin, server, sent) = Create(P.UsersView | P.GroupsAssign | P.Speak | P.UserKick, reply: Serve);
        clock = (ManualTimeProvider)server.Time;
        var notices = new List<string>();
        server.Notice += notices.Add;
        admin.UsersAndBans.Apply(new UserList("r", [new KnownUserInfo("fpX", "Xaver", [WellKnownGroups.Guest], CanBeModeratedByMe: true)]));
        GroupAssignmentViewModel Box(string name) => Assert.Single(admin.UsersAndBans.Users).Toggles.Single(groupAssignment => groupAssignment.Name == name);

        foreach (var (name, on) in new[] { ("Moderator", true), ("Gast", false), ("Moderator", false), ("Gast", true), ("Moderator", true) })
        {
            var box = Box(name);
            box.IsChecked = on;
            await box.ToggleCommand.ExecuteAsync(null);
        }
        Assert.Equal((true, true), (Box("Gast").IsChecked, Box("Moderator").IsChecked));
        clock.Advance(TimeSpan.FromSeconds(1));
        await ListUsersSentAsync(sent, 2);

        Assert.Empty(notices);
        Assert.InRange(sent.OfType<ListUsers>().Count(), 1, 2);
        Assert.Equal((true, true), (Box("Gast").IsChecked, Box("Moderator").IsChecked));
    }

    /// <summary>A just ticked box keeps its state until a list asked for after the tick arrives; an older list does not flip it back.</summary>
    [Fact]
    public async Task GroupToggle_OlderListArrives_NotReverted()
    {
        var (admin, server, sent) = Create(P.UsersView | P.GroupsAssign | P.Speak | P.UserKick);
        var time = (ManualTimeProvider)server.Time;
        var before = new KnownUserInfo("fpX", "Xaver", [WellKnownGroups.Guest], CanBeModeratedByMe: true);
        server.Apply(new UserJoined(Online(2, "fpX", "Xaver"))); // a refresh is on its way when the box is ticked
        var older = sent.OfType<ListUsers>().Single().RequestId;
        admin.UsersAndBans.Apply(new UserList("r", [before]));
        GroupAssignmentViewModel Mod() => Assert.Single(admin.UsersAndBans.Users).Toggles.Single(groupAssignment => groupAssignment.Name == "Moderator");

        Mod().IsChecked = true;
        await Mod().ToggleCommand.ExecuteAsync(null);
        server.Apply(new UserList(older, [before])); // asked for before the tick
        Assert.True(Mod().IsChecked);

        time.Advance(TimeSpan.FromSeconds(1));
        await ListUsersSentAsync(sent, 2);
        var newer = sent.LastRequestId<ListUsers>();
        Assert.NotEqual(older, newer);
        server.Apply(new UserList(newer, [before])); // asked for after the tick: this one counts, even without the group
        Assert.False(Mod().IsChecked);
    }

    /// <summary>Unban and ban right after each other: the ban list is asked for once (the unban is answered with it).</summary>
    [Fact]
    public async Task UnbanThenBan_RightAfterEachOther_BanListAskedOnce()
    {
        var ban = new BanInfo(Guid.NewGuid(), "fpB", "Bert", null, "", "mod", null);
        var dialogs = new AdminDialogs { UsersAndBans = new() { Ban = (_, _, _) => Task.FromResult<BanChoice?>(new BanChoice("spam", 60, false)) } };
        var (admin, server, sent) = Create(P.UsersView | P.BansView | P.UserBan | P.Speak, dialogs: dialogs);
        admin.UsersAndBans.Apply(new UserList("r", [Known("fpX", "Xaver") with { CanBeModeratedByMe = true },
            Known("fpB", "Bert", bans: [ban]) with { CanBeModeratedByMe = true }]));

        await admin.UsersAndBans.Users.Single(knownUser => knownUser.Nickname == "Bert").UnbanCommand.ExecuteAsync(null);
        await admin.UsersAndBans.Users.Single(knownUser => knownUser.Nickname == "Xaver").BanCommand.ExecuteAsync(null);

        Assert.Single(sent.OfType<ListBans>());
        Assert.Single(sent.OfType<ListUsers>());
    }

    // ---- Package 97 (A113): waiting states ----

    /// <summary>Package 97 (AC3): every list shows its loading state until the first page is complete; a refresh keeps the content.</summary>
    [Fact]
    public async Task Lists_ShowLoadingUntilFirstPage_RefreshKeepsContent()
    {
        var (admin, server, sent) = Create(P.UsersView | P.BansView | P.BackupsManage | P.LogsView | P.Speak);
        var time = (ManualTimeProvider)server.Time;
        await admin.RequestListsAsync();
        PendingActionViewModel[] loads = [admin.UsersAndBans.UsersLoad, admin.UsersAndBans.BansLoad, admin.BackupCard.BackupsLoad, admin.Logs.FilesLoad];
        Assert.All(loads, pendingAction => Assert.True(pendingAction.IsRunning && !pendingAction.IsBusy)); // nothing shows before 150 ms
        time.Advance(PendingActionViewModel.ShowAfter);
        Assert.All(loads, pendingAction => Assert.True(pendingAction.IsFirstLoad && !pendingAction.IsRefreshing));

        // the users come in two pages: loading until the list is complete
        var users = sent.OfType<ListUsers>().Single().RequestId;
        server.Apply(new UserList(users, [new KnownUserInfo("fpX", "Xaver", [])], 0, 2));
        Assert.True(admin.UsersAndBans.UsersLoad.IsFirstLoad);
        server.Apply(new UserList(users, [new KnownUserInfo("fpY", "Yvonne", [])], 1, 2));
        Assert.False(admin.UsersAndBans.UsersLoad.IsBusy);
        server.Apply(new BanList(sent.OfType<ListBans>().Single().RequestId, []));
        server.Apply(new BackupList(sent.OfType<ListBackups>().Single().RequestId, []));
        server.Apply(new LogList(sent.OfType<ListLogs>().Single().RequestId, []));
        Assert.All(loads, pendingAction => Assert.False(pendingAction.IsRunning || pendingAction.IsBusy));

        // a refresh: the old cards stay, with a small spinner instead of the loading state
        time.Advance(TimeSpan.FromSeconds(1));
        await admin.RequestListsAsync(); // Package 154: what each page's refresh button asks, all at once
        time.Advance(PendingActionViewModel.ShowAfter);
        Assert.All(loads, pendingAction => Assert.True(pendingAction.IsRefreshing && !pendingAction.IsFirstLoad));
        Assert.Equal(2, admin.UsersAndBans.Users.Count);

        // a list that never comes: a visible error after 10 s, and "Aktualisieren" asks again
        time.Advance(TimeSpan.FromSeconds(10));
        Assert.All(loads, pendingAction => Assert.Equal(Strings.Pending_NoAnswer, pendingAction.Error));
        Assert.All(loads, pendingAction => Assert.False(pendingAction.IsBusy));
        await admin.RequestListsAsync(); // Package 154: what each page's refresh button asks, all at once
        Assert.All(loads, pendingAction => Assert.True(pendingAction.IsRunning && pendingAction.Error is null));
    }

    /// <summary>Package 98 (AC4): the user card's actions spin until a user list asked for after them shows the change.</summary>
    [Fact]
    public async Task CardActions_Sent_BusyUntilConfirmed()
    {
        var dialogs = new AdminDialogs { UsersAndBans = new() { ConfirmDeleteUser = _ => Task.FromResult(true) } };
        var (admin, server, sent) = Create(P.UsersView | P.BansView | P.UserBan | P.UserDelete | P.UserMute | P.GroupsAssign | P.Speak | P.UserKick,
            dialogs: dialogs);
        var time = (ManualTimeProvider)server.Time;
        var xaver = Known("fpX", "Xaver") with { CanBeModeratedByMe = true, ServerMuted = true };
        admin.UsersAndBans.Apply(new UserList("r", [xaver]));
        KnownUserViewModel Card() => admin.UsersAndBans.Users.Single(knownUser => knownUser.Fingerprint == "fpX");

        await Card().LiftMuteCommand.ExecuteAsync(null);
        Assert.Single(sent.OfType<SetKnownUserServerMute>());
        time.Advance(PendingActionViewModel.ShowAfter);
        Assert.True(Card().Busy.IsBusy);
        admin.UsersAndBans.Apply(new UserList("r", [xaver])); // an older list: still waiting
        Assert.True(Card().Busy.IsBusy);
        var asked = sent.LastRequestId<ListUsers>();
        server.Apply(new UserList(asked, [xaver with { ServerMuted = false }]));
        Assert.False(Card().Busy.IsBusy);

        // a group toggle, refused: the card stops and shows why
        time.Advance(TimeSpan.FromSeconds(1));
        var box = Card().Toggles.Single(groupAssignment => groupAssignment.Name == "Moderator");
        box.IsChecked = true;
        await box.ToggleCommand.ExecuteAsync(null);
        var assign = sent.OfType<AssignGroup>().Single();
        time.Advance(PendingActionViewModel.ShowAfter);
        Assert.True(Card().Busy.IsBusy);
        server.Apply(new Error(assign.RequestId, ReasonCodes.PermissionDenied));
        Assert.False(Card().Busy.IsBusy);
        Assert.Equal(OVS.Client.Localization.ErrorTexts.For(ReasonCodes.PermissionDenied), Card().Busy.Error);

        // deleting: busy until the next list, where the card is gone
        time.Advance(TimeSpan.FromSeconds(1));
        await Card().DeleteCommand.ExecuteAsync(null);
        time.Advance(PendingActionViewModel.ShowAfter);
        Assert.True(Card().Busy.IsBusy);
        server.Apply(new UserList(sent.LastRequestId<ListUsers>(), []));
        Assert.Empty(admin.UsersAndBans.Users);
    }

    /// <summary>Package 102: a lifted ban folds away in the real list, the count is right at once.</summary>
    [Fact]
    public void LiftedBan_FoldsAway_CountAtOnce()
    {
        var (view, server, _) = Create(P.BansView | P.Speak);
        server.Post = action => action();
        server.Leave.Delay = TimeSpan.FromMinutes(1);
        var keep = new BanInfo(Guid.NewGuid(), "fpA", "Anton", null, "", "mod", null);
        var lifted = new BanInfo(Guid.NewGuid(), "fpX", "Xaver", null, "", "mod", null);
        view.UsersAndBans.Apply(new BanList("b", [keep, lifted]));
        view.UsersAndBans.Apply(new BanList("c", [keep]));
        Assert.Equal(2, view.UsersAndBans.Bans.Count);
        Assert.Equal("Anton", Assert.Single(view.UsersAndBans.Bans.Live()).Nickname);
        Assert.True(CollectionSync.IsLeaving(view.UsersAndBans.Bans, view.UsersAndBans.Bans.Single(ban => ban.Nickname == "Xaver")));
        Assert.False(view.UsersAndBans.HasNoBanMatches);
    }

    /// <summary>Package 109: a new group dropped before it was saved folds away like a deleted one.</summary>
    [Fact]
    public async Task UnsavedGroup_Dropped_FoldsAway()
    {
        var (view, server, _) = Create(P.GroupsView | P.GroupsCreate | P.Speak);
        server.Post = action => action();
        server.Leave.Delay = TimeSpan.FromMilliseconds(220);
        view.GroupList.NewGroupCommand.Execute(null);
        var fresh = view.GroupList.SelectedGroup!;
        Assert.Null(fresh.Id);
        view.GroupList.DeleteGroupCommand.Execute(null);
        Assert.Null(view.GroupList.SelectedGroup);
        Assert.Contains(fresh, view.GroupList.Groups); // still there, folding away
        Assert.True(CollectionSync.IsLeaving(view.GroupList.Groups, fresh));
        Assert.DoesNotContain(fresh, view.GroupList.Groups.Live());

        server.Apply(new GroupsChanged(server.Mirror.Groups.ToList())); // a rebuild meanwhile does not bring it back
        Assert.DoesNotContain(fresh, view.GroupList.Groups.Live());
        ((ManualTimeProvider)server.Time).Advance(TimeSpan.FromMilliseconds(220));
        await AsyncWait.UntilAsync(() => !view.GroupList.Groups.Contains(fresh)); // the removal runs on the pool
        Assert.DoesNotContain(fresh, view.GroupList.Groups);
        Assert.Equal(["Gast", "Moderator", "Admin"], view.GroupList.Groups.Select(groupEdit => groupEdit.Name));

        // no delay (the simplified display): gone at once
        server.Leave.Delay = TimeSpan.Zero;
        view.GroupList.NewGroupCommand.Execute(null);
        var second = view.GroupList.SelectedGroup!;
        view.GroupList.DeleteGroupCommand.Execute(null);
        Assert.DoesNotContain(second, view.GroupList.Groups);
        Assert.Null(view.GroupList.SelectedGroup);
    }

    [Fact]
    public async Task OnServerStateChanged_LogsDownloadRightLost_LogRowsLoseTheirCheckboxes()
    {
        var logFile = new LogFileInfo("server/2026-01-10_12-00-00.log", LogKind.Server, null, null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, 10);
        var time = new ManualTimeProvider();
        var server = FakeServers.Admin(time: time, reply: request => request is ListLogs ? new LogList(request.RequestId, [logFile]) : null);
        var page = new AdminViewModel(server);
        await HangGuard.BoundAsync(page.RequestListsAsync());
        Assert.True(page.Logs.Files.Single().CanSelect);

        FakeServers.SetOwnRightsAndGroups(server, P.All & ~P.LogsDownload, [WellKnownGroups.Admin]);

        Assert.False(page.Logs.Files.Single().CanSelect);
    }

    [Fact]
    public void OnServerStateChanged_ServerCarriesAnIconAndLosesItsPassword_IconSectionFollowsBeforeSettingsSectionAndPageTabs()
    {
        var (page, server, _) = Create(P.All, hasPassword: true);
        var order = new List<string?>();
        page.ServerIcon.PropertyChanged += (_, args) => order.Add("icon " + args.PropertyName);
        page.ServerSettings.PropertyChanged += (_, args) => order.Add("settings " + args.PropertyName);
        page.PropertyChanged += (_, args) => order.Add("page " + args.PropertyName);

        server.Apply(new ServerSettingsChanged(server.Mirror.Settings with { IconHash = "abc", HasPassword = false }));

        var iconFollowed = order.IndexOf("icon " + nameof(ServerIconViewModel.HasIcon));
        var settingsFollowed = order.IndexOf("settings " + nameof(ServerSettingsViewModel.HasPassword));
        var tabsNotified = order.IndexOf("page " + nameof(AdminViewModel.ShowGroups));
        Assert.Equal(0, iconFollowed);
        Assert.True(iconFollowed < settingsFollowed);
        Assert.True(settingsFollowed < tabsNotified);
    }

    [Fact]
    public void OnServerStateChanged_ServerReportsAnIconHash_IconSectionRaisesHasIconAndShowsIt()
    {
        var (page, server, _) = Create(P.All);
        var raised = new List<string?>();
        page.ServerIcon.PropertyChanged += (_, args) => raised.Add(args.PropertyName);

        server.Apply(new ServerSettingsChanged(server.Mirror.Settings with { IconHash = "abc" }));

        Assert.True(page.ServerIcon.HasIcon);
        Assert.Equal([nameof(ServerIconViewModel.HasIcon)], raised);
    }

    [Fact]
    public void Detach_ServerIconPngChangesAfterwards_IconSectionRaisesNothing()
    {
        var (page, server, _) = Create(P.All);
        var raised = new List<string?>();
        page.ServerIcon.PropertyChanged += (_, args) => raised.Add(args.PropertyName);
        page.Detach();

        server.IconPng = [1, 2, 3];

        Assert.Empty(raised);
    }

    [Fact]
    public async Task SaveAsync_NoPlaceChanged_ReturnsTrueAndSendsNothing()
    {
        var sent = new List<Request>();
        var admin = new AdminViewModel(FakeServers.Admin(sent, new ManualTimeProvider()));

        Assert.True(await HangGuard.BoundAsync(admin.SaveAsync()));
        Assert.Empty(sent);
    }

    [Fact]
    public async Task SaveAsync_SecondPlaceRefused_StopsThereAndReturnsFalse()
    {
        var sent = new List<Request>();
        var admin = new AdminViewModel(FakeServers.Admin(sent, new ManualTimeProvider(),
            reply: request => request is SetChannelLinks ? new Error(request.RequestId, ReasonCodes.PermissionDenied) : null));
        admin.Links.UnlinkAll();
        admin.ServerSettings.Name = "Neu";

        Assert.False(await HangGuard.BoundAsync(admin.SaveAsync()));
        Assert.Single(sent.OfType<SetChannelLinks>());
        Assert.DoesNotContain(sent, request => request is UpdateServerSettings);
    }

    [Fact]
    public async Task SaveAsync_AllPlacesConfirmed_ReturnsTrue()
    {
        var sent = new List<Request>();
        var admin = new AdminViewModel(FakeServers.Admin(sent, new ManualTimeProvider(), reply: request => request switch
        {
            SetChannelLinks => new ChannelsUnlinked(FakeServers.Lobby, FakeServers.Raid),
            UpdateServerSettings => new ServerSettingsChanged(new ServerSettingsInfo("Neu", "Hallo", true)),
            _ => null,
        }));
        admin.Links.UnlinkAll();
        admin.ServerSettings.Name = "Neu";

        Assert.True(await HangGuard.BoundAsync(admin.SaveAsync()));
        Assert.Single(sent.OfType<SetChannelLinks>());
        Assert.Single(sent.OfType<UpdateServerSettings>());
        Assert.False(admin.HasChanges);
    }

    /// <summary>Step 27a: the page wires the section's own group change mark to the server, so the server's update of the own groups plays no tone.</summary>
    [Fact]
    public async Task OwnGroupToggled_ServerUpdateBeforeTheList_NoGroupChangedTone()
    {
        var (admin, server, _) = Create(P.All, groupIds: [WellKnownGroups.Admin]);
        var heard = new List<OVS.Client.Audio.Sounds.SoundEvent>();
        server.SoundRequested += heard.Add;
        admin.UsersAndBans.Apply(new UserList("r", [Known("fp1", "ich", [WellKnownGroups.Admin])]));
        var guest = Assert.Single(admin.UsersAndBans.Users).Toggles.Single(toggle => toggle.Name == "Gast");
        guest.IsChecked = true;

        await HangGuard.BoundAsync(guest.ToggleCommand.ExecuteAsync(null));
        server.Apply(new UserUpdated(new UserInfo(1, "fp1", "ich", Lobby, false, false, false, P.All, [WellKnownGroups.Admin, WellKnownGroups.Guest])));

        Assert.DoesNotContain(OVS.Client.Audio.Sounds.SoundEvent.GroupChanged, heard);
    }

    /// <summary>Step 27h: the page closes while an own group change waits; another admin's change of the own groups is heard again.</summary>
    [Fact]
    public async Task Detach_OwnGroupChangeWaiting_GroupChangedToneComesBack()
    {
        var (admin, server, _) = Create(P.All, groupIds: [WellKnownGroups.Admin]);
        var heard = new List<OVS.Client.Audio.Sounds.SoundEvent>();
        server.SoundRequested += heard.Add;
        admin.UsersAndBans.Apply(new UserList("r", [Known("fp1", "ich", [WellKnownGroups.Admin])]));
        var guest = Assert.Single(admin.UsersAndBans.Users).Toggles.Single(toggle => toggle.Name == "Gast");
        guest.IsChecked = true;
        await HangGuard.BoundAsync(guest.ToggleCommand.ExecuteAsync(null));

        admin.Detach();
        server.Apply(new UserUpdated(new UserInfo(1, "fp1", "ich", Lobby, false, false, false, P.All, [WellKnownGroups.Admin, WellKnownGroups.Guest])));

        Assert.Contains(OVS.Client.Audio.Sounds.SoundEvent.GroupChanged, heard);
    }

    // ---- Step 26a: the page asks through its own record, and hands each section its part ----

    static AdminPlace CreateChangedPlace(Action discard) => new(() => true, () => Task.FromResult(false), discard);

    [Fact]
    public async Task ConfirmLeaveAsync_NoAdminDialogsAndChanges_DiscardsAndLeaves()
    {
        var (admin, _, _) = Create(P.All);
        var discarded = 0;

        Assert.True(await HangGuard.BoundAsync(admin.ConfirmLeaveAsync(CreateChangedPlace(() => discarded++))));
        Assert.Equal(1, discarded);
    }

    [Fact]
    public async Task ConfirmLeaveAsync_ChangesAndAnAdminRecord_AsksItsQuestionOnce()
    {
        var asked = 0;
        var (admin, _, _) = Create(P.All, dialogs: new AdminDialogs { AskLeave = () => { asked++; return Task.FromResult(LeaveChoice.Stay); } });

        Assert.False(await HangGuard.BoundAsync(admin.ConfirmLeaveAsync(CreateChangedPlace(() => { }))));
        Assert.Equal(1, asked);
    }

    [Fact]
    public async Task ConfirmLeaveAsync_AskLeaveThrows_PropagatesAndDiscardsNothing()
    {
        var (admin, _, _) = Create(P.All, dialogs: new AdminDialogs { AskLeave = () => throw new InvalidOperationException("Dialog kaputt") });
        var discarded = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(() => HangGuard.BoundAsync(admin.ConfirmLeaveAsync(CreateChangedPlace(() => discarded++))));
        Assert.Equal(0, discarded);
    }

    [Fact]
    public async Task Sections_AdminRecord_EachAsksThroughItsOwnPart()
    {
        var asked = new List<string>();
        var (admin, _, sent) = Create(P.All, dialogs: new AdminDialogs
        {
            UsersAndBans = new() { ConfirmDeleteUser = name => { asked.Add("delete " + name); return Task.FromResult(false); } },
            BackupCard = new() { ConfirmBackupDownload = () => { asked.Add("download"); return Task.FromResult(false); } },
        });
        admin.UsersAndBans.Apply(new UserList("r", [Known("fpX", "Xaver")]));

        await HangGuard.BoundAsync(Assert.Single(admin.UsersAndBans.Users).DeleteCommand.ExecuteAsync(null));
        Assert.False(await HangGuard.BoundAsync(admin.BackupCard.ConfirmDownloadAsync()));

        Assert.Equal(["delete Xaver", "download"], asked);
        Assert.DoesNotContain(sent, request => request is DeleteKnownUser);
    }

    [Fact]
    public void AdminDialogs_Default_SectionRecordsAreEmptyNotNull()
    {
        var dialogs = new AdminDialogs();

        Assert.Null(dialogs.AskLeave);
        Assert.Equal(new UsersAndBansDialogs(), dialogs.UsersAndBans);
        Assert.Equal(new BackupCardDialogs(), dialogs.BackupCard);
    }

    /// <summary>Package 154: each list page's refresh button asks for its own list only.</summary>
    [Fact]
    public async Task RefreshUsers_Clicked_OnlyUsersAsked()
    {
        var (admin, _, sent) = Create(P.All);
        await admin.UsersAndBans.RefreshUsersCommand.ExecuteAsync(null);
        Assert.Equal((1, 0, 0, 0), (sent.OfType<ListUsers>().Count(), sent.OfType<ListBans>().Count(), sent.OfType<ListLogs>().Count(), sent.OfType<ListBackups>().Count()));
    }

    /// <summary>Package 154: the bans page's refresh button.</summary>
    [Fact]
    public async Task RefreshBans_Clicked_OnlyBansAsked()
    {
        var (admin, _, sent) = Create(P.All);
        await admin.UsersAndBans.RefreshBansCommand.ExecuteAsync(null);
        Assert.Equal((0, 1, 0, 0), (sent.OfType<ListUsers>().Count(), sent.OfType<ListBans>().Count(), sent.OfType<ListLogs>().Count(), sent.OfType<ListBackups>().Count()));
    }

    /// <summary>Package 154: the logs page's refresh button.</summary>
    [Fact]
    public async Task RefreshLogs_Clicked_OnlyLogsAsked()
    {
        var (admin, _, sent) = Create(P.All);
        await admin.Logs.RefreshCommand.ExecuteAsync(null);
        Assert.Equal((0, 0, 1, 0), (sent.OfType<ListUsers>().Count(), sent.OfType<ListBans>().Count(), sent.OfType<ListLogs>().Count(), sent.OfType<ListBackups>().Count()));
    }

    /// <summary>Package 154: the backups page's refresh button.</summary>
    [Fact]
    public async Task RefreshBackups_Clicked_OnlyBackupsAsked()
    {
        var (admin, _, sent) = Create(P.All);
        await admin.BackupCard.RefreshCommand.ExecuteAsync(null);
        Assert.Equal((0, 0, 0, 1), (sent.OfType<ListUsers>().Count(), sent.OfType<ListBans>().Count(), sent.OfType<ListLogs>().Count(), sent.OfType<ListBackups>().Count()));
    }

    /// <summary>Package 154: a group title shows while one of its pages does.</summary>
    [Theory]
    [InlineData(P.All, true, true, true)]
    [InlineData(P.UsersView | P.UserBan, false, true, false)]
    [InlineData(P.LogsView, false, false, true)]
    public void GroupTitles_Rights_ShownWithTheirPages(P perms, bool server, bool members, bool manage)
    {
        var (admin, _, _) = Create(perms);
        Assert.Equal((server, members, manage), (admin.ShowServerGroup, admin.ShowMembersGroup, admin.ShowManageGroup));
    }

    sealed record BanButtonsShown(bool Ban, bool Unban, bool Delete);

    sealed record AdminPageRig(AdminViewModel Admin, ServerViewModel Server, List<Request> Sent);

    sealed record MatrixRig(LinkMatrixViewModel Matrix, ServerViewModel Server, List<Request> Sent, Guid[] Channels);
}
