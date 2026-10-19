using System.Globalization;
using OVS.Client.Collections;
using OVS.Client.Dialogs;
using OVS.Client.Localization;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OVS.Client.Models;
using OVS.Client.Services;
using OVS.Shared.Moderation;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.Connection;
using OVS.Shared.Protocol.Messages.Groups;
using OVS.Shared.Protocol.Messages.Moderation;
using OVS.Shared.Protocol.Messages.Users;

namespace OVS.Client.ViewModels;

/// <summary>
/// The users and bans tabs of the administration: the overviews with search, filters and order, the actions of a user
/// card, the group toggles on it, and the refresh of both lists they share.
/// </summary>
internal sealed partial class UsersAndBansViewModel : ObservableObject
{
    readonly ServerViewModel server;
    readonly UsersAndBansDialogs dialogs;
    readonly Action<bool> markOwnGroupChange;
    IReadOnlyList<KnownUserInfo> knownUsers = [];
    /// <summary>Package 155: the fingerprints of the rows the user opened.</summary>
    readonly HashSet<string> expandedUsers = [];
    // Package 87: the lists come in pages, collected until complete
    readonly PagedListCollector<KnownUserInfo> userPages = new();
    readonly PagedListCollector<BanInfo> banPages = new();
    // every refresh of the user and ban list goes through these, so quick actions never exceed the server's list limit
    readonly ListRefresh userRefresh, banRefresh;

    // Package 71: search, filters and order of the user overview survive every refresh
    [ObservableProperty] string searchText = "";
    [ObservableProperty] Choice<UserStatusFilter> selectedStatusFilter;
    [ObservableProperty] Choice<Guid?>? selectedGroupFilter;
    [ObservableProperty] Choice<UserSortOrder> selectedSortOrder;
    [ObservableProperty] string userCountText = "";
    // Package 80: the same for the ban overview; active bans by default
    IReadOnlyList<BanInfo> allBans = [];
    [ObservableProperty] string banSearchText = "";
    [ObservableProperty] Choice<BanStatusFilter> selectedBanStatusFilter;
    [ObservableProperty] Choice<BanTypeFilter> selectedBanTypeFilter;
    [ObservableProperty] Choice<BanSortOrder> selectedBanSortOrder;
    [ObservableProperty] string banCountText = "";
    HashSet<string> onlineFingerprints;
    // Package 92: the rights the user list's rank flags were judged by; the list is asked for again when they change
    Permission judgedActor;
    IReadOnlyList<GroupInfo> judgedGroups;
    bool detached;

