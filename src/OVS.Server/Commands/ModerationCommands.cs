using OVS.Server.Data;
using OVS.Server.Permissions;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;

namespace OVS.Server;

public sealed partial class ServerState
{
    void OnKick(Session s, Kick r)
    {
        if (!Require(s, r, Permission.UserKick) || !FindTarget(s, r, r.SessionId, out var target) || !ValidateReason(s, r, r.Reason)) return;
        logs.Server($"{target.Nickname} wurde von {s.Nickname} gekickt: {r.Reason}");
        RemoveLocked(target, new Disconnected(Codes.Kicked, r.Reason));
    }

    void OnBan(Session s, Ban r)
    {
        if (!Require(s, r, Permission.UserBan) || !FindTarget(s, r, r.SessionId, out var target) || !ValidateReason(s, r, r.Reason)) return;
        if (!ValidateDuration(s, r, r.DurationMinutes)) return;
        AddBan(s, target.Fingerprint, target.Nickname, r.IncludeIp ? target.Ip.ToString() : null, r.Reason, r.DurationMinutes, target);
    }

    /// <summary>Package 72: the same ban by fingerprint, for online and offline users alike.</summary>
    void OnBanUser(Session s, BanUser r)
    {
        if (!Require(s, r, Permission.UserBan) || !FindKnownTarget(s, r, r.Fingerprint, out var user, out var online)
            || !ValidateReason(s, r, r.Reason) || !ValidateDuration(s, r, r.DurationMinutes)) return;
        var ip = online?.Ip.ToString() ?? user.LastIp;
        if (r.IncludeIp && ip is null)
        {
            Fail(s, r, Codes.InvalidValue, "Für diesen Nutzer ist keine IP-Adresse bekannt.");
            return;
        }
        AddBan(s, user.Fingerprint, online?.Nickname ?? user.LastNickname, r.IncludeIp ? ip : null, r.Reason, r.DurationMinutes, online);
    }

    /// <summary>Package 72 (A88): record, groups, statistics and bans go; the log files stay and age out.</summary>
    void OnDeleteUser(Session s, DeleteUser r)
    {
        if (!Require(s, r, Permission.UserDelete)) return;
        var user = FindUser(r.Fingerprint);
        if (user is null)
        {
            Fail(s, r, Codes.NotFound);
            return;
        }
        if (PermissionRules.WouldRemoveLastAdmin(data.Users.Select(u => (u.Fingerprint, (IReadOnlyCollection<Guid>)u.GroupIds)),
                user.Fingerprint, PermissionRules.AdminGroupId))
        {
            Fail(s, r, Codes.LastAdmin);
            return;
        }
        if (!FindKnownTarget(s, r, r.Fingerprint, out _, out var online)) return;
        data.Users.Remove(user);
        int bans = data.Bans.RemoveAll(b => b.Fingerprint == user.Fingerprint); // Package 80: the history as well
        Persist();
        logs.Server($"Nutzerdaten von {user.LastNickname} gelöscht von {s.Nickname} ({user.Fingerprint[..Math.Min(12, user.Fingerprint.Length)]}, {bans} Bans)");
        if (online is not null) RemoveLocked(online, new Disconnected(Codes.UserDeleted));
    }

    void AddBan(Session s, string fingerprint, string nickname, string? ip, string reason, int? durationMinutes, Session? online)
    {
        var now = time.GetUtcNow();
        data.Bans.Add(new BanRecord
        {
            Id = Guid.NewGuid(),
            Fingerprint = fingerprint,
            Nickname = nickname,
            Ip = ip,
            Reason = reason,
            CreatedBy = s.Nickname,
            ExpiresAt = durationMinutes is { } minutes ? now.AddMinutes(minutes) : null,
            CreatedAt = now, // Package 80
            CreatedByFingerprint = s.Fingerprint,
            DurationMinutes = durationMinutes,
        });
        Persist();
        var duration = durationMinutes is { } m ? $"für {m} Minuten" : "dauerhaft";
        logs.Server($"{nickname} wurde von {s.Nickname} gebannt {duration}{(ip is not null ? " mit IP" : "")}{(online is null ? " (offline)" : "")}: {reason}");
        if (online is not null) RemoveLocked(online, new Disconnected(Codes.Banned, reason));
    }

