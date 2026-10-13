using System.Collections.Concurrent;
using System.Net;
using System.Text;
using OVS.Server.Data;
using OVS.Server.Data.Records;
using OVS.Server.Logging;
using OVS.Server.Rules;
using OVS.Server.Throttling;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.Channels;
using OVS.Shared.Protocol.Messages.Chat;
using OVS.Shared.Protocol.Messages.Connection;
using OVS.Shared.Protocol.Messages.Groups;
using OVS.Shared.Protocol.Messages.Links;
using OVS.Shared.Protocol.Messages.LogAccess;
using OVS.Shared.Protocol.Messages.Moderation;
using OVS.Shared.Protocol.Messages.ServerBackups;
using OVS.Shared.Protocol.Messages.ServerInfo;
using OVS.Shared.Protocol.Messages.Users;
using static OVS.Server.Permissions.PermissionRules;
using RouteEntry = (System.Action<OVS.Server.State.ServerState, OVS.Server.Session, OVS.Shared.Protocol.Messages.Request> Handler, bool IsTransfer);

namespace OVS.Server.State;

/// <summary>All live and persisted server state. Every request handler runs under one lock.</summary>
public sealed partial class ServerState
{
    const int MaxConnectionsPerIp = 5;

    // ponytail: one global lock for all state; fine for a few hundred users, shard per channel if it ever contends
    readonly object gate = new();
    readonly ServerConfig config;
    readonly DataStore store;
    readonly ServerIconStore icon;
    readonly BackupStore backups; // Package 74
    readonly UploadFolder uploads; // Package 75
    readonly ServerData data;
    readonly TimeProvider time;
    readonly ServerLogWriter logs;
    readonly Dictionary<uint, Session> sessions = [];
    /// <summary>Package 88: the same sessions for the UDP path, read without the lock; changed together with sessions.</summary>
    readonly ConcurrentDictionary<uint, Session> voiceSessions = new();
    readonly Dictionary<IPAddress, int> connectionsPerIp = [];
    readonly PasswordGuessLimit passwordGuesses = new(); // Package 91
    readonly AdminTokenGuessLimit adminTokenGuesses = new(); // Package 86
    /// <summary>Package 94; internal so a test can see that ended sessions leave nothing behind.</summary>
    internal readonly ChannelPasswordGuessLimit channelPasswordGuesses = new();
    readonly NewIdentityLimit newIdentities = new(); // Package 87
    readonly SaveScheduler saves;
    DateTimeOffset nextPrune; // the next daily prune of unused guests, checked on each save
    readonly AdminToken adminToken = new();
    uint lastSessionId;
    string? closedWith; // set by CloseAll: the reason every later handshake is rejected with

    /// <param name="stores">From <see cref="ServerStores.Open"/>, so the crash cleanups have run.</param>
    /// <exception cref="ArgumentException">The stores belong to another data folder than <paramref name="config"/>.</exception>
    /// <param name="config">The run's settings.</param>
    /// <param name="time">The clock of the run.</param>
    /// <param name="logs">The server and channel logs.</param>
    public ServerState(ServerConfig config, TimeProvider time, ServerLogWriter logs, ServerStores stores)
    {
        if (stores.DataDir != config.DataDir)
            throw new ArgumentException($"The stores are in {stores.DataDir}, the config names {config.DataDir}", nameof(stores));
        this.config = config;
        this.time = time;
        this.logs = logs;
        store = stores.DataStore;
        icon = stores.IconStore;
        backups = stores.BackupStore;
        uploads = stores.UploadFolder;
        logReader = stores.LogReader; // Package 81
        logExporter = stores.LogExporter;
        data = store.LoadOrCreate(() => ServerData.CreateDefault(config));
        saves = new SaveScheduler(gate, WriteData, time, logs.Server);
        bool pruned = PruneGuests(time.GetUtcNow());
        if (data.Migrate(config) || pruned) store.Save(data);
        logs.Update(data.Settings.LogDays, data.Settings.LogRotateDaily);
        HintIgnoredStartValues();
        if (!data.Users.Any(user => user.GroupIds.Contains(AdminGroupId)))
        {
            // The token is a secret: console only, never in a log file (files end up in backups).
            logs.Server.AdminToken(adminToken.Generate());
            logs.Server.AdminTokenCreated();
        }
    }