    /// <param name="server">The connected server: its lists, requests, rights and tones.</param>
    /// <param name="markOwnGroupChange">
    /// Tells the server whether an own group change of this section is still on its way (true is set before the request
    /// goes out, as the server's update can come at once), so the server plays no "groups changed" tone for it.
    /// </param>
    /// <param name="dialogs">The ban and delete questions; unset means cancelled.</param>
    internal UsersAndBansViewModel(ServerViewModel server, Action<bool> markOwnGroupChange, UsersAndBansDialogs? dialogs = null)
    {
        this.server = server;
        this.markOwnGroupChange = markOwnGroupChange;
        this.dialogs = dialogs ?? new UsersAndBansDialogs();
        UsersLoad = server.NewPending();
        BansLoad = server.NewPending();
        server.Pictures.Arrived += OnPictureArrived; // Package 168
        userRefresh = new("u", () => !detached && ShowUsers, id => SendRoutedAsync(new ListUsers(), id, UsersLoad), server.Time);
        banRefresh = new("b", () => !detached && ShowBans, id => SendRoutedAsync(new ListBans(), id, BansLoad), server.Time);
        // Package 97: a list that never came no longer blocks the next request ("Aktualisieren" retries)
        UsersLoad.TimedOut += userRefresh.Completed;
        BansLoad.TimedOut += banRefresh.Completed;
        StatusFilters =
        [
            new(UserStatusFilter.All, Strings.Ui_StatusAll), new(UserStatusFilter.Online, Strings.Ui_StatusOnline),
            new(UserStatusFilter.Offline, Strings.Ui_StatusOffline), new(UserStatusFilter.Banned, Strings.Ui_StatusBanned),
        ];
        SortOrders =
        [
            new(UserSortOrder.Name, Strings.Ui_SortName), new(UserSortOrder.LastLogin, Strings.Ui_SortLastLogin),
            new(UserSortOrder.OnlineTime, Strings.Ui_SortOnlineTime),
        ];
        selectedStatusFilter = StatusFilters[0];
        selectedSortOrder = SortOrders[0];
        BanStatusFilters =
        [
            new(BanStatusFilter.Active, Strings.Ui_BanStatusActive), new(BanStatusFilter.Expired, Strings.Ui_BanStatusExpired),
            new(BanStatusFilter.Lifted, Strings.Ui_BanStatusLifted), new(BanStatusFilter.All, Strings.Ui_BanStatusAll),
        ];
        BanTypeFilters =
        [
            new(BanTypeFilter.All, Strings.Ui_BanTypeAll), new(BanTypeFilter.Permanent, Strings.Ui_BanTypePermanent),
            new(BanTypeFilter.Temporary, Strings.Ui_BanTypeTemporary), new(BanTypeFilter.WithIp, Strings.Ui_BanTypeWithIp),
        ];
        BanSortOrders =
        [
            new(BanSortOrder.Newest, Strings.Ui_SortNewest), new(BanSortOrder.EndingSoonest, Strings.Ui_SortEndingSoonest),
            new(BanSortOrder.MostAttempts, Strings.Ui_SortMostAttempts), new(BanSortOrder.Name, Strings.Ui_SortName),
        ];
        selectedBanStatusFilter = BanStatusFilters[0];
        selectedBanTypeFilter = BanTypeFilters[0];
        selectedBanSortOrder = BanSortOrders[0];
        userActions = new(Ban: BanUserAsync, Unban: UnbanUserAsync, Delete: DeleteUserAsync, LiftMute: LiftMuteAsync);
        onlineFingerprints = OnlineFingerprints();
        judgedActor = Actor;
        judgedGroups = server.Mirror.Groups;
        RebuildGroupFilters();
        RebuildUsers();
    }

    Permission Actor => server.SelfPermissions;

    // Package 76: seeing a tab and acting in it are separate rights
    internal bool ShowUsers => Actor.Has(Permission.UsersView);

    internal bool ShowBans => Actor.Has(Permission.BansView);

    /// <summary>Package 97 (A113): the lists' loading states.</summary>
    internal PendingActionViewModel UsersLoad { get; }
    internal PendingActionViewModel BansLoad { get; }

    /// <summary>Package 71: the known users that pass search and filters, in the chosen order.</summary>
    internal ObservableCollection<KnownUserViewModel> Users { get; } = [];

    /// <summary>Package 168: a picture asked for by a card arrived.</summary>
    void OnPictureArrived(OVS.Shared.Protocol.AvatarHash hash, byte[] png)
    {
        foreach (var card in Users.Where(card => card.Info.Avatar == hash)) card.AvatarPng = png;
    }
    internal IReadOnlyList<Choice<UserStatusFilter>> StatusFilters { get; }
    internal ObservableCollection<Choice<Guid?>> GroupFilters { get; } = [];
    internal IReadOnlyList<Choice<UserSortOrder>> SortOrders { get; }

    /// <summary>Package 80: the bans that pass search and filters, in the chosen order.</summary>
    internal ObservableCollection<BanViewModel> Bans { get; } = [];
    internal IReadOnlyList<Choice<BanStatusFilter>> BanStatusFilters { get; }
    internal IReadOnlyList<Choice<BanTypeFilter>> BanTypeFilters { get; }
    internal IReadOnlyList<Choice<BanSortOrder>> BanSortOrders { get; }

    /// <summary>No ban stored at all (none ever, or all aged out); otherwise an empty list means nothing matches.</summary>
    internal bool HasNoBans => allBans.Count == 0;
    internal bool HasNoBanMatches => allBans.Count > 0 && !Bans.Live().Any();

    /// <summary>Package 102: the filter leaves nobody (rows folding away do not count).</summary>
    internal bool HasNoUsersShown => !Users.Live().Any();

    /// <summary>Asks for the user list and the ban list (each at most once a second) and waits for the requests to go out.</summary>
    internal async Task RefreshAsync()
    {
        var users = userRefresh.RequestAsync();
        await banRefresh.RequestAsync();
        await users;
    }

    /// <summary>Package 154: asks for the user list only (the members page's refresh button).</summary>
    [RelayCommand]
    internal Task RefreshUsersAsync() => userRefresh.RequestAsync();

