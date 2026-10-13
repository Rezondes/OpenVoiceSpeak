using System.Runtime.CompilerServices;
using OVS.Client.Dialogs;
using OVS.Client.Localization;
using OVS.Client.Services;
using OVS.Client.ViewModels;
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
using static OVS.Tests.TestSupport.HangGuard;

namespace OVS.Tests.Client.ViewModels;

/// <summary>
/// The server's answers go by request id to the route their request was sent with (ServerViewModel.Routes), and the
/// administration page, its logs tab and its transfer send each request that needs its answer with their own route.
/// </summary>
public sealed class AnswerRoutingTests() : TempDirectoryTest("ovs-routing-", quiet: false)
{
    readonly ManualTimeProvider time = new();
    readonly List<Request> sent = [];
    readonly List<Message> received = [];

    ServerViewModel Server(Func<Request, Message?>? reply = null) => FakeServers.Admin(sent, time, reply: reply);

    // ---- The routes of ServerViewModel ----

    [Fact]
    public async Task SendAsync_AnsweredBeforeTheIdReturns_ReachesTheRoute()
    {
        var server = Server(reply: request => new BackupList(request.RequestId, []));

        var id = await Bound(server.SendAsync(new ListBackups(), route: received.Add));

        Assert.Equal(id, Assert.IsType<BackupList>(Assert.Single(received)).RequestId);
    }

    /// <summary>Step 27c, routes question (3): a send that fails leaves the route to its owner, who removes it (Detach, the next transfer).</summary>
    [Fact]
    public async Task SendAsync_SendThrows_RouteStaysUntilItsOwnerRemovesIt()
    {
        var server = Server(reply: _ => throw new IOException("Verbindung weg"));
        var notices = new List<string>();
        server.Notice += notices.Add;
        var pending = server.NewPending();
        Action<Message> route = received.Add;

        var id = await Bound(server.SendAsync(new ListBackups(), pending: pending, route: route));

        Assert.Equal(ErrorTexts.For(ReasonCodes.ConnectionLost), pending.Error);
        Assert.Equal([ErrorTexts.For(ReasonCodes.ConnectionLost)], notices);
        Assert.Same(route, server.Routes.Find(id));
        server.Routes.RemoveAll(route);
        Assert.Null(server.Routes.Find(id));
    }

    [Fact]
    public async Task Apply_SecondAnswerUnderTheId_ReachesTheRouteToo()
    {
        var server = Server();
        var id = await Bound(server.SendAsync(new ListBackups(), route: received.Add));
        BackupList first = new(id, [], 0, 2), second = new(id, [], 1, 2);

        server.Apply(first);
        server.Apply(second);

        Assert.Equal([first, second], received);
    }

    [Fact]
    public async Task Apply_AnswerToAnotherRequest_ReachesNoRoute()
    {
        var server = Server();
        await Bound(server.SendAsync(new ListBackups(), route: received.Add));
        var other = await Bound(server.SendAsync(new ListBackups())); // without a route

        server.Apply(new BackupList(other, []));
        server.Apply(new BackupList(null, []));

        Assert.Empty(received);
    }

    [Fact]
    public async Task RemoveAll_TwoHandlers_OnlyTheRemovedOneGetsNothing()
    {
        var server = Server();
        var kept = new List<Message>();
        var first = await Bound(server.SendAsync(new ListBackups(), route: received.Add));
        var second = await Bound(server.SendAsync(new ListBackups(), route: received.Add));
        var other = await Bound(server.SendAsync(new ListBans(), route: kept.Add));

        server.Routes.RemoveAll(received.Add);
        server.Apply(new BackupList(first, []));
        server.Apply(new BackupList(second, []));
        server.Apply(new BanList(other, []));

        Assert.Empty(received);
        Assert.Single(kept);
        Assert.Equal([null, null], new[] { first, second }.Select(server.Routes.Find));
    }

    [Fact]
    public async Task Apply_ErrorForARoutedRequest_ReachesTheRouteWithNoticeAndPendingError()
    {
        var server = Server();
        var notices = new List<string>();
        server.Notice += notices.Add;
        var pending = server.NewPending();
        var id = await Bound(server.SendAsync(new ListBackups(), pending: pending, route: received.Add));
        var error = new Error(id, ReasonCodes.PermissionDenied);

        server.Apply(error);

        Assert.Same(error, Assert.Single(received));
        Assert.Equal([ErrorTexts.For(ReasonCodes.PermissionDenied)], notices);
        Assert.Equal(ErrorTexts.For(ReasonCodes.PermissionDenied), pending.Error);
    }