    /// <summary>The admin token while nobody redeemed it.</summary>
    public string? PendingAdminToken => adminToken.Current;

    /// <summary>Package 69: raised under the lock after the server settings changed; handlers must be quick.</summary>
    public event Action? SettingsChanged;

    /// <summary>Package 69: the daily restart as set in the administration, At in server local time.</summary>
    public AutoRestartSetting AutoRestart
    {
        get { lock (gate) return new AutoRestartSetting(data.Settings.AutoRestart, data.Settings.AutoRestartTime); }
    }

    /// <summary>Package 69: environment and server-config.json only give start values; a differing one is worth a hint.</summary>
    void HintIgnoredStartValues()
    {
        var serverSettings = data.Settings;
        var values = new (string Key, string Given, string Stored)[]
        {
            ("OVS_MAX_USERS", $"{config.MaxUsers}", $"{serverSettings.MaxUsers}"),
            ("OVS_LOG_DAYS", $"{config.LogDays}", $"{serverSettings.LogDays}"),
            ("OVS_LOG_ROTATE_DAILY", OnOff(config.LogRotateDaily), OnOff(serverSettings.LogRotateDaily)),
            ("OVS_AUTO_RESTART", OnOff(config.AutoRestartAt is not null), OnOff(serverSettings.AutoRestart)),
            ("OVS_AUTO_RESTART_TIME", $"{config.AutoRestartTime:HH:mm:ss}", $"{serverSettings.AutoRestartTime:HH:mm:ss}"),
        };
        foreach (var (key, given, stored) in values)
            if (config.GivenStartValues.Contains(key) && given != stored)
                logs.Server.StartValueIgnored(key, given, stored);

        static string OnOff(bool on) => on ? "on" : "off";
    }

    /// <summary>The sessions online.</summary>
    public int SessionCount
    {
        get { lock (gate) return sessions.Count; }
    }

    // ---- Requests ----

    /// <summary>Package 83: a test hook, runs under the lock right before a request's handler.</summary>
    public Action<Request>? BeforeRequest { get; set; }