    /// <summary>Package 154: asks for the ban list only (the bans page's refresh button).</summary>
    [RelayCommand]
    internal Task RefreshBansAsync() => banRefresh.RequestAsync();

    /// <summary>
    /// The page closed: no more requests, and no answer reaches this section. Group changes still waiting for a user list
    /// are forgotten and the own one unmarked, since a closed page can confirm none of them (Step 27h).
    /// </summary>
    internal void Detach()
    {
        server.Pictures.Arrived -= OnPictureArrived;
        // ponytail: every list request keeps its route until here (a list refresh at most once a second); drop a list
        // round's route once a newer round is complete if pages ever stay open for days
        detached = true;
        server.Routes.RemoveAll(Apply);
        pendingGroupChanges.Clear();
        UpdateOwnGroupChangePending();
    }

    /// <summary>The server's state changed: the group filter, the online status and the cards follow.</summary>
    internal void FollowServer()
    {
        RebuildGroupFilters();
        // Package 71: online status at once from the live state, the stored data follows with a new list
        var online = OnlineFingerprints();
        // Package 92: new own rights or group rights change on whom one may act; the server judges that anew
        bool rejudge = Actor != judgedActor || server.Mirror.Groups != judgedGroups;
        if (!online.SetEquals(onlineFingerprints) || rejudge) _ = userRefresh.RequestAsync();
        onlineFingerprints = online;
        (judgedActor, judgedGroups) = (Actor, server.Mirror.Groups);
        RebuildUsers();
    }

    /// <summary>Sends a request whose answers, and the server's error for it, come back to <see cref="Apply"/>.</summary>
    Task<string> SendRoutedAsync(Request request, string? id = null, PendingActionViewModel? pending = null) => server.SendAsync(request, id, pending, Apply);

    /// <summary>The server's answers to this section's own requests (sent with this as their route) and its errors for them.</summary>
    internal void Apply(Message answer)
    {
        switch (answer)
        {
            case Error error:
                userRefresh.Failed(error.RequestId);
                banRefresh.Failed(error.RequestId);
                if (pendingGroupChanges.RemoveAll(change => change.RequestId == error.RequestId) > 0) UpdateOwnGroupChangePending();
                break;
            case UserList list:
                if (userPages.Add(list.RequestId, list.Offset, list.Total, list.Users, (roundId, offset) => _ = SendRoutedAsync(new ListUsers(offset), roundId)) is not { } users) break;
                userRefresh.Completed();
                UsersLoad.Done();
                ConfirmCards(userRefresh.RoundOf(list.RequestId));
                ConfirmGroupChanges(users, userRefresh.RoundOf(list.RequestId));
                knownUsers = users;
                RebuildUsers();
                break;
            case BanList list:
                if (banPages.Add(list.RequestId, list.Offset, list.Total, list.Bans, (roundId, offset) => _ = SendRoutedAsync(new ListBans(offset), roundId)) is not { } bans) break;
                banRefresh.Completed();
                BansLoad.Done();
                foreach (var busy in banBusy.Values) busy.Done(); // Package 98: an unban is answered with the new list
                allBans = bans;
                RebuildBans();
                break;
        }
    }

    // ---- Package 71: user overview ----

    partial void OnSearchTextChanged(string value) => RebuildUsers();
    partial void OnSelectedStatusFilterChanged(Choice<UserStatusFilter> value) => RebuildUsers();
    partial void OnSelectedGroupFilterChanged(Choice<Guid?>? value) => RebuildUsers();
    partial void OnSelectedSortOrderChanged(Choice<UserSortOrder> value) => RebuildUsers();

    HashSet<string> OnlineFingerprints() => server.Mirror.Users.Values.Select(user => user.Fingerprint).ToHashSet();

    /// <summary>Keeps the chosen group when the groups change; a deleted group falls back to "all".</summary>
    void RebuildGroupFilters()
    {
        var wanted = new List<Choice<Guid?>> { new(null, Strings.Ui_AllGroups) };
        wanted.AddRange(server.Mirror.Groups.Select(group => new Choice<Guid?>(group.Id, group.Name)));
        if (wanted.SequenceEqual(GroupFilters)) return;
        var selected = SelectedGroupFilter?.Value;
        GroupFilters.Clear();
        foreach (var choice in wanted) GroupFilters.Add(choice);
        SelectedGroupFilter = GroupFilters.FirstOrDefault(choice => choice.Value == selected) ?? GroupFilters[0];
    }