    static bool ValidateDuration(Session s, Request r, int? minutes)
    {
        if (minutes is not <= 0) return true;
        Fail(s, r, Codes.InvalidValue, "Dauer muss positiv sein");
        return false;
    }

    void OnUnban(Session s, Unban r)
    {
        if (!Require(s, r, Permission.UserBan)) return;
        var now = time.GetUtcNow();
        var ban = data.Bans.FirstOrDefault(b => b.Id == r.BanId && b.IsActive(now)); // Package 80: history cannot be lifted again
        if (ban is null)
        {
            Fail(s, r, Codes.NotFound);
            return;
        }
        ban.LiftedAt = now; // Package 80 (A97): kept as history
        ban.LiftedBy = s.Nickname;
        Persist();
        logs.Server($"Bann von {ban.Nickname} aufgehoben von {s.Nickname}");
        SendBanList(s, r.RequestId);
    }

    void OnListBans(Session s, ListBans r)
    {
        if (!Require(s, r, Permission.BansView)) return;
        SendBanList(s, r.RequestId);
    }

    void OnSetServerMute(Session s, SetServerMute r)
    {
        if (!Require(s, r, Permission.UserMute) || !FindTarget(s, r, r.SessionId, out var target)) return;
        target.ServerMuted = r.Muted;
        logs.Server($"{target.Nickname} serverseitig {(r.Muted ? "stummgeschaltet" : "wieder freigegeben")} von {s.Nickname}");
        Broadcast(new UserUpdated(Info(target)));
    }

    /// <summary>Package 80: active bans and the history; the client filters.</summary>
    void SendBanList(Session s, string? requestId) => s.Send(new BanList(requestId, data.Bans.Select(ToInfo).ToList()));

    static BanInfo ToInfo(BanRecord b) => new(b.Id, b.Fingerprint, b.Nickname, b.Ip, b.Reason, b.CreatedBy, b.ExpiresAt,
        b.CreatedAt, b.CreatedByFingerprint, b.DurationMinutes, b.LiftedAt, b.LiftedBy, b.BlockedAttempts, b.LastAttempt, b.LastAttemptIp);

    /// <summary>The reason goes into the server log and the ban list: bounded, one line, no control characters. Empty is fine.</summary>
    static bool ValidateReason(Session s, Request r, string? reason)
    {
        if (reason is null || (reason.Length <= ProtocolInfo.MaxReasonLength && !reason.Any(c => char.IsControl(c) || c is '\u2028' or '\u2029'))) return true;
        Fail(s, r, Codes.InvalidValue, $"Ein Grund hat höchstens {ProtocolInfo.MaxReasonLength} Zeichen und keine Zeilenumbrüche oder Steuerzeichen.");
        return false;
    }

    /// <summary>
    /// Package 72: a stored user, never the actor, and only one whose rights are a subset of the actor's.
    /// Online the session's rights count, offline those of the stored groups.
    /// </summary>
    bool FindKnownTarget(Session s, Request r, string fingerprint, out UserRecord user, out Session? online)
    {
        user = FindUser(fingerprint)!;
        online = sessions.Values.FirstOrDefault(x => x.Fingerprint == fingerprint);
        if (user is null)
        {
            Fail(s, r, Codes.NotFound);
            return false;
        }
        var rights = online?.Permissions ?? PermissionRules.Effective(user.GroupIds, data.Groups);
        if (fingerprint == s.Fingerprint || !s.Permissions.CanActOn(rights))
        {
            Fail(s, r, Codes.PermissionDenied);
            return false;
        }
        return true;
    }

    bool FindTarget(Session s, Request r, uint sessionId, out Session target)
    {
        if (!sessions.TryGetValue(sessionId, out target!))
        {
            Fail(s, r, Codes.NotFound);
            return false;
        }
        if (!s.Permissions.CanActOn(target.Permissions))
        {
            Fail(s, r, Codes.PermissionDenied);
            return false;
        }
        return true;
    }
}