    /// <summary>
    /// Every request type with its handler, built once. A transfer (one chunk per answer, pull-based) is exempt from the
    /// request budget. Internal so a test can compare it with the request types of the protocol.
    /// </summary>
    internal static readonly IReadOnlyDictionary<Type, RouteEntry> Routes = new[]
    {
        Route<JoinChannel>(static (state, session, request) => state.OnJoinChannel(session, request)),
        Route<CreateChannel>(static (state, session, request) => state.OnCreateChannel(session, request)),
        Route<EditChannel>(static (state, session, request) => state.OnEditChannel(session, request)),
        Route<ReorderChannels>(static (state, session, request) => state.OnReorderChannels(session, request)),
        Route<DeleteChannel>(static (state, session, request) => state.OnDeleteChannel(session, request)),
        Route<MoveUser>(static (state, session, request) => state.OnMoveUser(session, request)),
        Route<SetSelfState>(static (state, session, request) => state.OnSetSelfState(session, request)),
        Route<CreateGroup>(static (state, session, request) => state.OnCreateGroup(session, request)),
        Route<ReorderGroups>(static (state, session, request) => state.OnReorderGroups(session, request)),
        Route<UpdateGroup>(static (state, session, request) => state.OnUpdateGroup(session, request)),
        Route<DeleteGroup>(static (state, session, request) => state.OnDeleteGroup(session, request)),
        Route<AssignGroup>(static (state, session, request) => state.OnAssignGroup(session, request)),
        Route<UnassignGroup>(static (state, session, request) => state.OnUnassignGroup(session, request)),
        Route<ListUsers>(static (state, session, request) => state.OnListUsers(session, request)),
        Route<RedeemAdminToken>(static (state, session, request) => state.OnRedeemAdminToken(session, request)),
        Route<UpdateServerSettings>(static (state, session, request) => state.OnUpdateServerSettings(session, request)),
        Route<SetServerIcon>(static (state, session, request) => state.OnSetServerIcon(session, request)),
        Route<GetServerIcon>(static (state, session, request) => state.OnGetServerIcon(session, request)),
        Route<KickSession>(static (state, session, request) => state.OnKickSession(session, request)),
        Route<BanSession>(static (state, session, request) => state.OnBanSession(session, request)),
        Route<Unban>(static (state, session, request) => state.OnUnban(session, request)),
        Route<BanKnownUser>(static (state, session, request) => state.OnBanKnownUser(session, request)),
        Route<DeleteKnownUser>(static (state, session, request) => state.OnDeleteKnownUser(session, request)),
        Route<ListBans>(static (state, session, request) => state.OnListBans(session, request)),
        Route<SetSessionServerMute>(static (state, session, request) => state.OnSetSessionServerMute(session, request)),
        Route<SetKnownUserServerMute>(static (state, session, request) => state.OnSetKnownUserServerMute(session, request)),
        Route<LinkChannels>(static (state, session, request) => state.OnLinkChannels(session, request)),
        Route<SetChannelLinks>(static (state, session, request) => state.OnSetChannelLinks(session, request)),
        Route<UnlinkChannels>(static (state, session, request) => state.OnUnlinkChannels(session, request)),
        Route<SendChat>(static (state, session, request) => state.OnSendChat(session, request)),
        Route<ListBackups>(static (state, session, request) => state.OnListBackups(session, request)),
        Route<CreateBackup>(static (state, session, request) => state.OnCreateBackup(session, request)),
        Route<DeleteBackup>(static (state, session, request) => state.OnDeleteBackup(session, request)),
        Route<RestoreBackup>(static (state, session, request) => state.OnRestoreBackup(session, request)),
        Route<DownloadBackup>(static (state, session, request) => state.OnDownloadBackup(session, request), isTransfer: true),
        Route<UploadBackupChunk>(static (state, session, request) => state.OnUploadBackupChunk(session, request), isTransfer: true),
        Route<ListLogs>(static (state, session, request) => state.OnListLogs(session, request)),
        Route<ReadLog>(static (state, session, request) => state.OnReadLog(session, request)),
        Route<SearchLogs>(static (state, session, request) => state.OnSearchLogs(session, request)),
        Route<PrepareLogDownload>(static (state, session, request) => state.OnPrepareLogDownload(session, request)),
        Route<DownloadLogChunk>(static (state, session, request) => state.OnDownloadLogChunk(session, request), isTransfer: true),
    }.ToDictionary();

    /// <summary>
    /// One entry of <see cref="Routes"/>; the handler gets the request as its own type. A chunked, pull-based request
    /// (the client asks for the next chunk after each answer) sets <paramref name="isTransfer"/>.
    /// </summary>
    static KeyValuePair<Type, RouteEntry> Route<T>(Action<ServerState, Session, T> handler, bool isTransfer = false) where T : Request =>
        new(typeof(T), ((state, session, request) => handler(state, session, (T)request), isTransfer));

    /// <summary>Handles one message of session.</summary>
    public void Handle(Session session, Message message)
    {
        lock (gate)
        {
            if (!IsCurrent(session)) return;
            if (message is not Request request) return;
            var route = Routes.GetValueOrDefault(request.GetType());
            if (!Admissible(session, request.RequestId, route.IsTransfer)) return;
            // Package 83: a malformed request is answered before any handler or limit of its own sees it
            if (RequestShape.Problem(request) is { } problem)
            {
                Fail(session, request, ReasonCodes.InvalidValue, problem);
                return;
            }
            try
            {
                BeforeRequest?.Invoke(request);
                if (route.Handler is null) Fail(session, request, ReasonCodes.UnknownRequest);
                else route.Handler(this, session, request);
            }
            catch (Exception exception)
            {
                // Package 83: handlers check before they change anything, so an unexpected error leaves the state as it was
                logs.Server.RequestFailed(request.GetType().Name, session.Nickname, exception.GetType().Name, exception.Message);
                Fail(session, request, ReasonCodes.InvalidValue);
            }
        }
    }

    /// <summary>
    /// Package 83: a frame that was a JSON object but no valid request. It counts against the request budget like a
    /// request and is answered InvalidValue with the problem; no handler, no hook, no log line.
    /// </summary>
    public void HandleMalformed(Session session, string? requestId, string problem)
    {
        lock (gate)
        {
            if (!IsCurrent(session) || !Admissible(session, requestId, isTransfer: false)) return;
            session.Outbox.Send(new Error(requestId, ReasonCodes.InvalidValue, problem));
        }
    }