    [Fact]
    public void Apply_ListsWithoutARoute_LastUserAndBanListKept()
    {
        var server = Server();
        var users = new UserList("u9", []);
        var bans = new BanList("b9", []);

        server.Apply(users);
        server.Apply(bans);

        Assert.Same(users, server.LastUserList);
        Assert.Same(bans, server.LastBanList);
    }

    /// <summary>The answers that carry a request id but are not routed, why, and one of each.</summary>
    static readonly Dictionary<string, (string Reason, Func<string, Message> Sample)> AnswersNotRouted = new()
    {
        [nameof(ChannelAdded)] = ("a change every client gets; the creator's copy confirms its mark (SendConfirmed matches the id)",
            id => new ChannelAdded(new ChannelInfo(Guid.NewGuid(), "Neu", "", 9), id)),
        [nameof(ServerIcon)] = ("goes to IconReceived, which takes a logo only when it matches the current hash", id => new ServerIcon(id, null, null)),
    };

    static IEnumerable<Type> AnswerTypes() => typeof(Message).Assembly.GetTypes()
        .Where(candidate => candidate.IsSubclassOf(typeof(Message)) && !candidate.IsAbstract && !candidate.IsSubclassOf(typeof(Request)) && candidate.GetProperty("RequestId") is not null)
        .OrderBy(candidate => candidate.Name, StringComparer.Ordinal);

    public static TheoryData<string> RoutedAnswerNames => new(AnswerTypes().Select(candidate => candidate.Name).Where(name => !AnswersNotRouted.ContainsKey(name)));
    public static TheoryData<string> NotRoutedAnswerNames => new(AnswersNotRouted.Keys);

    [Fact]
    public void AnswersNotRouted_EveryEntry_AnAnswerType()
    {
        Assert.Subset(AnswerTypes().Select(candidate => candidate.Name).ToHashSet(), AnswersNotRouted.Keys.ToHashSet());
    }

    /// <summary>Every answer with a request id reaches the route of its request, unless listed above with its reason.</summary>
    [Theory]
    [MemberData(nameof(RoutedAnswerNames))]
    public async Task Apply_EveryAnswerType_ReachesTheRouteOfItsRequest(string typeName)
    {
        var type = AnswerTypes().Single(candidate => candidate.Name == typeName);
        var server = Server();
        var id = await Bound(server.SendAsync(new ListBackups(), route: received.Add));
        var answer = type == typeof(Error) ? new Error(id, ReasonCodes.InvalidValue) : (Message)RuntimeHelpers.GetUninitializedObject(type);
        type.GetProperty("RequestId")!.SetValue(answer, id);

        server.Apply(answer);

        Assert.Same(answer, Assert.Single(received));
    }

    [Theory]
    [MemberData(nameof(NotRoutedAnswerNames))]
    public async Task Apply_AnswerListedAsNotRouted_SkipsTheRouteOfItsRequest(string typeName)
    {
        var server = Server();
        var id = await Bound(server.SendAsync(new ListBackups(), route: received.Add));

        server.Apply(AnswersNotRouted[typeName].Sample(id));

        Assert.Empty(received);
    }

    // ---- The administration page ----

    static readonly KnownUserInfo Xaver = new("fpX", "Xaver", [WellKnownGroups.Guest], ServerMuted: true, CanBeModeratedByMe: true,
        Bans: [new BanInfo(Guid.NewGuid(), "fpX", "Xaver", null, "Spam", "ich", null)]);
    static readonly KnownUserInfo Yvonne = new("fpY", "Yvonne", [WellKnownGroups.Guest], CanBeModeratedByMe: true);
    static readonly BackupInfo Stored = new("2026-01-01_12-00-00.ovsbackup", DateTimeOffset.UnixEpoch, 3, "dev.0000");
    static readonly LogFileInfo ServerLog = new("server/2026-01-01_12-00-00.log", LogKind.Server, null, null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, 3);
    static readonly Guid Team = Guid.NewGuid();

