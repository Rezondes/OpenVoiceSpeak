using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using OVS.Server.Data;
using OVS.Server.Logging;
using OVS.Server.Voice;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;
using static OVS.Server.Permissions.PermissionRules;

namespace OVS.Server;

/// <summary>All live and persisted server state. Every request handler runs under one lock.</summary>
public sealed partial class ServerState
{
    const int MaxConnectionsPerIp = 5;
    const int FreePasswordFailures = 5;
    static readonly TimeSpan ForgetPasswordFailures = TimeSpan.FromDays(1);

    // ponytail: one global lock for all state; fine for a few hundred users, shard per channel if it ever contends
    readonly object gate = new();
    readonly ServerConfig config;
    readonly DataStore store;
    readonly ServerIconStore icon;
    readonly BackupStore backups; // Package 74
    readonly ServerData data;
    readonly TimeProvider time;
    readonly ServerLogs logs;
    readonly Dictionary<uint, Session> sessions = [];
    readonly Dictionary<IPAddress, int> connectionsPerIp = [];
    readonly Dictionary<IPAddress, (int Count, DateTimeOffset Last)> passwordFailures = [];
    readonly Dictionary<IPAddress, (int Count, DateTimeOffset First)> adminTokenFailures = []; // Package 86, per PasswordSource
    readonly Dictionary<Guid, DateTimeOffset> attemptSaved = []; // Package 80: last save of a ban's blocked attempts
    DateTimeOffset nextPasswordSweep;
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
        backups.RemoveUploads(); // Package 75: what a crash left behind
        LogReader = new LogReader(config.DataDir, time); // Package 81
        LogReader.RemoveExports(); // Package 82: what a crash left behind
        data = store.LoadOrCreate(() => ServerData.CreateDefault(config));
        if (data.Migrate(config)) store.Save(data);
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

