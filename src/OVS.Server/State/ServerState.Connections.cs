using System.Net;
using System.Security.Cryptography;
using OVS.Server.Data.Records;
using OVS.Server.Security;
using OVS.Shared.Net;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.Channels;
using OVS.Shared.Protocol.Messages.Connection;
using OVS.Shared.Protocol.Messages.Users;
using static OVS.Server.Permissions.PermissionRules;

namespace OVS.Server.State;

public sealed partial class ServerState
{
    // ---- Connections ----

    /// <summary>Counts the connection. Always pair with ReleaseConnection.</summary>
    public bool TryAddConnection(IPAddress ip)
    {
        ip = IpAddresses.ToAddressGroup(ip); // Package 88: IPv6 per /64
        lock (gate)
        {
            int count = connectionsPerIp.GetValueOrDefault(ip) + 1;
            connectionsPerIp[ip] = count;
            return count <= MaxConnectionsPerIp;
        }
    }

    public int ConnectionsFrom(IPAddress ip)
    {
        lock (gate) return connectionsPerIp.GetValueOrDefault(IpAddresses.ToAddressGroup(ip));
    }

    public void ReleaseConnection(IPAddress ip)
    {
        ip = IpAddresses.ToAddressGroup(ip);
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
            // A handshake that finishes during shutdown must not join a server that is going away.
            if (closedWith is not null) return (null, new Rejected(closedWith));
            var now = time.GetUtcNow();
            var ipText = ip.ToString();
            var source = IpAddresses.ToAddressGroup(ip);
            // Package 88: an IP ban keeps the address it was given and matches everything in its group (IPv6: the /64)
            var ban = data.Bans.FirstOrDefault(b => b.IsActive(now) && (b.Fingerprint == fingerprint
                || b.Ip is not null && IPAddress.TryParse(b.Ip, out var banned) && IpAddresses.ToAddressGroup(banned).Equals(source)));
            if (ban is not null)
            {
                ban.CountBlockedAttempt(ipText, now); // Package 80 (A97)
                saves.PersistAtMostOncePerMinute(ban.Id);
                return (null, new Rejected(Codes.Banned, BanText(ban)));
            }

            // A source that guessed wrong too often is turned away before its password is even looked at.
            if (data.Settings.StoredPassword is not null && passwordGuesses.IsBlocked(source, now))
                return (null, new Rejected(Codes.TooManyPasswordAttempts));
            // Package 91: PBKDF2 with 100,000 iterations, about 10 ms on a desktop CPU, under the lock
            // ponytail: verified under the global lock; move it before the lock if slow hosts make logins stall the server
            if (!data.Settings.CheckPassword(password))
            {
                passwordGuesses.RecordFailure(source, now);
                return (null, new Rejected(Codes.WrongPassword));
            }
            if (data.Settings.HasLegacyHash()) // Package 91: the right password turns the old unsalted hash into the new format
            {
                data.Settings.StoredPassword = StoredPassword.Create(password ?? "");
                saves.Persist();
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
            if (user is null && !newIdentities.TryCount(source, now, out bool firstRefusal))
            {
                if (firstRefusal)
                    logs.Server($"Neue Identität von {ip} abgelehnt: zu viele neue Identitäten ({ServerTuning.NewIdentitiesPerHour} pro Stunde)");
                return (null, new Rejected(Codes.RateLimited));
            }

            if (replaced is not null) RemoveLocked(replaced, new Disconnected(Codes.ReplacedByNewConnection));

            if (user is null)
            {
                user = new UserRecord { Fingerprint = fingerprint, GroupIds = [GuestGroupId], FirstSeen = now };
                data.Users.Add(user);
            }
            user.Login(nickname, ipText, now);
            saves.PersistSoon(); // Package 87: a connect loop no longer costs an fsync per connection

            var session = new Session(++lastSessionId, fingerprint, nickname, ip, RandomNumberGenerator.GetBytes(32), time)
            {
                ChannelId = data.DefaultChannelId,
                GroupIds = user.GroupIds.ToList(),
                Permissions = Effective(user.GroupIds, data.Groups),
                ServerMuted = user.ServerMuted, // Package 85: set before the Welcome, so no voice slips through in between
            };
            sessions.Add(session.Id, session);
            voiceSessions[session.Id] = session;
            session.Outbox.Send(new Welcome(session.Id, Convert.ToBase64String(session.VoiceKey), Snapshot(session)));
            foreach (var other in sessions.Values)
                if (other != session) other.Outbox.Send(new UserJoined(Info(session, other))); // Package 92: per recipient
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
                s.Outbox.Close(final);
                DropUpload(s);
                s.LogDownload.End(); // Package 82
                channelPasswordGuesses.ForgetSession(s.Id);
                if (!statsCounted) AddSessionStats(s, now);
                ChannelLog(s.ChannelId, $"{s.Nickname} hat den Channel verlassen ({why})");
            }
            logs.Server($"{why}, {sessions.Count} Nutzer getrennt");
            saves.Close(force: sessions.Count > 0); // the running sessions' statistics, or what is still pending
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
        session.Outbox.Close(final);
        DropUpload(session); // Package 75: an abandoned upload leaves no file
        session.LogDownload.End(); // Package 82: nor a prepared log download
        channelPasswordGuesses.ForgetSession(session.Id); // nor its wrong channel passwords
        if (!IsCurrent(session)) return;
        sessions.Remove(session.Id);
        voiceSessions.TryRemove(session.Id, out _);
        AddSessionStats(session, time.GetUtcNow());
        saves.PersistSoon();
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
}