    /// <summary>Who gets the answers to each request the page sends: its users and bans tabs, its backup card, its logs tab or its transfer.</summary>
    static readonly Dictionary<string, string> Owners = new()
    {
        [nameof(ListUsers)] = "users and bans", [nameof(ListBans)] = "users and bans", [nameof(Unban)] = "users and bans",
        [nameof(AssignGroup)] = "users and bans", [nameof(UnassignGroup)] = "users and bans",
        [nameof(BanKnownUser)] = "users and bans", [nameof(DeleteKnownUser)] = "users and bans", [nameof(SetKnownUserServerMute)] = "users and bans",
        [nameof(ListBackups)] = "backups", [nameof(CreateBackup)] = "backups", [nameof(DeleteBackup)] = "backups",
        [nameof(ListLogs)] = "logs", [nameof(ReadLog)] = "logs", [nameof(SearchLogs)] = "logs",
        [nameof(DownloadBackup)] = "transfer", [nameof(UploadBackupChunk)] = "transfer", [nameof(PrepareLogDownload)] = "transfer",
        [nameof(DownloadLogChunk)] = "transfer",
    };

    /// <summary>The requests the page sends without a route, and why no answer of theirs is needed.</summary>
    static readonly Dictionary<string, string> RequestsNotRouted = new()
    {
        [nameof(RestoreBackup)] = "answered by the disconnect of every client; its error ends Restoring",
        [nameof(CreateGroup)] = "confirmed by the groups every client gets (SendConfirmed); its error ends the save's mark",
        [nameof(UpdateGroup)] = "confirmed by the groups every client gets (SendConfirmed); its error ends the save's mark",
        [nameof(ReorderGroups)] = "confirmed by the groups every client gets (SendConfirmed); its error ends the order's mark",
        [nameof(DeleteGroup)] = "shown by the groups every client gets; its error shows as a notice",
        [nameof(UpdateServerSettings)] = "confirmed by the settings every client gets (SendConfirmed)",
        [nameof(SetServerIcon)] = "confirmed by the settings every client gets (SendConfirmed)",
        [nameof(SetChannelLinks)] = "confirmed by the links every client gets (SendConfirmed)",
    };

    /// <summary>The answers each request gets besides an error, as the server sends them; a new answer type needs its requests here.</summary>
    static readonly Dictionary<Type, Type[]> AnswersOf = new()
    {
        [typeof(ListUsers)] = [typeof(UserList)],
        [typeof(ListBans)] = [typeof(BanList)],
        [typeof(Unban)] = [typeof(BanList)],
        [typeof(ListBackups)] = [typeof(BackupList)],
        [typeof(CreateBackup)] = [typeof(BackupList)],
        [typeof(DeleteBackup)] = [typeof(BackupList)],
        [typeof(DownloadBackup)] = [typeof(BackupChunk)],
        [typeof(UploadBackupChunk)] = [typeof(UploadBackupAck), typeof(BackupUploaded), typeof(BackupList)],
        [typeof(ListLogs)] = [typeof(LogList)],
        [typeof(ReadLog)] = [typeof(LogPage)],
        [typeof(SearchLogs)] = [typeof(LogSearchResult)],
        [typeof(PrepareLogDownload)] = [typeof(LogDownloadReady)],
        [typeof(DownloadLogChunk)] = [typeof(LogChunk)],
        [typeof(CreateChannel)] = [typeof(ChannelAdded)],
        [typeof(GetServerIcon)] = [typeof(ServerIcon)],
    };

    /// <summary>A request answered with a routed answer type must be decided for the page: an owner of its route, or a reason.</summary>
    [Fact]
    public void AnswersOf_RequestWithARoutedAnswer_InOwnersOrRequestsNotRouted()
    {
        Assert.Equal(AnswerTypes().Where(candidate => candidate != typeof(Error)).Select(candidate => candidate.Name),
            AnswersOf.Values.SelectMany(types => types).Select(candidate => candidate.Name).Distinct().Order(StringComparer.Ordinal));
        var withRoutedAnswer = AnswersOf.Where(pair => pair.Value.Any(candidate => !AnswersNotRouted.ContainsKey(candidate.Name))).Select(pair => pair.Key.Name);
        Assert.All(withRoutedAnswer, name => Assert.True(Owners.ContainsKey(name) || RequestsNotRouted.ContainsKey(name), $"{name} is not decided"));
    }