    /// <summary>Builds the cards from the stored users and shows the ones that pass search, filters and order.</summary>
    void RebuildUsers()
    {
        var now = server.Time.GetUtcNow();
        var search = SearchText.Trim();
        var status = SelectedStatusFilter?.Value ?? UserStatusFilter.All;
        var filterGroupId = SelectedGroupFilter?.Value;
        var adminCount = knownUsers.Count(user => user.GroupIds.Contains(WellKnownGroups.Admin));
        var all = knownUsers.Select(user =>
        {
            // Package 72, 84 (A102): the server checks the same; never oneself, only strictly weaker users, never the last admin.
            // Package 92: the rank comes judged with the list, the client knows no rights of others.
            var weaker = user.CanBeModeratedByMe;
            var isLastAdmin = GroupRules.IsLastAdmin(user.GroupIds, adminCount);
            var toggles = server.Mirror.Groups.Select(group => new GroupAssignmentViewModel(group.Id, group.Name, IsInGroup(user, group.Id),
                group.AssignableByMe && weaker, // Package 92: judged by the server, group rights only reach GroupsView holders
                toggle => ToggleUserGroupAsync(user.Fingerprint, toggle))).ToList();
            var activeBans = (user.Bans ?? []).Where(ban => BanStatusRule.Evaluate(ban.LiftedAt, ban.ExpiresAt, now) == BanStatus.Active).ToList();
            return new KnownUserViewModel(user, onlineFingerprints.Contains(user.Fingerprint), toggles, activeBans, userActions, CardBusy(user.Fingerprint),
                canBan: Actor.Has(Permission.UserBan) && weaker && !isLastAdmin && activeBans.Count == 0,
                canUnban: Actor.Has(Permission.UserBan) && weaker && activeBans.Count > 0,
                canDelete: Actor.Has(Permission.UserDelete) && weaker && !isLastAdmin,
                canLiftMute: Actor.Has(Permission.UserMute) && weaker && user.ServerMuted, // Package 85
                isExpanded: expandedUsers.Contains(user.Fingerprint), // Package 155: an open row stays open when the list comes again
                expandedChanged: expanded => { if (expanded) expandedUsers.Add(user.Fingerprint); else expandedUsers.Remove(user.Fingerprint); })
            {
                AvatarPng = server.Pictures.Png(user.Avatar), // Package 168
            };
        }).ToList();
        var visible = all.Where(card => card.Matches(search) && (filterGroupId is null || card.Info.GroupIds.Contains(filterGroupId.Value)) && status switch
        {
            UserStatusFilter.Online => card.IsOnline,
            UserStatusFilter.Offline => !card.IsOnline,
            UserStatusFilter.Banned => card.IsBanned,
            _ => true,
        });
        var byName = StringComparer.CurrentCultureIgnoreCase;
        visible = (SelectedSortOrder?.Value ?? UserSortOrder.Name) switch
        {
            UserSortOrder.LastLogin => visible.OrderByDescending(card => card.Info.LastLogin ?? DateTimeOffset.MinValue).ThenBy(card => card.Nickname, byName),
            UserSortOrder.OnlineTime => visible.OrderByDescending(card => card.Info.OnlineTime).ThenBy(card => card.Nickname, byName),
            _ => visible.OrderBy(card => card.Nickname, byName),
        };
        var shown = visible.ToList();
        CollectionSync.Sync(Users, shown, server.Leave);
        UserCountText = TextFormat.Of(Strings.Ui_UserCount, shown.Count, all.Count);
        OnPropertyChanged(nameof(HasNoUsersShown));
    }

    // ---- Package 80: ban overview ----

    partial void OnBanSearchTextChanged(string value) => RebuildBans();
    partial void OnSelectedBanStatusFilterChanged(Choice<BanStatusFilter> value) => RebuildBans();
    partial void OnSelectedBanTypeFilterChanged(Choice<BanTypeFilter> value) => RebuildBans();
    partial void OnSelectedBanSortOrderChanged(Choice<BanSortOrder> value) => RebuildBans();