    /// <summary>False once the session was removed or replaced by a newer connection (call under gate).</summary>
    bool IsCurrent(Session session) => sessions.TryGetValue(session.Id, out var current) && current == session;

    // ---- Helpers ----

    bool Require(Session session, Request request, Permission permission)
    {
        if (session.Permissions.Has(permission)) return true;
        Fail(session, request, ReasonCodes.PermissionDenied);
        return false;
    }

    static void Fail(Session session, Request request, string code, string? detail = null) =>
        session.Outbox.Send(new Error(request.RequestId, code, detail));

    /// <summary>Fails the request with a rule's refusal; false when the rule allows it.</summary>
    static bool Refuse(Session session, Request request, Refusal? refusal)
    {
        if (refusal is null) return false;
        Fail(session, request, refusal.Code, refusal.Detail);
        return true;
    }

    /// <summary>Package 87: at most ListPageSize entries from offset, so no list outgrows a frame.</summary>
    static List<T> Page<T>(IEnumerable<T> all, int offset) => all.Skip(offset).Take(ServerTuning.ListPageSize).ToList();

    UserRecord? FindUser(string fingerprint) => data.Users.FirstOrDefault(user => user.Fingerprint == fingerprint);

    ChannelRecord? FindChannel(Guid id) => data.Channels.FirstOrDefault(channel => channel.Id == id);

    /// <summary>Packages 83 and 94: a server or channel password longer than MaxPasswordLength is refused; true when it was.</summary>
    static bool PasswordTooLong(Session session, Request request, string? password)
    {
        if (password is not { Length: > ProtocolLimits.MaxPasswordLength }) return false;
        Fail(session, request, ReasonCodes.InvalidValue, $"Ein Passwort hat höchstens {ProtocolLimits.MaxPasswordLength} Zeichen.");
        return true;
    }

    /// <summary>Package 83: the compare form of a nickname, compatibility-normalized (NFKC) and case-folded.</summary>
    static string NicknameKey(string nickname)
    {
        try
        {
            return nickname.Normalize(NormalizationForm.FormKC).ToUpperInvariant();
        }
        catch (ArgumentException) // a stored name from before Package 83 may hold a lone surrogate
        {
            return nickname.ToUpperInvariant();
        }
    }

    /// <summary>
    /// Re-reads every online user's groups and tells everyone what changed for them.
    /// Package 92: per recipient; a recipient hears about another user only when its view of that user changed
    /// (group ids or the CanBeModeratedByMe flag), so hidden rights changes leave no trace. Whoever got other rights
    /// also gets the groups again (GroupsView and AssignableByMe depend on them); <paramref name="groupsChanged"/> sends them to everyone.
    /// </summary>
    void RecomputePermissions(bool groupsChanged = false)
    {
        var before = sessions.Values.ToDictionary(session => session, session => (session.Permissions, session.GroupIds));
        var changed = new HashSet<Session>();
        foreach (var session in sessions.Values)
        {
            var groups = FindUser(session.Fingerprint)?.GroupIds ?? [];
            var perms = Effective(groups, data.Groups);
            if (perms == session.Permissions && groups.SequenceEqual(session.GroupIds)) continue;
            bool limitsChanged = perms.Has(Permission.ServerConfig) != session.Permissions.Has(Permission.ServerConfig);
            session.Permissions = perms;
            session.GroupIds = groups.ToList();
            changed.Add(session);
            if (limitsChanged) session.Outbox.Send(new ServerSettingsChanged(SettingsInfo(session))); // Package 69: the limits come and go with the right
        }
        foreach (var to in sessions.Values)
            if (groupsChanged || (changed.Contains(to) && before[to].Permissions != to.Permissions)) to.Outbox.Send(new GroupsChanged(GroupInfos(to)));
        if (changed.Count == 0) return;
        // ponytail: O(online^2) checks when every rights change touches everyone (e.g. the guest group edited); a rare admin action
        foreach (var to in sessions.Values)
        {
            var (toBefore, _) = before[to];
            foreach (var session in sessions.Values)
            {
                bool send = session == to ? changed.Contains(session)
                    : !before[session].GroupIds.SequenceEqual(session.GroupIds)
                      || toBefore.CanModerate(to.Fingerprint, before[session].Permissions, session.Fingerprint) != Moderates(to, session);
                if (send) to.Outbox.Send(new UserUpdated(Info(session, to)));
            }
        }
    }