    public int ConnectionsFrom(IPAddress ip)
    {
        lock (gate) return connectionsPerIp.GetValueOrDefault(ip);
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

    // ---- Password guessing (call under gate) ----

    /// <summary>IPv4 address, or the /64 of an IPv6 address (one IPv6 host usually owns a whole /64).</summary>
    static IPAddress PasswordSource(IPAddress ip)
    {
        if (ip.AddressFamily != AddressFamily.InterNetworkV6) return ip;
        var bytes = ip.GetAddressBytes();
        Array.Clear(bytes, 8, 8);
        return new IPAddress(bytes);
    }

    /// <summary>From the 5th wrong password on: blocked 1, 2, 4, ... minutes (at most 60) after the last wrong one.</summary>
    static TimeSpan PasswordBlock(int failures) =>
        failures < FreePasswordFailures ? TimeSpan.Zero
            : TimeSpan.FromMinutes(Math.Min(60, 1 << Math.Min(failures - FreePasswordFailures, 6)));

    bool PasswordBlocked(IPAddress source, DateTimeOffset now) =>
        passwordFailures.TryGetValue(source, out var f) && now < f.Last + PasswordBlock(f.Count);

    void RecordPasswordFailure(IPAddress source, DateTimeOffset now)
    {
        if (now >= nextPasswordSweep)
        {
            foreach (var (key, f) in passwordFailures)
                if (now - f.Last >= ForgetPasswordFailures) passwordFailures.Remove(key);
            nextPasswordSweep = now + ForgetPasswordFailures;
        }
        int count = passwordFailures.TryGetValue(source, out var old) && now - old.Last < ForgetPasswordFailures ? old.Count : 0;
        passwordFailures[source] = (count + 1, now);
    }

    public (Session? Session, Rejected? Rejection) Admit(string fingerprint, string nickname, IPAddress ip, string? password)
    {
        lock (gate)
        {
            // A handshake that finishes during shutdown must not join a server that is going away.
            if (closedWith is not null) return (null, new Rejected(closedWith));
            var now = time.GetUtcNow();
            var ipText = ip.ToString();
            var ban = data.Bans.FirstOrDefault(b => b.IsActive(now) && (b.Fingerprint == fingerprint || b.Ip == ipText));
            if (ban is not null)
            {
                RecordBlockedAttempt(ban, ipText, now);
                return (null, new Rejected(Codes.Banned, BanText(ban)));
            }

            // A source that guessed wrong too often is turned away before its password is even looked at.
            var source = PasswordSource(ip);
            if (data.Settings.PasswordHash is not null && PasswordBlocked(source, now))
                return (null, new Rejected(Codes.TooManyPasswordAttempts));
            if (!data.Settings.CheckPassword(password))
            {
                RecordPasswordFailure(source, now);
                return (null, new Rejected(Codes.WrongPassword));
            }

            var replaced = sessions.Values.FirstOrDefault(s => s.Fingerprint == fingerprint);
            if (sessions.Count - (replaced is null ? 0 : 1) >= data.Settings.MaxUsers)
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
            user.Login(nickname, ipText, now);
            Persist();

            var session = new Session(++lastSessionId, fingerprint, nickname, ip, RandomNumberGenerator.GetBytes(32), time)
            {
                ChannelId = data.DefaultChannelId,
                GroupIds = user.GroupIds.ToList(),
                Permissions = Effective(user.GroupIds, data.Groups),
            };
            sessions.Add(session.Id, session);
            session.Send(new Welcome(session.Id, Convert.ToBase64String(session.VoiceKey), Snapshot(session)));
            foreach (var other in sessions.Values)
                if (other != session) other.Send(new UserJoined(Info(session)));
            logs.Server($"{nickname} verbunden ({fingerprint[..12]}, {ip})");
            ChannelLog(session.ChannelId, $"{nickname} hat den Channel betreten (verbunden)");
            return (session, null);
        }
    }

    /// <param name="reason">Why the connection ended, for the logs (e.g. "Zeitüberschreitung").</param>
    public void Remove(Session session, string reason = "vom Client beendet")
    {
        lock (gate) RemoveLocked(session, null, reason);
    }

    public void CloseAll(Message final)
    {
        lock (gate)
        {
            closedWith = final is Disconnected d ? d.Reason : Codes.ServerShutdown;
            var why = final switch
            {
                Disconnected { Reason: Codes.ServerRestart } => "Server startet neu",
                Disconnected { Reason: Codes.Restoring } => "Server wird aus einem Backup wiederhergestellt", // Package 74
                _ => "Server fährt herunter",
            };
            var now = time.GetUtcNow();
            foreach (var s in sessions.Values)
            {
                s.Close(final);
                DropUpload(s);
                s.DropLogDownload(ended: true); // Package 82
                AddSessionStats(s, now);
                ChannelLog(s.ChannelId, $"{s.Nickname} hat den Channel verlassen ({why})");
            }
            logs.Server($"{why}, {sessions.Count} Nutzer getrennt");
            if (sessions.Count > 0) Persist();
            sessions.Clear();
        }
    }

    public void LogRejected(IPAddress ip, string code, string? nickname, string? fingerprint)
    {
        var who = nickname is null ? "" : fingerprint is null ? $" ({nickname})" : $" ({nickname}, {fingerprint[..12]})";
        logs.Server($"Verbindung von {ip} abgelehnt: {code}{who}");
    }

    void RemoveLocked(Session session, Message? final, string reason = "getrennt")
    {
        session.Close(final);
        DropUpload(session); // Package 75: an abandoned upload leaves no file
        session.DropLogDownload(ended: true); // Package 82: nor a prepared log download
        if (!sessions.TryGetValue(session.Id, out var current) || current != session) return;
        sessions.Remove(session.Id);
        AddSessionStats(session, time.GetUtcNow());
        Persist();
        Broadcast(new UserLeft(session.Id));
        if (final is Disconnected d) reason = d.Reason switch
        {
            Codes.Kicked => "gekickt",
            Codes.Banned => "gebannt",
            Codes.ReplacedByNewConnection => "durch neue Verbindung ersetzt",
            Codes.UserDeleted => "Nutzerdaten gelöscht",
            Codes.RateLimited => "zu viele Anfragen", // Package 86
            _ => d.Reason,
        };
        logs.Server($"{session.Nickname} getrennt ({reason})");
        ChannelLog(session.ChannelId, $"{session.Nickname} hat den Channel verlassen ({reason})");
    }

    /// <summary>Package 70: adds the ended session to the user's totals (A86: saved on disconnect, not while online).</summary>
    void AddSessionStats(Session s, DateTimeOffset now)
    {
        if (FindUser(s.Fingerprint) is not { } user) return;
        user.OnlineTime += now - s.ConnectedAt;
        user.SpeechTime += s.SpeechTime;
        user.ChatMessages += s.ChatMessages;
    }

    void ChannelLog(Guid channelId, string text)
    {
        if (FindChannel(channelId) is { } channel) logs.Channel(channel.Id, channel.Name, text);
    }

    string ChannelName(Guid id) => FindChannel(id)?.Name ?? "?";

    // ---- Requests ----

    public void Handle(Session session, Message message)
    {
        lock (gate)
        {
            if (!sessions.TryGetValue(session.Id, out var current) || current != session) return;
            if (message is Request request && !Admissible(session, request)) return;
            switch (message)
            {
                case JoinChannel r: OnJoinChannel(session, r); break;
                case CreateChannel r: OnCreateChannel(session, r); break;
                case EditChannel r: OnEditChannel(session, r); break;
                case ReorderChannels r: OnReorderChannels(session, r); break;
                case DeleteChannel r: OnDeleteChannel(session, r); break;
                case MoveUser r: OnMoveUser(session, r); break;
                case SetSelfState r: OnSetSelfState(session, r); break;
                case CreateGroup r: OnCreateGroup(session, r); break;
                case ReorderGroups r: OnReorderGroups(session, r); break;
                case UpdateGroup r: OnUpdateGroup(session, r); break;
                case DeleteGroup r: OnDeleteGroup(session, r); break;
                case AssignGroup r: OnAssignGroup(session, r); break;
                case UnassignGroup r: OnUnassignGroup(session, r); break;
                case ListUsers r: OnListUsers(session, r); break;
                case RedeemAdminToken r: OnRedeemAdminToken(session, r); break;
                case UpdateServerSettings r: OnUpdateServerSettings(session, r); break;
                case SetServerIcon r: OnSetServerIcon(session, r); break;
                case GetServerIcon r: OnGetServerIcon(session, r); break;
                case Kick r: OnKick(session, r); break;
                case Ban r: OnBan(session, r); break;
                case Unban r: OnUnban(session, r); break;
                case BanUser r: OnBanUser(session, r); break;
                case DeleteUser r: OnDeleteUser(session, r); break;
                case ListBans r: OnListBans(session, r); break;
                case SetServerMute r: OnSetServerMute(session, r); break;
                case LinkChannels r: OnLinkChannels(session, r); break;
                case SetChannelLinks r: OnSetChannelLinks(session, r); break;
                case UnlinkChannels r: OnUnlinkChannels(session, r); break;
                case SendChat r: OnSendChat(session, r); break;
                case ListBackups r: OnListBackups(session, r); break;
                case CreateBackup r: OnCreateBackup(session, r); break;
                case DeleteBackup r: OnDeleteBackup(session, r); break;
                case RestoreBackup r: OnRestoreBackup(session, r); break;
                case DownloadBackup r: OnDownloadBackup(session, r); break;
                case UploadBackupChunk r: OnUploadBackupChunk(session, r); break;
                case ListLogs r: OnListLogs(session, r); break;
                case ReadLog r: OnReadLog(session, r); break;
                case SearchLogs r: OnSearchLogs(session, r); break;
                case PrepareLogDownload r: OnPrepareLogDownload(session, r); break;
                case DownloadLogChunk r: OnDownloadLogChunk(session, r); break;
                case Request r: Fail(session, r, Codes.UnknownRequest); break;
            }
        }
    }

    // ---- Package 86: request limits (call under gate) ----

    /// <summary>The per-session budget and the RequestId length; false when the request was answered or the session dropped.</summary>
    bool Admissible(Session s, Request r)
    {
        var id = r.RequestId is { Length: > Limits.MaxRequestIdLength } tooLong ? tooLong[..Limits.MaxRequestIdLength] : r.RequestId;
        // Transfers are pull-based (one chunk per answer) and need the budget less than their speed does.
        bool transfer = r is DownloadBackup or UploadBackupChunk or DownloadLogChunk;
        switch (transfer ? null : s.TakeRequest())
        {
            case true:
                RemoveLocked(s, new Disconnected(Codes.RateLimited));
                return false;
            case false:
                s.Send(new Error(id, Codes.RateLimited));
                return false;
        }
        if (id == r.RequestId) return true;
        s.Send(new Error(id, Codes.InvalidValue, $"RequestId länger als {Limits.MaxRequestIdLength} Zeichen"));
        return false;
    }

    /// <summary>The own limit of a costly request kind, per session; answers RateLimited when it is used up.</summary>
    static bool Throttle(Session s, Request r, double perSecond, double burst = 1)
    {
        if (s.TryTakeCostly(r.GetType(), perSecond, burst)) return true;
        Fail(s, r, Codes.RateLimited);
        return false;
    }

    static bool ThrottleList(Session s, Request r) => Throttle(s, r, Limits.ListsPerSecond, Limits.ListBurst);

    static bool ThrottleHeavy(Session s, Request r) => Throttle(s, r, 1 / Limits.HeavyInterval.TotalSeconds);

    /// <summary>Needs no right, but one answer can be ~700 KB: from the cached string, once per IconInterval.</summary>
    void OnGetServerIcon(Session s, GetServerIcon r)
    {
        if (Throttle(s, r, 1 / Limits.IconInterval.TotalSeconds)) s.Send(new ServerIcon(r.RequestId, icon.Hash, icon.Base64));
    }

    // ---- Voice ----

    public Session? FindSession(uint id)
    {
        lock (gate) return sessions.GetValueOrDefault(id);
    }

    public (List<Session> Recipients, byte Target) VoiceRecipients(Session sender, byte requestedTarget)
    {
        lock (gate)
        {
            if (!sessions.TryGetValue(sender.Id, out var current) || current != sender) return ([], 0);
            return (VoiceRouting.Recipients(sessions.Values, sender, requestedTarget, LinkedChannels, id => FindChannel(id)?.IsMuted == true),
                VoiceRouting.EffectiveTarget(sender, requestedTarget));
        }
    }

    public IReadOnlySet<Guid> LinkedChannels(Guid channelId)
    {
        lock (gate) return data.Links.Where(l => l.Touches(channelId)).Select(l => l.Other(channelId)).ToHashSet();
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
        if (sessions.Count == 0) return;
        var frame = Session.Encode(message); // Package 86: once for everyone
        foreach (var s in sessions.Values) s.SendFrame(frame);
    }

    /// <summary>
    /// Saves after every change. Package 80 (A97): lifted and expired bans stay as history and are dropped on the way
    /// once they ended longer ago than the log retention (LogDays, 0 = forever).
    /// </summary>
    void Persist()
    {
        if (data.Settings.LogDays > 0)
        {
            var now = time.GetUtcNow();
            var cutoff = now.AddDays(-data.Settings.LogDays);
            data.Bans.RemoveAll(b => b.EndedAt(now) < cutoff);
        }
        store.Save(data);
    }

    /// <summary>Package 80 (A97): counts a join the ban turned away; written at most once per minute per ban, later saves take the rest along.</summary>
    void RecordBlockedAttempt(BanRecord ban, string ip, DateTimeOffset now)
    {
        ban.BlockedAttempts++;
        ban.LastAttempt = now;
        ban.LastAttemptIp = ip;
        if (attemptSaved.TryGetValue(ban.Id, out var saved) && now - saved < TimeSpan.FromMinutes(1)) return;
        attemptSaved[ban.Id] = now;
        Persist();
    }

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
            bool limitsChanged = perms.Has(Permission.ServerConfig) != s.Permissions.Has(Permission.ServerConfig);
            s.Permissions = perms;
            s.GroupIds = groups.ToList();
            Broadcast(new UserUpdated(Info(s)));
            if (limitsChanged) s.Send(new ServerSettingsChanged(SettingsInfo(s))); // Package 69: the limits come and go with the right
        }
    }

    static UserInfo Info(Session s) =>
        new(s.Id, s.Fingerprint, s.Nickname, s.ChannelId, s.SelfMuted, s.SelfDeafened, s.ServerMuted, s.Permissions, s.GroupIds);

    static ChannelInfo Info(ChannelRecord c) => new(c.Id, c.Name, c.Description, c.Order, c.IsMuted, c.MaxUsers);

    /// <summary>Package 69: the limits only for those who may change them.</summary>
    ServerSettingsInfo SettingsInfo(Session to) =>
        new(data.Settings.Name, data.Settings.WelcomeText, data.Settings.PasswordHash is not null, icon.Hash,
            to.Permissions.Has(Permission.ServerConfig) ? data.Settings.Limits : null);

    void BroadcastSettings()
    {
        foreach (var s in sessions.Values) s.Send(new ServerSettingsChanged(SettingsInfo(s)));
    }

    List<GroupInfo> GroupInfos() => data.Groups.Select(g => new GroupInfo(g.Id, g.Name, g.Permissions)).ToList();

    ServerSnapshot Snapshot(Session to) => new(
        SettingsInfo(to),
        data.DefaultChannelId,
        data.Channels.Select(Info).ToList(),
        data.Links.Select(l => new LinkInfo(l.A, l.B)).ToList(),
        GroupInfos(),
        sessions.Values.Select(Info).ToList());

    static string BanText(BanRecord ban) =>
        ban.ExpiresAt is { } until ? $"{ban.Reason} (bis {until:yyyy-MM-dd HH:mm} UTC)" : $"{ban.Reason} (dauerhaft)";
}
