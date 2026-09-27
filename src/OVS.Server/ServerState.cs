using System.Net;
using System.Security.Cryptography;
using OVS.Server.Data;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;
using static OVS.Server.Permissions.PermissionRules;

namespace OVS.Server;

/// <summary>All live and persisted server state. Every request handler runs under one lock.</summary>
public sealed partial class ServerState
{
    const int MaxConnectionsPerIp = 5;

    // ponytail: one global lock for all state; fine for a few hundred users, shard per channel if it ever contends
    readonly object gate = new();
    readonly ServerConfig config;
    readonly DataStore store;
    readonly ServerData data;
    readonly TimeProvider time;
    readonly Action<string> log;
    readonly Dictionary<uint, Session> sessions = [];
    readonly Dictionary<IPAddress, int> connectionsPerIp = [];
    readonly AdminToken adminToken = new();
    uint lastSessionId;

    public ServerState(ServerConfig config, TimeProvider time, Action<string> log)
    {
        this.config = config;
        this.time = time;
        this.log = log;
        store = new DataStore(Path.Combine(config.DataDir, DataStore.FileName));
        data = store.LoadOrCreate(() => ServerData.CreateDefault(config));
        if (!data.Users.Any(u => u.GroupIds.Contains(AdminGroupId)))
            log($"Admin-Token: {adminToken.Generate()}  (im Client unter 'Admin-Token einlösen' eingeben)");
    }

    public string? PendingAdminToken => adminToken.Current;

    public int SessionCount
    {
        get { lock (gate) return sessions.Count; }
    }

    // ---- Connections ----

    /// <summary>Counts the connection. Always pair with ReleaseConnection.</summary>
    public bool TryAddConnection(IPAddress ip)
    {
        lock (gate)
        {
            int count = connectionsPerIp.GetValueOrDefault(ip) + 1;
            connectionsPerIp[ip] = count;
            return count <= MaxConnectionsPerIp;
        }
    }

    public void ReleaseConnection(IPAddress ip)
    {
        lock (gate)
        {
            int count = connectionsPerIp.GetValueOrDefault(ip) - 1;
            if (count <= 0) connectionsPerIp.Remove(ip);
            else connectionsPerIp[ip] = count;
        }
    }

    public (Session? Session, Rejected? Rejection) Admit(string fingerprint, string nickname, IPAddress ip, string? password)
    {
        lock (gate)
        {
            var now = time.GetUtcNow();
            var ipText = ip.ToString();
            var ban = data.Bans.FirstOrDefault(b => b.IsActive(now) && (b.Fingerprint == fingerprint || b.Ip == ipText));
            if (ban is not null) return (null, new Rejected(Codes.Banned, BanText(ban)));

            if (!data.Settings.CheckPassword(password)) return (null, new Rejected(Codes.WrongPassword));

            var replaced = sessions.Values.FirstOrDefault(s => s.Fingerprint == fingerprint);
            if (sessions.Count - (replaced is null ? 0 : 1) >= config.MaxUsers)
                return (null, new Rejected(Codes.ServerFull));
            if (sessions.Values.Any(s => s != replaced && string.Equals(s.Nickname, nickname, StringComparison.OrdinalIgnoreCase)))
                return (null, new Rejected(Codes.NicknameTaken));

            if (replaced is not null) RemoveLocked(replaced, new Disconnected(Codes.ReplacedByNewConnection));

            var user = FindUser(fingerprint);
            if (user is null)
            {
                user = new UserRecord { Fingerprint = fingerprint, GroupIds = [GuestGroupId], FirstSeen = now };
                data.Users.Add(user);
            }
            user.LastNickname = nickname;
            Persist();

            var session = new Session(++lastSessionId, fingerprint, nickname, ip, RandomNumberGenerator.GetBytes(32))
            {
                ChannelId = data.DefaultChannelId,
                GroupIds = user.GroupIds.ToList(),
                Permissions = Effective(user.GroupIds, data.Groups),
            };
            sessions.Add(session.Id, session);
            session.Send(new Welcome(session.Id, Convert.ToBase64String(session.VoiceKey), Snapshot()));
            foreach (var other in sessions.Values)
                if (other != session) other.Send(new UserJoined(Info(session)));
            log($"{nickname} verbunden ({fingerprint[..12]}, {ip})");
            return (session, null);
        }
    }

    public void Remove(Session session)
    {
        lock (gate) RemoveLocked(session, null);
    }

    public void CloseAll(Message final)
    {
        lock (gate)
        {
            foreach (var s in sessions.Values) s.Close(final);
            sessions.Clear();
        }
    }

