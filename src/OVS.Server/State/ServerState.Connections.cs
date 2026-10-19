using System.Net;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using OVS.Server.Data.Records;
using OVS.Server.Logging;
using OVS.Server.Security;
using OVS.Shared.Net;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.Channels;
using OVS.Shared.Protocol.Messages.Connection;
using OVS.Shared.Protocol.Messages.Users;
using OVS.Shared.Voice;
using static OVS.Server.Permissions.PermissionRules;

namespace OVS.Server.State;

internal sealed partial class ServerState
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

    /// <summary>The open connections from ip.</summary>
    public int ConnectionsFrom(IPAddress ip)
    {
        lock (gate) return connectionsPerIp.GetValueOrDefault(IpAddresses.ToAddressGroup(ip));
    }

    /// <summary>Counts one connection from ip less.</summary>
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

    /// <summary>
    /// Lets a client in after its proof, or says why not. Package 191 (A203): with <paramref name="service"/> it is the
    /// server admin's invisible session, see <see cref="ServiceSessions"/>.
    /// </summary>
    public Admission Admit(string fingerprint, string nickname, IPAddress ip, string? password, bool service = false)
    {
        lock (gate)
        {
            // A handshake that finishes during shutdown must not join a server that is going away.
            if (closedWith is not null) return new Admission(null, new Rejected(closedWith));
            var now = time.GetUtcNow();
            var ipText = ip.ToString();
            var source = IpAddresses.ToAddressGroup(ip);
            // Package 88: an IP ban keeps the address it was given and matches everything in its group (IPv6: the /64)
            var ban = data.Bans.FirstOrDefault(banRecord => banRecord.IsActive(now) && (banRecord.Fingerprint == fingerprint
                || banRecord.Ip is not null && IPAddress.TryParse(banRecord.Ip, out var banned) && IpAddresses.ToAddressGroup(banned).Equals(source)));
            if (ban is not null)
            {
                ban.CountBlockedAttempt(ipText, now); // Package 80 (A97)
                saves.PersistAtMostOncePerMinute(ban.Id);
                return new Admission(null, new Rejected(ReasonCodes.Banned, BanText(ban)));
            }

            if (service && !ServiceSessions.MayOpen(FindUser(fingerprint)))
                return new Admission(null, new Rejected(ReasonCodes.ServiceNotAllowed));
            if (!service && CheckPassword(password, source, now) is { } wrong) return new Admission(null, wrong); // Package 191: the fingerprint is proven already

            var replaced = sessions.Values.FirstOrDefault(otherSession => ServiceSessions.Replaces(otherSession, fingerprint, service));
            if (!service && ServiceSessions.Visible(sessions.Values).Count() - (replaced is null ? 0 : 1) >= data.Settings.MaxUsers)
                return new Admission(null, new Rejected(ReasonCodes.ServerFull));
            // Package 83: normalized, and an offline user's last name is taken as well
            // ponytail: one linear pass over all users per login; index the keys if the user list grows into the tens of thousands
            var key = NicknameKey(nickname);
            // A stored name can be held by several records from older data: it belongs to the one that logged in last
            // (no login counts as oldest, then the one seen first), so the others cannot lock each other out.
            var owner = data.Users.Where(userRecord => userRecord.LastNickname.Length > 0 && NicknameKey(userRecord.LastNickname) == key)
                .OrderByDescending(userRecord => userRecord.LastLogin ?? DateTimeOffset.MinValue).ThenBy(userRecord => userRecord.FirstSeen).ThenBy(userRecord => userRecord.Fingerprint, StringComparer.Ordinal)
                .FirstOrDefault();
            if (sessions.Values.Any(otherSession => otherSession != replaced && NicknameKey(otherSession.Nickname) == key) || owner is not null && owner.Fingerprint != fingerprint)
                return new Admission(null, new Rejected(ReasonCodes.NicknameTaken));

            var user = FindUser(fingerprint);
            if (user is null && !newIdentities.TryCount(source, now, out bool firstRefusal))
            {
                if (firstRefusal)
                    logs.Server.NewIdentityRefused(ip, ServerTuning.NewIdentitiesPerHour);
                return new Admission(null, new Rejected(ReasonCodes.RateLimited));
            }

            if (replaced is not null) RemoveLocked(replaced, new Disconnected(ReasonCodes.ReplacedByNewConnection));

            if (user is null)
            {
                user = new UserRecord { Fingerprint = fingerprint, GroupIds = [GuestGroupId], FirstSeen = now };
                data.Users.Add(user);
            }
            user.Login(nickname, ipText, now);
            saves.PersistSoon(); // Package 87: a connect loop no longer costs an fsync per connection

            var session = new Session(++lastSessionId, fingerprint, nickname, ip, RandomNumberGenerator.GetBytes(VoiceCrypto.KeySize), time, Metrics)
            {
                IsService = service,
                ChannelId = data.DefaultChannelId,
                GroupIds = user.GroupIds.ToList(),
                Permissions = Effective(user.GroupIds, data.Groups),
                ServerMuted = user.ServerMuted, // Package 85: set before the Welcome, so no voice slips through in between
                ServerMutedUntil = user.ServerMutedUntil, // Package 166
            };
            sessions.Add(session.Id, session);
            if (service)
            {
                serviceSessionCount++;
                session.Outbox.Send(new Welcome(session.Id, Convert.ToBase64String(session.VoiceKey), Snapshot(session)));
                logs.Server.ServiceConnected(nickname, fingerprint[..12], ip); // Package 191: in no channel, no voice, seen by nobody
                return new Admission(session, null);
            }
            voiceSessions[session.Id] = session;
            Metrics.Connected(); // Package 192: a service session (191) counts as neither
            dailyUsers.Add(fingerprint, user.FirstSeen);
            session.Outbox.Send(new Welcome(session.Id, Convert.ToBase64String(session.VoiceKey), Snapshot(session)));
            foreach (var other in sessions.Values)
                if (other != session) other.Outbox.Send(new UserJoined(Info(session, other))); // Package 92: per recipient
            logs.Server.Connected(nickname, fingerprint[..12], ip);
            ChannelLogger(session.ChannelId)?.JoinedOnConnect(nickname);
            return new Admission(session, null);
        }
    }

    /// <returns>Null for the right password, else why the session is refused.</returns>
    Rejected? CheckPassword(string? password, IPAddress source, DateTimeOffset now)
    {
        // A source that guessed wrong too often is turned away before its password is even looked at.
        if (data.Settings.StoredPassword is not null && passwordGuesses.IsBlocked(source, now))
            return new Rejected(ReasonCodes.TooManyPasswordAttempts);
        // Package 91: PBKDF2 with 100,000 iterations, about 10 ms on a desktop CPU, under the lock
        // ponytail: verified under the global lock; move it before the lock if slow hosts make logins stall the server
        if (!data.Settings.CheckPassword(password))
        {
            passwordGuesses.RecordFailure(source, now);
            return new Rejected(ReasonCodes.WrongPassword);
        }
        if (data.Settings.HasLegacyHash()) // Package 91: the right password turns the old unsalted hash into the new format
        {
            data.Settings.StoredPassword = StoredPassword.Create(password ?? "");
            saves.Persist();
            logs.Server.PasswordHashUpgraded();
        }
        return null;
    }

    /// <param name="reason">Why the connection ended, for the logs (e.g. "timed out").</param>
    /// <param name="session">The session to end.</param>
    public void Remove(Session session, string reason = "ended by the client")
    {
        lock (gate) RemoveLocked(session, null, reason);
    }

    /// <summary>Sends final to every session and ends them; later handshakes are rejected with its reason.</summary>
    public void CloseAll(Message final) => CloseAll(final, statsCounted: false);

    /// <param name="statsCounted">The running sessions' statistics are in their records already (restore safety backup).</param>
    /// <param name="final">The last message every session gets.</param>
    void CloseAll(Message final, bool statsCounted)
    {
        lock (gate)
        {
            closedWith = final is Disconnected disconnected ? disconnected.Reason : ReasonCodes.ServerShutdown;
            var why = final switch
            {
                Disconnected { Reason: ReasonCodes.ServerRestart } => "Server restarting",
                Disconnected { Reason: ReasonCodes.Restoring } => "Server being restored from a backup", // Package 74
                _ => "Server shutting down",
            };
            var now = time.GetUtcNow();
            foreach (var session in sessions.Values)
            {
                session.Outbox.Close(final);
                DropUpload(session);
                session.LogDownload.End(); // Package 82
                channelPasswordGuesses.ForgetSession(session.Id);
                if (session.IsService) continue; // Package 191: no statistics, in no channel
                if (!statsCounted) AddSessionStats(session, now);
                ChannelLogger(session.ChannelId)?.Left(session.Nickname, why);
            }
            logs.Server.AllDisconnected(why, sessions.Count);
            muteEnd?.Dispose(); // Package 166: no mute ends after the close
            afkCheck?.Dispose(); // Package 169
            saves.Close(force: sessions.Count > 0); // the running sessions' statistics, or what is still pending
            sessions.Clear();
            serviceSessionCount = 0;
            voiceSessions.Clear();
        }
    }

    /// <summary>Logs a rejected handshake.</summary>
    public void LogRejected(IPAddress ip, string code, string? nickname, string? fingerprint)
    {
        var who = nickname is null ? "" : fingerprint is null ? $" ({nickname})" : $" ({nickname}, {fingerprint[..12]})";
        logs.Server.ConnectionRefused(ip, code, who);
        Metrics.Rejected(code); // Package 147
    }

    void RemoveLocked(Session session, Message? final, string reason = "disconnected")
    {
        session.Outbox.Close(final);
        DropUpload(session); // Package 75: an abandoned upload leaves no file
        session.LogDownload.End(); // Package 82: nor a prepared log download
        channelPasswordGuesses.ForgetSession(session.Id); // nor its wrong channel passwords
        if (!IsCurrent(session)) return;
        sessions.Remove(session.Id);
        if (session.IsService)
        {
            serviceSessionCount--; // Package 191: nobody saw it come, nobody sees it go, it counts no online time
            logs.Server.Disconnected(session.Nickname, final is Disconnected { Reason: var code } ? code : reason);
            return;
        }
        voiceSessions.TryRemove(session.Id, out _);
        AddSessionStats(session, time.GetUtcNow());
        saves.PersistSoon();
        Broadcast(new UserLeft(session.Id));
        if (final is Disconnected disconnected) reason = disconnected.Reason switch
        {
            ReasonCodes.Kicked => "kicked",
            ReasonCodes.Banned => "banned",
            ReasonCodes.ReplacedByNewConnection => "replaced by a new connection",
            ReasonCodes.UserDeleted => "user data deleted",
            ReasonCodes.RateLimited => "too many requests", // Package 86
            _ => disconnected.Reason,
        };
        logs.Server.Disconnected(session.Nickname, reason);
        ChannelLogger(session.ChannelId)?.Left(session.Nickname, reason);
    }

    /// <summary>Package 70: adds the ended session to the user's totals (A86: saved on disconnect, not while online).</summary>
    void AddSessionStats(Session session, DateTimeOffset now) => AddStats(session.Fingerprint, now - session.ConnectedAt, session.SpeechTime, session.ChatMessages);

    /// <summary>Negative values take back what was added before.</summary>
    void AddStats(string fingerprint, TimeSpan online, TimeSpan speech, int chatMessages)
    {
        if (FindUser(fingerprint) is not { } user) return;
        user.OnlineTime += online;
        user.SpeechTime += speech;
        user.ChatMessages += chatMessages;
    }

    /// <summary>The channel's log; null for a separator (Package 111: nothing happens in it) or an unknown channel.</summary>
    ILogger? ChannelLogger(Guid channelId) =>
        FindChannel(channelId) is { Kind: ChannelKind.Voice } channel ? logs.Channel(channel.Id, channel.Name) : null;

    string ChannelName(Guid id) => FindChannel(id) is { } channel ? channel.Kind == ChannelKind.Separator ? "(separator)" : channel.Name : "?";
}
