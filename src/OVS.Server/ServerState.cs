using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
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
    /// <summary>Package 88: the same sessions for the UDP path, read without the lock; changed together with sessions.</summary>
    readonly ConcurrentDictionary<uint, Session> voiceSessions = new();
    readonly Dictionary<IPAddress, int> connectionsPerIp = [];
    readonly Dictionary<IPAddress, (int Count, DateTimeOffset Last)> passwordFailures = [];
    readonly Dictionary<IPAddress, (int Count, DateTimeOffset First)> adminTokenFailures = []; // Package 86, per AddressGroup
    readonly Dictionary<IPAddress, (int Count, DateTimeOffset First, bool Logged)> newIdentities = []; // Package 87, per AddressGroup
    DateTimeOffset nextIdentitySweep;
    ITimer? saveTimer; // Package 87: the debounced save of logins and logouts
    bool savePending;
    bool saveFailed; // the last write of the data file failed; logged once, and once more when a save succeeds again
    DateTimeOffset nextPrune;
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

    // ---- Connections ----

    /// <summary>Counts the connection. Always pair with ReleaseConnection.</summary>
    public bool TryAddConnection(IPAddress ip)
    {
        ip = AddressGroup(ip); // Package 88: IPv6 per /64
        lock (gate)
        {
            int count = connectionsPerIp.GetValueOrDefault(ip) + 1;
            connectionsPerIp[ip] = count;
            return count <= MaxConnectionsPerIp;
        }
    }

    public int ConnectionsFrom(IPAddress ip)
    {
        lock (gate) return connectionsPerIp.GetValueOrDefault(AddressGroup(ip));
    }

    public void ReleaseConnection(IPAddress ip)
    {
        ip = AddressGroup(ip);
        lock (gate)
        {
            int count = connectionsPerIp.GetValueOrDefault(ip) - 1;
            if (count <= 0) connectionsPerIp.Remove(ip);
            else connectionsPerIp[ip] = count;
        }
    }

    // ---- Password guessing (call under gate) ----

    /// <summary>
    /// IPv4 address, or the /64 of an IPv6 address (one IPv6 host usually owns a whole /64). Package 88: the one grouping
    /// for password and token throttling, new identities, the connection limit, IP bans and the UDP source check.
    /// </summary>
    internal static IPAddress AddressGroup(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) return ip.MapToIPv4();
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
            var source = AddressGroup(ip);
            // Package 88: an IP ban keeps the address it was given and matches everything in its group (IPv6: the /64)
            var ban = data.Bans.FirstOrDefault(b => b.IsActive(now) && (b.Fingerprint == fingerprint
                || b.Ip is not null && IPAddress.TryParse(b.Ip, out var banned) && AddressGroup(banned).Equals(source)));
            if (ban is not null)
            {
                RecordBlockedAttempt(ban, ipText, now);
                return (null, new Rejected(Codes.Banned, BanText(ban)));
            }

            // A source that guessed wrong too often is turned away before its password is even looked at.
            if (data.Settings.PasswordHash is not null && PasswordBlocked(source, now))
                return (null, new Rejected(Codes.TooManyPasswordAttempts));
            // Package 91: PBKDF2 with 100,000 iterations, about 10 ms on a desktop CPU, under the lock
            // ponytail: verified under the global lock; move it before the lock if slow hosts make logins stall the server
            if (!data.Settings.CheckPassword(password))
            {
                RecordPasswordFailure(source, now);
                return (null, new Rejected(Codes.WrongPassword));
            }
            if (data.Settings.HasLegacyHash) // Package 91: the right password turns the old unsalted hash into the new format
            {
                data.Settings.PasswordHash = ServerSettings.Hash(password ?? "");
                Persist();
                logs.Server("Serverpasswort-Hash auf PBKDF2 umgestellt");
            }

            var replaced = sessions.Values.FirstOrDefault(s => s.Fingerprint == fingerprint);
            if (sessions.Count - (replaced is null ? 0 : 1) >= data.Settings.MaxUsers)
                return (null, new Rejected(Codes.ServerFull));
            // Package 83: normalized, and an offline user's last name is taken as well
            // ponytail: one linear pass over all users per login; index the keys if the user list grows into the tens of thousands
            var key = NicknameKey(nickname);
            // A stored name can be held by several records from older data: it belongs to the one that logged in last
            // (no login counts as oldest, then the one seen first), so the others cannot lock each other out.
            var owner = data.Users.Where(u => u.LastNickname.Length > 0 && NicknameKey(u.LastNickname) == key)
                .OrderByDescending(u => u.LastLogin ?? DateTimeOffset.MinValue).ThenBy(u => u.FirstSeen).ThenBy(u => u.Fingerprint, StringComparer.Ordinal)
                .FirstOrDefault();
            if (sessions.Values.Any(s => s != replaced && NicknameKey(s.Nickname) == key) || owner is not null && owner.Fingerprint != fingerprint)
                return (null, new Rejected(Codes.NicknameTaken));

            var user = FindUser(fingerprint);
            if (user is null && !CountNewIdentity(source, ip, now)) return (null, new Rejected(Codes.RateLimited));

            if (replaced is not null) RemoveLocked(replaced, new Disconnected(Codes.ReplacedByNewConnection));

            if (user is null)
            {
                user = new UserRecord { Fingerprint = fingerprint, GroupIds = [GuestGroupId], FirstSeen = now };
                data.Users.Add(user);
            }
            user.Login(nickname, ipText, now);
            PersistSoon(); // Package 87: a connect loop no longer costs an fsync per connection

            var session = new Session(++lastSessionId, fingerprint, nickname, ip, RandomNumberGenerator.GetBytes(32), time)
            {
                ChannelId = data.DefaultChannelId,
                GroupIds = user.GroupIds.ToList(),
                Permissions = Effective(user.GroupIds, data.Groups),
                ServerMuted = user.ServerMuted, // Package 85: set before the Welcome, so no voice slips through in between
            };
            sessions.Add(session.Id, session);
            voiceSessions[session.Id] = session;
            session.Send(new Welcome(session.Id, Convert.ToBase64String(session.VoiceKey), Snapshot(session)));
            foreach (var other in sessions.Values)
                if (other != session) other.Send(new UserJoined(Info(session, other))); // Package 92: per recipient
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

    public void CloseAll(Message final) => CloseAll(final, statsCounted: false);

    /// <param name="statsCounted">The running sessions' statistics are in their records already (restore safety backup).</param>
    void CloseAll(Message final, bool statsCounted)
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
                if (!statsCounted) AddSessionStats(s, now);
                ChannelLog(s.ChannelId, $"{s.Nickname} hat den Channel verlassen ({why})");
            }
            logs.Server($"{why}, {sessions.Count} Nutzer getrennt");
            saveTimer?.Dispose(); // Package 87: nothing is written after this save (a restore replaces the file next)
            if (sessions.Count > 0 || savePending)
            {
                try
                {
                    Persist();
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    logs.Server($"Serverdaten beim Beenden nicht gespeichert: {e.Message}");
                }
            }
            sessions.Clear();
            voiceSessions.Clear();
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
        voiceSessions.TryRemove(session.Id, out _);
        AddSessionStats(session, time.GetUtcNow());
        PersistSoon();
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
    void AddSessionStats(Session s, DateTimeOffset now) => AddStats(s.Fingerprint, now - s.ConnectedAt, s.SpeechTime, s.ChatMessages);

    /// <summary>Negative values take back what was added before.</summary>
    void AddStats(string fingerprint, TimeSpan online, TimeSpan speech, int chatMessages)
    {
        if (FindUser(fingerprint) is not { } user) return;
        user.OnlineTime += online;
        user.SpeechTime += speech;
        user.ChatMessages += chatMessages;
    }

    /// <summary>Package 111: a separator has no log of its own (nothing happens in it).</summary>
    void ChannelLog(Guid channelId, string text)
    {
        if (FindChannel(channelId) is { Kind: ChannelKind.Voice } channel) logs.Channel(channel.Id, channel.Name, text);
    }

    string ChannelName(Guid id) => FindChannel(id) is { } c ? c.Kind == ChannelKind.Separator ? "(Trenner)" : c.Name : "?";

    // ---- Requests ----

    /// <summary>Package 83: a test hook, runs under the lock right before a request's handler.</summary>
    public Action<Request>? BeforeRequest { get; set; }

    /// <summary>Package 83: stands in for a frame that was a JSON object but no valid request; never on the wire.</summary>
    internal sealed record MalformedRequest(string Problem) : Request;

    public void Handle(Session session, Message message)
    {
        lock (gate)
        {
            if (!sessions.TryGetValue(session.Id, out var current) || current != session) return;
            if (message is not Request request) return;
            if (!Admissible(session, request)) return;
            // Package 83: a malformed request is answered before any handler or limit of its own sees it
            if (((request as MalformedRequest)?.Problem ?? RequestShape.Problem(request)) is { } problem)
            {
                Fail(session, request, Codes.InvalidValue, problem);
                return;
            }
            try
            {
                BeforeRequest?.Invoke(request);
                Dispatch(session, request);
            }
            catch (Exception e)
            {
                // Package 83: handlers check before they change anything, so an unexpected error leaves the state as it was
                logs.Server($"Fehler bei Anfrage {request.GetType().Name} von {session.Nickname}: {e.GetType().Name}: {e.Message}");
                Fail(session, request, Codes.InvalidValue);
            }
        }
    }

    void Dispatch(Session session, Request message)
    {
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
            case SetStoredServerMute r: OnSetStoredServerMute(session, r); break;
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

    /// <summary>
    /// Package 87: the first page counts; a later page is free only once per round, within ListRoundWindow
    /// of an answered first page of the same list. Any other page counts like a first page.
    /// </summary>
    static bool ThrottleList(Session s, Request r, int offset)
    {
        if (offset > 0 && s.TakeFollowPage(r.GetType(), offset)) return true;
        if (!Throttle(s, r, Limits.ListsPerSecond, Limits.ListBurst)) return false;
        if (offset <= 0) s.StartListRound(r.GetType());
        return true;
    }

    static bool ThrottleHeavy(Session s, Request r) => Throttle(s, r, 1 / Limits.HeavyInterval.TotalSeconds);

    /// <summary>
    /// Needs no right, but one answer can be ~700 KB: from the cached string, once per IconInterval.
    /// A logo this session has not been sent yet is always answered, so a change reaches it at once.
    /// </summary>
    void OnGetServerIcon(Session s, GetServerIcon r)
    {
        bool allowed = s.TryTakeCostly(r.GetType(), 1 / Limits.IconInterval.TotalSeconds, 1);
        if (!allowed && icon.Hash == s.ServedIconHash)
        {
            Fail(s, r, Codes.RateLimited);
            return;
        }
        s.ServedIconHash = icon.Hash;
        s.Send(new ServerIcon(r.RequestId, icon.Hash, icon.Base64));
    }

    // ---- Voice ----

    /// <summary>Package 88: lock-free, so a UDP flood never waits on or holds up the control channel.</summary>
    public Session? FindSession(uint id) => voiceSessions.GetValueOrDefault(id);

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

    void BroadcastExcept(Session except, Message message)
    {
        var frame = Session.Encode(message);
        foreach (var s in sessions.Values.Where(s => s != except)) s.SendFrame(frame);
    }

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
        var now = time.GetUtcNow();
        if (data.Settings.LogDays > 0)
        {
            var cutoff = now.AddDays(-data.Settings.LogDays);
            data.Bans.RemoveAll(b => b.EndedAt(now) < cutoff);
        }
        if (now >= nextPrune) PruneGuests(now);
        BeforeSave?.Invoke();
        store.Save(data);
        savePending = false; // a full save takes a pending debounced one along; a failed one leaves it pending
        if (saveFailed)
        {
            saveFailed = false;
            logs.Server("Serverdaten werden wieder gespeichert");
        }
    }

    /// <summary>Tests only: runs before every write of the data file; throwing simulates a locked file or a full disk.</summary>
    public Action? BeforeSave { get; set; }

    /// <summary>Package 87: writes a pending debounced save now (tests and tools that read the data file).</summary>
    public void FlushPendingSave()
    {
        lock (gate)
            if (savePending) Persist();
    }

    /// <summary>Package 87: saves within SaveDelay, once for all changes until then; CloseAll saves what is pending.</summary>
    void PersistSoon()
    {
        if (savePending) return;
        savePending = true;
        saveTimer ??= time.CreateTimer(_ =>
        {
            lock (gate)
            {
                if (!savePending || closedWith is not null) return;
                try
                {
                    Persist();
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // A virus scanner or backup tool holds the file, or the disk is full: keep the changes and try again.
                    if (!saveFailed) logs.Server($"Serverdaten nicht gespeichert, neuer Versuch alle {Limits.SaveDelay.TotalSeconds:0} s: {e.Message}");
                    saveFailed = true;
                    saveTimer!.Change(Limits.SaveDelay, Timeout.InfiniteTimeSpan);
                }
            }
        }, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        saveTimer.Change(Limits.SaveDelay, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Package 87: drops unused guest records, checked at start and then once a day; true when some went.</summary>
    bool PruneGuests(DateTimeOffset now)
    {
        nextPrune = now + TimeSpan.FromDays(1);
        int removed = data.PruneGuests(now - Limits.PruneGuestsAfter);
        if (removed > 0)
            logs.Server($"{removed} ungenutzte Gast-Einträge entfernt (nur Gast, kein Bann, über {Limits.PruneGuestsAfter.TotalDays:0} Tage nicht angemeldet)");
        return removed > 0;
    }

    /// <summary>Package 87: counts an unknown fingerprint of this source; false (and one log line per window) above the limit.</summary>
    bool CountNewIdentity(IPAddress source, IPAddress ip, DateTimeOffset now)
    {
        if (now >= nextIdentitySweep)
        {
            foreach (var (key, e) in newIdentities)
                if (now - e.First >= Limits.NewIdentityWindow) newIdentities.Remove(key);
            nextIdentitySweep = now + Limits.NewIdentityWindow;
        }
        (int Count, DateTimeOffset First, bool Logged) entry = newIdentities.TryGetValue(source, out var old) && now - old.First < Limits.NewIdentityWindow ? old : (0, now, false);
        if (entry.Count >= Limits.NewIdentitiesPerHour)
        {
            if (!entry.Logged)
                logs.Server($"Neue Identität von {ip} abgelehnt: zu viele neue Identitäten ({Limits.NewIdentitiesPerHour} pro Stunde)");
            newIdentities[source] = entry with { Logged = true };
            return false;
        }
        newIdentities[source] = entry with { Count = entry.Count + 1 };
        return true;
    }

    /// <summary>Package 87: at most ListPageSize entries from offset, so no list outgrows a frame.</summary>
    static List<T> Page<T>(IEnumerable<T> all, int offset) => all.Skip(offset).Take(Limits.ListPageSize).ToList();

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

    /// <summary>Trimmed name of 1..max chars without control, format or separator characters (Package 83: TextRules), else null.</summary>
    public static string? ValidName(string? name, int max) => TextRules.Name(name, max);

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
            if (limitsChanged) s.Send(new ServerSettingsChanged(SettingsInfo(s))); // Package 69: the limits come and go with the right
        }
        foreach (var to in sessions.Values)
            if (groupsChanged || (changed.Contains(to) && before[to].Permissions != to.Permissions)) to.Send(new GroupsChanged(GroupInfos(to)));
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
                if (send) to.Send(new UserUpdated(Info(s, to)));
            }
        }
    }

    /// <summary>Package 92: a user's state sent to everyone, each with its own view (at most three encodings).</summary>
    void BroadcastUser(Session user)
    {
        byte[]? moderator = null, other = null;
        foreach (var to in sessions.Values)
        {
            if (to == user) to.Send(new UserUpdated(Info(user, to)));
            else if (Moderates(to, user)) to.SendFrame(moderator ??= Session.Encode(new UserUpdated(Info(user, to))));
            else to.SendFrame(other ??= Session.Encode(new UserUpdated(Info(user, to))));
        }
    }

    /// <summary>Package 92 (A102): the strict Package 84 rule, the same the moderation commands check.</summary>
    static bool Moderates(Session actor, Session target) => actor.Permissions.CanModerate(actor.Fingerprint, target.Permissions, target.Fingerprint);

    /// <summary>Package 92 (A104): the own entry with its full rights; others without their rights, but with the recipient's flag.</summary>
    static UserInfo Info(Session s, Session to) =>
        new(s.Id, s.Fingerprint, s.Nickname, s.ChannelId, s.SelfMuted, s.SelfDeafened, s.ServerMuted,
            s == to ? s.Permissions : Permission.None, s.GroupIds, Moderates(to, s));

    static ChannelInfo Info(ChannelRecord c) =>
        new(c.Id, c.Name, c.Description, c.Order, c.IsMuted, c.MaxUsers, c.AllowedGroupIds?.ToList(), c.PasswordHash is not null, c.Kind);

    /// <summary>Package 69: the limits only for those who may change them.</summary>
    ServerSettingsInfo SettingsInfo(Session to) =>
        new(data.Settings.Name, data.Settings.WelcomeText, data.Settings.PasswordHash is not null, icon.Hash,
            to.Permissions.Has(Permission.ServerConfig) ? data.Settings.Limits : null);

    void BroadcastSettings()
    {
        foreach (var s in sessions.Values) s.Send(new ServerSettingsChanged(SettingsInfo(s)));
    }

    /// <summary>Package 92 (A104): the rights of the groups only with GroupsView; everyone learns which groups it may assign.</summary>
    List<GroupInfo> GroupInfos(Session to)
    {
        bool view = to.Permissions.Has(Permission.GroupsView);
        return data.Groups.Select(g => new GroupInfo(g.Id, g.Name, view ? g.Permissions : Permission.None, CanAssign(to.Permissions, g))).ToList();
    }

    void BroadcastGroups()
    {
        foreach (var s in sessions.Values) s.Send(new GroupsChanged(GroupInfos(s)));
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
