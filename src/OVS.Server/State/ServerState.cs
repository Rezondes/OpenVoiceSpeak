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
    readonly ServerLogs logs;
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

    public ServerState(ServerConfig config, TimeProvider time, ServerLogs logs)
    {
        this.config = config;
        this.time = time;
        this.logs = logs;
        store = new DataStore(Path.Combine(config.DataDir, DataStore.FileName));
        icon = new ServerIconStore(config.DataDir);
        backups = new BackupStore(config.DataDir, time);
        uploads = new UploadFolder(backups.Folder);
        uploads.RemoveLeftovers(); // Package 75: what a crash left behind
        backups.RemoveUnfinished();
        LogReader = new LogReader(config.DataDir, time); // Package 81
        logExporter = new LogExporter(config.DataDir, LogReader, time);
        logExporter.RemoveExports(); // Package 82: what a crash left behind
        data = store.LoadOrCreate(() => ServerData.CreateDefault(config));
        saves = new SaveScheduler(gate, WriteData, time, line => logs.Server(line));
        bool pruned = PruneGuests(time.GetUtcNow());
        if (data.Migrate(config) || pruned) store.Save(data);
        logs.Update(data.Settings.LogDays, data.Settings.LogRotateDaily);
        HintIgnoredStartValues();
        if (!data.Users.Any(u => u.GroupIds.Contains(AdminGroupId)))
        {
            // The token is a secret: console only, never in a log file (files end up in backups).
            logs.Server($"Admin-Token: {adminToken.Generate()}  (im Client unter 'Admin-Token einlösen' eingeben)", toFile: false);
            logs.Server("Admin-Token erzeugt, es steht nur in der Konsolenausgabe", toConsole: false);
        }
    }

    public string? PendingAdminToken => adminToken.Current;

    /// <summary>Package 69: raised under the lock after the server settings changed; handlers must be quick.</summary>
    public event Action? SettingsChanged;

    /// <summary>Package 69: the daily restart as set in the administration, At in server local time.</summary>
    public (bool On, TimeOnly At) AutoRestart
    {
        get { lock (gate) return (data.Settings.AutoRestart, data.Settings.AutoRestartTime); }
    }

    /// <summary>Package 69: environment and server-config.json only give start values; a differing one is worth a hint.</summary>
    void HintIgnoredStartValues()
    {
        var s = data.Settings;
        var values = new (string Key, string Given, string Stored)[]
        {
            ("OVS_MAX_USERS", $"{config.MaxUsers}", $"{s.MaxUsers}"),
            ("OVS_LOG_DAYS", $"{config.LogDays}", $"{s.LogDays}"),
            ("OVS_LOG_ROTATE_DAILY", OnOff(config.LogRotateDaily), OnOff(s.LogRotateDaily)),
            ("OVS_AUTO_RESTART", OnOff(config.AutoRestartAt is not null), OnOff(s.AutoRestart)),
            ("OVS_AUTO_RESTART_TIME", $"{config.AutoRestartTime:HH:mm:ss}", $"{s.AutoRestartTime:HH:mm:ss}"),
        };
        foreach (var (key, given, stored) in values)
            if (config.GivenStartValues.Contains(key) && given != stored)
                logs.Server($"Hinweis: {key}={given} wird ignoriert, es gilt der gespeicherte Wert {stored} (änderbar in der Verwaltung unter Server)");

        static string OnOff(bool on) => on ? "an" : "aus";
    }

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
        Route<JoinChannel>(static (state, s, r) => state.OnJoinChannel(s, r)),
        Route<CreateChannel>(static (state, s, r) => state.OnCreateChannel(s, r)),
        Route<EditChannel>(static (state, s, r) => state.OnEditChannel(s, r)),
        Route<ReorderChannels>(static (state, s, r) => state.OnReorderChannels(s, r)),
        Route<DeleteChannel>(static (state, s, r) => state.OnDeleteChannel(s, r)),
        Route<MoveUser>(static (state, s, r) => state.OnMoveUser(s, r)),
        Route<SetSelfState>(static (state, s, r) => state.OnSetSelfState(s, r)),
        Route<CreateGroup>(static (state, s, r) => state.OnCreateGroup(s, r)),
        Route<ReorderGroups>(static (state, s, r) => state.OnReorderGroups(s, r)),
        Route<UpdateGroup>(static (state, s, r) => state.OnUpdateGroup(s, r)),
        Route<DeleteGroup>(static (state, s, r) => state.OnDeleteGroup(s, r)),
        Route<AssignGroup>(static (state, s, r) => state.OnAssignGroup(s, r)),
        Route<UnassignGroup>(static (state, s, r) => state.OnUnassignGroup(s, r)),
        Route<ListUsers>(static (state, s, r) => state.OnListUsers(s, r)),
        Route<RedeemAdminToken>(static (state, s, r) => state.OnRedeemAdminToken(s, r)),
        Route<UpdateServerSettings>(static (state, s, r) => state.OnUpdateServerSettings(s, r)),
        Route<SetServerIcon>(static (state, s, r) => state.OnSetServerIcon(s, r)),
        Route<GetServerIcon>(static (state, s, r) => state.OnGetServerIcon(s, r)),
        Route<Kick>(static (state, s, r) => state.OnKick(s, r)),
        Route<Ban>(static (state, s, r) => state.OnBan(s, r)),
        Route<Unban>(static (state, s, r) => state.OnUnban(s, r)),
        Route<BanUser>(static (state, s, r) => state.OnBanUser(s, r)),
        Route<DeleteUser>(static (state, s, r) => state.OnDeleteUser(s, r)),
        Route<ListBans>(static (state, s, r) => state.OnListBans(s, r)),
        Route<SetServerMute>(static (state, s, r) => state.OnSetServerMute(s, r)),
        Route<SetStoredServerMute>(static (state, s, r) => state.OnSetStoredServerMute(s, r)),
        Route<LinkChannels>(static (state, s, r) => state.OnLinkChannels(s, r)),
        Route<SetChannelLinks>(static (state, s, r) => state.OnSetChannelLinks(s, r)),
        Route<UnlinkChannels>(static (state, s, r) => state.OnUnlinkChannels(s, r)),
        Route<SendChat>(static (state, s, r) => state.OnSendChat(s, r)),
        Route<ListBackups>(static (state, s, r) => state.OnListBackups(s, r)),
        Route<CreateBackup>(static (state, s, r) => state.OnCreateBackup(s, r)),
        Route<DeleteBackup>(static (state, s, r) => state.OnDeleteBackup(s, r)),
        Route<RestoreBackup>(static (state, s, r) => state.OnRestoreBackup(s, r)),
        Route<DownloadBackup>(static (state, s, r) => state.OnDownloadBackup(s, r), isTransfer: true),
        Route<UploadBackupChunk>(static (state, s, r) => state.OnUploadBackupChunk(s, r), isTransfer: true),
        Route<ListLogs>(static (state, s, r) => state.OnListLogs(s, r)),
        Route<ReadLog>(static (state, s, r) => state.OnReadLog(s, r)),
        Route<SearchLogs>(static (state, s, r) => state.OnSearchLogs(s, r)),
        Route<PrepareLogDownload>(static (state, s, r) => state.OnPrepareLogDownload(s, r)),
        Route<DownloadLogChunk>(static (state, s, r) => state.OnDownloadLogChunk(s, r), isTransfer: true),
    }.ToDictionary();

    /// <summary>
    /// One entry of <see cref="Routes"/>; the handler gets the request as its own type. A chunked, pull-based request
    /// (the client asks for the next chunk after each answer) sets <paramref name="isTransfer"/>.
    /// </summary>
    static KeyValuePair<Type, RouteEntry> Route<T>(Action<ServerState, Session, T> handler, bool isTransfer = false) where T : Request =>
        new(typeof(T), ((state, s, r) => handler(state, s, (T)r), isTransfer));

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
                Fail(session, request, Codes.InvalidValue, problem);
                return;
            }
            try
            {
                BeforeRequest?.Invoke(request);
                if (route.Handler is null) Fail(session, request, Codes.UnknownRequest);
                else route.Handler(this, session, request);
            }
            catch (Exception e)
            {
                // Package 83: handlers check before they change anything, so an unexpected error leaves the state as it was
                logs.Server($"Fehler bei Anfrage {request.GetType().Name} von {session.Nickname}: {e.GetType().Name}: {e.Message}");
                Fail(session, request, Codes.InvalidValue);
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
            session.Outbox.Send(new Error(requestId, Codes.InvalidValue, problem));
        }
    }

    /// <summary>False once the session was removed or replaced by a newer connection (call under gate).</summary>
    bool IsCurrent(Session session) => sessions.TryGetValue(session.Id, out var current) && current == session;

    // ---- Helpers ----

    bool Require(Session session, Request request, Permission permission)
    {
        if (session.Permissions.Has(permission)) return true;
        Fail(session, request, Codes.PermissionDenied);
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
    static List<T> Page<T>(IEnumerable<T> all, int offset) => all.Skip(offset).Take(Limits.ListPageSize).ToList();

    UserRecord? FindUser(string fingerprint) => data.Users.FirstOrDefault(u => u.Fingerprint == fingerprint);

    ChannelRecord? FindChannel(Guid id) => data.Channels.FirstOrDefault(c => c.Id == id);

    /// <summary>Packages 83 and 94: a server or channel password longer than MaxPasswordLength is refused; true when it was.</summary>
    static bool PasswordTooLong(Session session, Request request, string? password)
    {
        if (password is not { Length: > ProtocolInfo.MaxPasswordLength }) return false;
        Fail(session, request, Codes.InvalidValue, $"Ein Passwort hat höchstens {ProtocolInfo.MaxPasswordLength} Zeichen.");
        return true;
    }

    /// <summary>Every stored user with its groups, as WouldRemoveLastAdmin takes them.</summary>
    IEnumerable<(string Fingerprint, IReadOnlyCollection<Guid> GroupIds)> UsersAsAdminTuples() =>
        data.Users.Select(u => (u.Fingerprint, (IReadOnlyCollection<Guid>)u.GroupIds));

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
        var before = sessions.Values.ToDictionary(s => s, s => (s.Permissions, s.GroupIds));
        var changed = new HashSet<Session>();
        foreach (var s in sessions.Values)
        {
            var groups = FindUser(s.Fingerprint)?.GroupIds ?? [];
            var perms = Effective(groups, data.Groups);
            if (perms == s.Permissions && groups.SequenceEqual(s.GroupIds)) continue;
            bool limitsChanged = perms.Has(Permission.ServerConfig) != s.Permissions.Has(Permission.ServerConfig);
            s.Permissions = perms;
            s.GroupIds = groups.ToList();
            changed.Add(s);
            if (limitsChanged) s.Outbox.Send(new ServerSettingsChanged(SettingsInfo(s))); // Package 69: the limits come and go with the right
        }
        foreach (var to in sessions.Values)
            if (groupsChanged || (changed.Contains(to) && before[to].Permissions != to.Permissions)) to.Outbox.Send(new GroupsChanged(GroupInfos(to)));
        if (changed.Count == 0) return;
        // ponytail: O(online^2) checks when every rights change touches everyone (e.g. the guest group edited); a rare admin action
        foreach (var to in sessions.Values)
        {
            var (toBefore, _) = before[to];
            foreach (var s in sessions.Values)
            {
                bool send = s == to ? changed.Contains(s)
                    : !before[s].GroupIds.SequenceEqual(s.GroupIds)
                      || toBefore.CanModerate(to.Fingerprint, before[s].Permissions, s.Fingerprint) != Moderates(to, s);
                if (send) to.Outbox.Send(new UserUpdated(Info(s, to)));
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
    static UserInfo Info(Session s, Session to) =>
        new(s.Id, s.Fingerprint, s.Nickname, s.ChannelId, s.SelfMuted, s.SelfDeafened, s.ServerMuted,
            s == to ? s.Permissions : Permission.None, s.GroupIds, Moderates(to, s));

    static ChannelInfo Info(ChannelRecord c) =>
        new(c.Id, c.Name, c.Description, c.Order, c.IsMuted, c.MaxUsers, c.AllowedGroupIds?.ToList(), c.StoredPassword is not null, c.Kind,
            c.Kind == ChannelKind.Separator ? 0 : c.Bitrate); // Package 132: a separator has none, also one stored before

    /// <summary>Package 69: the limits only for those who may change them.</summary>
    ServerSettingsInfo SettingsInfo(Session to) =>
        new(data.Settings.Name, data.Settings.WelcomeText, data.Settings.StoredPassword is not null, icon.Hash,
            to.Permissions.Has(Permission.ServerConfig) ? data.Settings.ToServerLimits() : null);

    void BroadcastSettings()
    {
        foreach (var s in sessions.Values) s.Outbox.Send(new ServerSettingsChanged(SettingsInfo(s)));
    }

    /// <summary>Package 92 (A104): the rights of the groups only with GroupsView; everyone learns which groups it may assign.</summary>
    List<GroupInfo> GroupInfos(Session to)
    {
        bool view = to.Permissions.Has(Permission.GroupsView);
        return data.Groups.Select(g => new GroupInfo(g.Id, g.Name, view ? g.Permissions : Permission.None, CanAssign(to.Permissions, g))).ToList();
    }

    void BroadcastGroups()
    {
        foreach (var s in sessions.Values) s.Outbox.Send(new GroupsChanged(GroupInfos(s)));
    }

    ServerSnapshot Snapshot(Session to) => new(
        SettingsInfo(to),
        data.DefaultChannelId,
        data.Channels.Select(Info).ToList(),
        data.Links.Select(l => new LinkInfo(l.A, l.B)).ToList(),
        GroupInfos(to),
        sessions.Values.Select(s => Info(s, to)).ToList());

    static string BanText(BanRecord ban) =>
        ban.ExpiresAt is { } until ? $"{ban.Reason} (bis {until:yyyy-MM-dd HH:mm} UTC)" : $"{ban.Reason} (dauerhaft)";
}