    /// <summary>A page that went through every action once, on its server, and the route each request went out with.</summary>
    sealed record DrivenPage(AdminViewModel Page, ServerViewModel Server, List<(Request Request, Action<Message>? Route)> Sent);

    /// <summary>Opens a page with every right, drives each of its actions once (see <see cref="DrivenPage"/>) and answers what the server would.</summary>
    async Task<DrivenPage> DriveEveryActionAsync()
    {
        var routes = new List<(Request Request, Action<Message>? Route)>();
        ServerViewModel server = null!;
        server = Server(request =>
        {
            routes.Add((request, server.Routes.Find(request.RequestId!))); // a route is in place before the request goes out
            return request switch
            {
                ListUsers => new UserList(request.RequestId, [Xaver, Yvonne]),
                ListBans => new BanList(request.RequestId, Xaver.Bans!),
                ListBackups => new BackupList(request.RequestId, [Stored]),
                ListLogs => new LogList(request.RequestId, [ServerLog]),
                CreateGroup createGroup => new GroupsChanged([.. server.Mirror.Groups, new GroupInfo(Team, createGroup.Name, createGroup.Permissions, true)]),
                DownloadBackup => new BackupChunk(request.RequestId, Stored.FileName, 0, 3, "AQID", true),
                UploadBackupChunk => new BackupUploaded(request.RequestId, Stored),
                PrepareLogDownload => new LogDownloadReady(request.RequestId, "d1", "server.log", 3),
                DownloadLogChunk => new LogChunk(request.RequestId, "d1", 0, 3, "AQID", true),
                _ => null,
            };
        });
        var page = new AdminViewModel(server, new AdminDialogs
        {
            UsersAndBans = new()
            {
                ConfirmDeleteUser = _ => Task.FromResult(true),
                Ban = (_, _, _) => Task.FromResult<BanChoice?>(new BanChoice("Spam", null, false)),
            },
            BackupCard = new() { Confirm = _ => Task.FromResult(true), ConfirmRestore = _ => Task.FromResult(true) },
        });
        var file = Path.Combine(dir, "up.ovsbackup");
        File.WriteAllBytes(file, [1, 2, 3]);

        await Bound(page.RequestListsAsync());
        KnownUserViewModel Card(string fingerprint) => page.UsersAndBans.Users.Single(knownUser => knownUser.Fingerprint == fingerprint);
        await Bound(Card("fpX").UnbanCommand.ExecuteAsync(null));
        await Bound(Card("fpX").LiftMuteCommand.ExecuteAsync(null));
        await Bound(Card("fpY").BanCommand.ExecuteAsync(null));
        await Bound(Card("fpY").DeleteCommand.ExecuteAsync(null));
        var guest = Card("fpY").Toggles.Single(groupAssignment => groupAssignment.GroupId == WellKnownGroups.Guest);
        guest.IsChecked = false;
        await Bound(guest.ToggleCommand.ExecuteAsync(null));
        var admin = Card("fpY").Toggles.Single(groupAssignment => groupAssignment.GroupId == WellKnownGroups.Admin);
        admin.IsChecked = true;
        await Bound(admin.ToggleCommand.ExecuteAsync(null));
        await Bound(page.UsersAndBans.Bans.Single().UnbanCommand.ExecuteAsync(null));
        await Bound(page.BackupCard.NewBackupCommand.ExecuteAsync(null));
        await Bound(page.BackupCard.Backups.Single().DeleteCommand.ExecuteAsync(null));
        await Bound(page.BackupCard.Backups.Single().RestoreCommand.ExecuteAsync(null));
        await Bound(page.BackupCard.DownloadBackupAsync(page.BackupCard.Backups.Single(), Path.Combine(dir, "down.ovsbackup")));
        await Bound(page.BackupCard.UploadBackupAsync(file, restore: false));
        page.Logs.Files.Single().IsSelected = true;
        await Bound(page.Logs.DownloadAsync(Path.Combine(dir, "server.log")));
        page.Logs.OpenCommand.Execute(page.Logs.Files.Single());
        page.Logs.SearchText = "Start";
        page.Logs.SearchCommand.Execute(null);
        await Bound(page.GroupList.NewGroupCommand.ExecuteAsync(null));
        await Bound(page.GroupList.SaveGroupCommand.ExecuteAsync(null));
        GroupEditViewModel TeamRow() => page.GroupList.Groups.Single(groupEdit => groupEdit.Id == Team);
        page.GroupList.SelectedGroup = TeamRow();
        await Bound(page.GroupList.SaveGroupCommand.ExecuteAsync(null));
        await Bound(page.GroupList.MoveGroupAsync(TeamRow(), page.GroupList.Groups[0], after: false));
        page.GroupList.SelectedGroup = TeamRow();
        await Bound(page.GroupList.DeleteGroupCommand.ExecuteAsync(null));
        await Bound(page.ServerSettings.SaveCommand.ExecuteAsync(null));
        page.ServerIcon.RemoveIconCommand.Execute(null);
        page.Links.Set(FakeServers.Lobby, FakeServers.Raid, false);
        await Bound(page.Links.ApplyCommand.ExecuteAsync(null));
        return new DrivenPage(page, server, routes);
    }