    void RemoveLocked(Session session, Message? final)
    {
        session.Close(final);
        if (!sessions.TryGetValue(session.Id, out var current) || current != session) return;
        sessions.Remove(session.Id);
        Broadcast(new UserLeft(session.Id));
        log($"{session.Nickname} getrennt");
    }

    // ---- Requests ----

    public void Handle(Session session, Message message)
    {
        lock (gate)
        {
            if (!sessions.TryGetValue(session.Id, out var current) || current != session) return;
            switch (message)
            {
                case JoinChannel r: OnJoinChannel(session, r); break;
                case CreateChannel r: OnCreateChannel(session, r); break;
                case EditChannel r: OnEditChannel(session, r); break;
                case DeleteChannel r: OnDeleteChannel(session, r); break;
                case MoveUser r: OnMoveUser(session, r); break;
                case SetSelfState r: OnSetSelfState(session, r); break;
                case CreateGroup r: OnCreateGroup(session, r); break;
                case UpdateGroup r: OnUpdateGroup(session, r); break;
                case DeleteGroup r: OnDeleteGroup(session, r); break;
                case AssignGroup r: OnAssignGroup(session, r); break;
                case UnassignGroup r: OnUnassignGroup(session, r); break;
                case ListUsers r: OnListUsers(session, r); break;
                case RedeemAdminToken r: OnRedeemAdminToken(session, r); break;
                case UpdateServerSettings r: OnUpdateServerSettings(session, r); break;
                case Kick r: OnKick(session, r); break;
                case Ban r: OnBan(session, r); break;
                case Unban r: OnUnban(session, r); break;
                case ListBans r: OnListBans(session, r); break;
                case SetServerMute r: OnSetServerMute(session, r); break;
                case Request r: Fail(session, r, Codes.UnknownRequest); break;
            }
        }
    }

    // ---- Helpers ----

    bool Require(Session session, Request request, Permission permission)
    {
        if (session.Permissions.Has(permission)) return true;
        Fail(session, request, Codes.PermissionDenied);
        return false;
    }

    static void Fail(Session session, Request request, string code, string? detail = null) =>
        session.Send(new Error(request.RequestId, code, detail));

    void Broadcast(Message message)
    {
        foreach (var s in sessions.Values) s.Send(message);
    }

    void Persist() => store.Save(data);

    UserRecord? FindUser(string fingerprint) => data.Users.FirstOrDefault(u => u.Fingerprint == fingerprint);

    ChannelRecord? FindChannel(Guid id) => data.Channels.FirstOrDefault(c => c.Id == id);

    /// <summary>Trimmed name of 1..max chars without control characters, else null.</summary>
    public static string? ValidName(string? name, int max)
    {
        var n = name?.Trim();
        return n is { Length: > 0 } && n.Length <= max && !n.Any(char.IsControl) ? n : null;
    }

    /// <summary>Re-reads every online user's groups and broadcasts those whose permissions changed.</summary>
    void RecomputePermissions()
    {
        foreach (var s in sessions.Values)
        {
            var groups = FindUser(s.Fingerprint)?.GroupIds ?? [];
            var perms = Effective(groups, data.Groups);
            if (perms == s.Permissions && groups.SequenceEqual(s.GroupIds)) continue;
            s.Permissions = perms;
            s.GroupIds = groups.ToList();
            Broadcast(new UserUpdated(Info(s)));
        }
    }

    static UserInfo Info(Session s) =>
        new(s.Id, s.Fingerprint, s.Nickname, s.ChannelId, s.SelfMuted, s.SelfDeafened, s.ServerMuted, s.Permissions, s.GroupIds);

    static ChannelInfo Info(ChannelRecord c) => new(c.Id, c.Name, c.Description, c.Order);

    ServerSettingsInfo SettingsInfo() =>
        new(data.Settings.Name, data.Settings.WelcomeText, data.Settings.PasswordHash is not null);

    List<GroupInfo> GroupInfos() => data.Groups.Select(g => new GroupInfo(g.Id, g.Name, g.Permissions)).ToList();

    ServerSnapshot Snapshot() => new(
        SettingsInfo(),
        data.DefaultChannelId,
        data.Channels.Select(Info).ToList(),
        data.Links.Select(l => new LinkInfo(l.A, l.B)).ToList(),
        GroupInfos(),
        sessions.Values.Select(Info).ToList());

    static string BanText(BanRecord ban) =>
        ban.ExpiresAt is { } until ? $"{ban.Reason} (bis {until:yyyy-MM-dd HH:mm} UTC)" : $"{ban.Reason} (dauerhaft)";
}