    /// <summary>Test hook: changes the stored data under the lock and applies the rights to the online sessions, as after a group change.</summary>
    internal void ChangeStoredData(Action<ServerData> change)
    {
        lock (gate)
        {
            change(data);
            RecomputePermissions();
        }
    }

    /// <summary>Package 92: a user's state sent to everyone, each with its own view (at most three encodings).</summary>
    void BroadcastUser(Session user)
    {
        byte[]? moderator = null, other = null;
        foreach (var to in sessions.Values)
        {
            if (to == user) to.Outbox.Send(new UserUpdated(Info(user, to)));
            else if (Moderates(to, user)) to.Outbox.SendFrame(moderator ??= SessionOutbox.Encode(new UserUpdated(Info(user, to))));
            else to.Outbox.SendFrame(other ??= SessionOutbox.Encode(new UserUpdated(Info(user, to))));
        }
    }

    /// <summary>Package 92 (A102): the strict Package 84 rule, the same the moderation commands check.</summary>
    static bool Moderates(Session actor, Session target) => actor.Permissions.CanModerate(actor.Fingerprint, target.Permissions, target.Fingerprint);

    /// <summary>Package 92 (A104): the own entry with its full rights; others without their rights, but with the recipient's flag.</summary>
    static UserInfo Info(Session session, Session to) =>
        new(session.Id, session.Fingerprint, session.Nickname, session.ChannelId, session.SelfMuted, session.SelfDeafened, session.ServerMuted,
            session == to ? session.Permissions : Permission.None, session.GroupIds, Moderates(to, session));

    static ChannelInfo Info(ChannelRecord channel) =>
        new(channel.Id, channel.Name, channel.Description, channel.Order, channel.IsMuted, channel.MaxUsers, channel.AllowedGroupIds?.ToList(), channel.StoredPassword is not null, channel.Kind,
            channel.Kind == ChannelKind.Separator ? 0 : channel.Bitrate); // Package 132: a separator has none, also one stored before

    /// <summary>Package 69: the limits only for those who may change them.</summary>
    ServerSettingsInfo SettingsInfo(Session to) =>
        new(data.Settings.Name, data.Settings.WelcomeText, data.Settings.StoredPassword is not null, icon.Hash,
            to.Permissions.Has(Permission.ServerConfig) ? data.Settings.ToServerLimits() : null);

    void BroadcastSettings()
    {
        foreach (var session in sessions.Values) session.Outbox.Send(new ServerSettingsChanged(SettingsInfo(session)));
    }

    /// <summary>Package 92 (A104): the rights of the groups only with GroupsView; everyone learns which groups it may assign.</summary>
    List<GroupInfo> GroupInfos(Session to)
    {
        bool view = to.Permissions.Has(Permission.GroupsView);
        return data.Groups.Select(groupRecord => new GroupInfo(groupRecord.Id, groupRecord.Name, view ? groupRecord.Permissions : Permission.None, CanAssign(to.Permissions, groupRecord))).ToList();
    }

    void BroadcastGroups()
    {
        foreach (var session in sessions.Values) session.Outbox.Send(new GroupsChanged(GroupInfos(session)));
    }

    ServerSnapshot Snapshot(Session to) => new(
        SettingsInfo(to),
        data.DefaultChannelId,
        data.Channels.Select(Info).ToList(),
        data.Links.Select(channelLink => new LinkInfo(channelLink.A, channelLink.B)).ToList(),
        GroupInfos(to),
        sessions.Values.Select(session => Info(session, to)).ToList());

    static string BanText(BanRecord ban) =>
        ban.ExpiresAt is { } until ? $"{ban.Reason} (bis {until:yyyy-MM-dd HH:mm} UTC)" : $"{ban.Reason} (dauerhaft)";
}