    void RebuildBans()
    {
        var now = server.Time.GetUtcNow();
        var search = BanSearchText.Trim();
        var status = SelectedBanStatusFilter?.Value ?? BanStatusFilter.All;
        var type = SelectedBanTypeFilter?.Value ?? BanTypeFilter.All;
        var all = allBans.Select(ban => new BanViewModel(ban, now, Actor.Has(Permission.UserBan) && MayActOnBanned(ban.Fingerprint), async () =>
        {
            await SendRoutedAsync(new Unban(ban.Id), pending: BanBusy(ban.Id)); // answered with the new ban list
            await userRefresh.RequestAsync(); // Package 72: the user cards follow
        }, BanBusy(ban.Id))).ToList();
        var visible = all.Where(row => row.Matches(search) && status switch
        {
            BanStatusFilter.Active => row.Status == BanStatus.Active,
            BanStatusFilter.Expired => row.Status == BanStatus.Expired,
            BanStatusFilter.Lifted => row.Status == BanStatus.Lifted,
            _ => true,
        } && type switch
        {
            BanTypeFilter.Permanent => row.Ban.ExpiresAt is null,
            BanTypeFilter.Temporary => row.Ban.ExpiresAt is not null,
            BanTypeFilter.WithIp => row.HasIp,
            _ => true,
        });
        var byName = StringComparer.CurrentCultureIgnoreCase;
        visible = (SelectedBanSortOrder?.Value ?? BanSortOrder.Newest) switch
        {
            BanSortOrder.EndingSoonest => visible.OrderBy(row => row.Ban.ExpiresAt ?? DateTimeOffset.MaxValue).ThenBy(row => row.Nickname, byName),
            BanSortOrder.MostAttempts => visible.OrderByDescending(row => row.Ban.BlockedAttempts).ThenBy(row => row.Nickname, byName),
            BanSortOrder.Name => visible.OrderBy(row => row.Nickname, byName),
            _ => visible.OrderByDescending(row => row.Ban.CreatedAt ?? DateTimeOffset.MinValue).ThenBy(row => row.Nickname, byName),
        };
        var shown = visible.ToList();
        CollectionSync.Sync(Bans, shown, server.Leave);
        BanCountText = TextFormat.Of(Strings.Ui_BanCount, shown.Count, all.Count);
        OnPropertyChanged(nameof(HasNoBans));
        OnPropertyChanged(nameof(HasNoBanMatches));
    }

    /// <summary>
    /// Package 84 (A102), 92: the server's judgement from the user list or the live state; anyone but oneself when the user
    /// is unknown here (without UsersView the list is missing, then the server has the last word).
    /// </summary>
    bool MayActOnBanned(string fingerprint) =>
        knownUsers.FirstOrDefault(user => user.Fingerprint == fingerprint)?.CanBeModeratedByMe
        ?? server.Mirror.Users.Values.FirstOrDefault(user => user.Fingerprint == fingerprint)?.CanBeModeratedByMe
        ?? fingerprint != server.Mirror.Self?.Fingerprint;

    // ---- Package 72: ban, unban and delete from a user card, online or offline ----

    /// <summary>What the user cards call: the cards know no page, only these.</summary>
    readonly KnownUserActions userActions;

    /// <summary>Package 98: the ban dialog stays open until a user list shows the ban.</summary>
    Task BanUserAsync(KnownUserViewModel user)
    {
        var ipKnown = user.Info.LastIp is not null;
        return ServerViewModel.AskAsync<BanChoice>(dialogs.Ban is { } showBanDialog ? submit => showBanDialog(user.Nickname, ipKnown, submit) : null,
            choice => CardAction(user.Fingerprint, new BanKnownUser(user.Fingerprint, choice.Reason, choice.DurationMinutes, choice.IncludeIp && ipKnown), bans: true));
    }

    Task UnbanUserAsync(KnownUserViewModel user)
    {
        foreach (var ban in user.Bans) CardAction(user.Fingerprint, new Unban(ban.Id)); // each answered with the new ban list
        return Task.CompletedTask;
    }

    /// <summary>Package 85: works offline too; the card follows with the next user list.</summary>
    Task LiftMuteAsync(KnownUserViewModel user)
    {
        CardAction(user.Fingerprint, new SetKnownUserServerMute(user.Fingerprint, false));
        return Task.CompletedTask;
    }

    async Task DeleteUserAsync(KnownUserViewModel user)
    {
        if (dialogs.ConfirmDeleteUser is not { } confirm || !await confirm(user.Nickname)) return;
        CardAction(user.Fingerprint, new DeleteKnownUser(user.Fingerprint), bans: true);
    }