    /// <summary>Every request the page sends (each action once) goes with the route of its owner, or is listed without one.</summary>
    [Fact]
    public async Task AdminPage_EveryRequest_RoutedToItsOwnerOrListed()
    {
        var (page, _, routes) = await DriveEveryActionAsync();

        string OwnerOf(Action<Message>? route) =>
            route is null ? "none"
            : route == (Action<Message>)page.UsersAndBans.Apply ? "users and bans"
            : route == (Action<Message>)page.BackupCard.Apply ? "backups"
            : route == (Action<Message>)page.Logs.Apply ? "logs"
            : route.Method.DeclaringType?.DeclaringType == typeof(AdminTransfer) ? "transfer" // a request's own closure in the transfer
            : "other";
        var owners = routes.GroupBy(r => r.Request.GetType().Name)
            .ToDictionary(grouping => grouping.Key, grouping => string.Join(", ", grouping.Select(r => OwnerOf(r.Route)).Distinct()));
        var expected = Owners.Concat(RequestsNotRouted.Keys.Select(name => KeyValuePair.Create(name, "none"))).ToDictionary();
        Assert.Equal(expected.OrderBy(pair => pair.Key, StringComparer.Ordinal), owners.OrderBy(pair => pair.Key, StringComparer.Ordinal));
    }

    [Fact]
    public async Task AdminPage_DetachedWithListsInFlight_UsersAndLogsStayEmptyWhenTheirAnswersCome()
    {
        var server = Server();
        var page = new AdminViewModel(server);
        await Bound(page.RequestListsAsync());

        page.Detach();
        server.Apply(new UserList(sent.OfType<ListUsers>().Single().RequestId, [Yvonne]));
        server.Apply(new LogList(sent.OfType<ListLogs>().Single().RequestId, [ServerLog]));

        Assert.Empty(page.UsersAndBans.Users);
        Assert.Empty(page.Logs.Files);
    }

    /// <summary>A section that keeps a route past the page's close would get answers for a page nobody sees.</summary>
    [Fact]
    public async Task AdminPage_DetachedAfterEveryAction_NoRouteOfAnyChildSectionRemains()
    {
        var (page, server, requests) = await DriveEveryActionAsync();
        var routesKeptBefore = requests.Where(pair => server.Routes.Find(pair.Request.RequestId!) is not null).Select(pair => pair.Request.GetType().Name).ToHashSet();
        Assert.Contains(nameof(ListUsers), routesKeptBefore);
        Assert.Contains(nameof(ListBackups), routesKeptBefore);
        Assert.Contains(nameof(ListLogs), routesKeptBefore);

        page.Detach();

        Assert.All(requests, pair => Assert.Null(server.Routes.Find(pair.Request.RequestId!)));
    }

    /// <summary>Package 75 (AC2): the server stores the upload and sends its new list; the backup shows in the page's list.</summary>
    [Fact]
    public async Task AdminPage_UploadStored_TheFollowingListShowsTheBackup()
    {
        var server = Server(request => request is UploadBackupChunk ? new BackupUploaded(request.RequestId, Stored) : null);
        var page = new AdminViewModel(server);
        var file = Path.Combine(dir, "up.ovsbackup");
        File.WriteAllBytes(file, [1, 2, 3]);
        await Bound(page.BackupCard.UploadBackupAsync(file, restore: false));

        server.Apply(new BackupList(sent.OfType<UploadBackupChunk>().Single().RequestId, [Stored]));

        Assert.Equal([Stored.FileName], page.BackupCard.Backups.Select(backup => backup.Info.FileName));
    }
}