    // ---- Package 98 (A113): a card's actions spin until a user list asked for after them shows the result ----

    readonly Dictionary<string, PendingActionViewModel> cardBusy = [];
    /// <summary>Per card: the user list requests sent before its last action; a list of a later request settles it.</summary>
    readonly Dictionary<string, int> cardAskedBefore = [];
    readonly Dictionary<Guid, PendingActionViewModel> banBusy = [];

    PendingActionViewModel CardBusy(string fingerprint) => cardBusy.TryGetValue(fingerprint, out var busy) ? busy : cardBusy[fingerprint] = server.NewPending();
    PendingActionViewModel BanBusy(Guid banId) => banBusy.TryGetValue(banId, out var busy) ? busy : banBusy[banId] = server.NewPending();

    /// <summary>Sends under the card's mark, then asks for the list (and with bans the ban list) that shows the result.</summary>
    PendingActionViewModel CardAction(string fingerprint, Request request, bool bans = false)
    {
        var busy = CardBusy(fingerprint);
        cardAskedBefore[fingerprint] = userRefresh.Sent;
        _ = SendAsync();
        return busy;

        async Task SendAsync()
        {
            await SendRoutedAsync(request, pending: busy); // an unban's own answer: the new ban list
            if (bans) await RefreshAsync();
            else await userRefresh.RequestAsync();
        }
    }

    /// <param name="round">The number of the user list request a complete list answers, 0 when unknown.</param>
    void ConfirmCards(int round)
    {
        foreach (var (fingerprint, askedBeforeCount) in cardAskedBefore.ToList())
        {
            if (round <= askedBeforeCount) continue;
            cardAskedBefore.Remove(fingerprint);
            if (cardBusy.GetValueOrDefault(fingerprint) is { IsRunning: true } busy) busy.Done();
        }
    }

    /// <summary>Package 73: group changes sent but not yet seen in a user list.</summary>
    readonly List<PendingGroupChange> pendingGroupChanges = [];

    /// <summary>A box just ticked keeps its state until a list asked for after the tick arrives; an older list would flip it back.</summary>
    bool IsInGroup(KnownUserInfo user, Guid groupId) =>
        pendingGroupChanges.LastOrDefault(change => change.Fingerprint == user.Fingerprint && change.GroupId == groupId) is { } pending
            ? pending.Assigned
            : user.GroupIds.Contains(groupId);

    async Task ToggleUserGroupAsync(string fingerprint, GroupAssignmentViewModel toggle)
    {
        bool own = fingerprint == server.Mirror.Self?.Fingerprint;
        if (own) markOwnGroupChange(true); // set before sending: the server's update can come at once
        cardAskedBefore[fingerprint] = userRefresh.Sent; // Package 98: the card spins until a later list shows it
        var id = await SendRoutedAsync(toggle.IsChecked ? new AssignGroup(fingerprint, toggle.GroupId) : new UnassignGroup(fingerprint, toggle.GroupId),
            pending: CardBusy(fingerprint)); // its error drops the change
        pendingGroupChanges.Add(new PendingGroupChange(id, fingerprint, toggle.GroupId, toggle.IsChecked, userRefresh.Sent));
        UpdateOwnGroupChangePending();
        await userRefresh.RequestAsync();
    }

    void UpdateOwnGroupChangePending()
    {
        var self = server.Mirror.Self?.Fingerprint;
        markOwnGroupChange(pendingGroupChanges.Any(change => change.Fingerprint == self));
    }

    /// <summary>Package 73: a tone once a user list shows a pending change; also works for users who are offline.</summary>
    /// <param name="users">The complete user list.</param>
    /// <param name="round">The number of the user list request this list answers, 0 when unknown.</param>
    void ConfirmGroupChanges(IReadOnlyList<KnownUserInfo> users, int round)
    {
        int confirmedCount = pendingGroupChanges.RemoveAll(change => change.IsShownIn(users));
        pendingGroupChanges.RemoveAll(change => change.IsSettledBy(round)); // asked for after the change and still not showing it: the list wins
        UpdateOwnGroupChangePending();
        if (confirmedCount > 0) server.RequestSound(Audio.Sounds.SoundEvent.GroupChangedByMe);
    }
}
