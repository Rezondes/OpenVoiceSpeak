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
        if (!Require(s, r, Permission.UserBan) || !FindTarget(s, r, r.SessionId, out var target, lastAdminFirst: true)
            || !ValidateReason(s, r, r.Reason) || !ValidateDuration(s, r, r.DurationMinutes)) return;
        AddBan(s, target.Fingerprint, target.Nickname, r.IncludeIp ? target.Ip.ToString() : null, r.Reason, r.DurationMinutes, target);
    }

    /// <summary>Package 72: the same ban by fingerprint, for online and offline users alike.</summary>
    void OnBanUser(Session s, BanUser r)
    {
        if (!Require(s, r, Permission.UserBan) || !FindKnownTarget(s, r, r.Fingerprint, out var user, out var online, lastAdminFirst: true)
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
        if (!Require(s, r, Permission.UserDelete) || !FindKnownTarget(s, r, r.Fingerprint, out var user, out var online, lastAdminFirst: true)) return;
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
        // Package 84 (A102): only who could ban the user now; offline the stored groups count, a deleted user has none
        if (!CanModerate(s, r, RightsOf(ban.Fingerprint), ban.Fingerprint)) return;
        ban.LiftedAt = now; // Package 80 (A97): kept as history
        ban.LiftedBy = s.Nickname;
        Persist();
        logs.Server($"Bann von {ban.Nickname} aufgehoben von {s.Nickname}");
        if (s.Permissions.Has(Permission.BansView)) SendBanList(s, r.RequestId); // Package 84: the list only for who may see it
    }

    void OnListBans(Session s, ListBans r)
    {
        if (!Require(s, r, Permission.BansView) || !ThrottleList(s, r, r.Offset)) return;
        SendBanList(s, r.RequestId, Math.Max(0, r.Offset));
    }

    void OnSetServerMute(Session s, SetServerMute r)
    {
        if (!Require(s, r, Permission.UserMute) || !FindTarget(s, r, r.SessionId, out var target)) return;
        SetServerMuted(s, FindUser(target.Fingerprint), target, target.Nickname, r.Muted);
    }

    /// <summary>Package 85: the same by fingerprint, for offline users too (the user card).</summary>
    void OnSetStoredServerMute(Session s, SetStoredServerMute r)
    {
        if (!Require(s, r, Permission.UserMute) || !FindKnownTarget(s, r, r.Fingerprint, out var user, out var online)) return;
        SetServerMuted(s, user, online, online?.Nickname ?? user.LastNickname, r.Muted);
    }

    /// <summary>Package 85: stored on the record so the next login starts muted; a running session follows at once.</summary>
    void SetServerMuted(Session s, UserRecord? user, Session? online, string nickname, bool muted)
    {
        if (user is not null && user.ServerMuted != muted)
        {
            user.ServerMuted = muted;
            Persist();
        }
        logs.Server($"{nickname} serverseitig {(muted ? "stummgeschaltet" : "wieder freigegeben")} von {s.Nickname}{(online is null ? " (offline)" : "")}");
        if (online is null) return;
        online.ServerMuted = muted;
        Broadcast(new UserUpdated(Info(online)));
    }

    /// <summary>Package 80: active bans and the history; the client filters. Package 87: one page, the client asks for the rest.</summary>
    void SendBanList(Session s, string? requestId, int offset = 0) =>
        s.Send(new BanList(requestId, Page(data.Bans, offset).Select(ToInfo).ToList(), offset, data.Bans.Count));

    static BanInfo ToInfo(BanRecord b) => new(b.Id, b.Fingerprint, b.Nickname, b.Ip, b.Reason, b.CreatedBy, b.ExpiresAt,
        b.CreatedAt, b.CreatedByFingerprint, b.DurationMinutes, b.LiftedAt, b.LiftedBy, b.BlockedAttempts, b.LastAttempt, b.LastAttemptIp);

    /// <summary>The reason goes into the server log and the ban list: bounded, one line, no control or bidi characters. Empty is fine.</summary>
    static bool ValidateReason(Session s, Request r, string? reason)
    {
        // Package 83: the text rules (also no bidi controls), and one line only
        if (reason is null || TextRules.Text(reason, ProtocolInfo.MaxReasonLength) is { } text && !text.Contains('\n')) return true;
        Fail(s, r, Codes.InvalidValue, $"Ein Grund hat höchstens {ProtocolInfo.MaxReasonLength} Zeichen und keine Zeilenumbrüche oder Steuerzeichen.");
        return false;
    }

    /// <summary>
    /// Package 72: a stored user, never the actor, and only one whose rights are a strict subset of the actor's (Package 84).
    /// Online the session's rights count, offline those of the stored groups.
    /// </summary>
    /// <param name="lastAdminFirst">Package 84: ban and delete refuse the last member of the Admin group as such (LastAdmin).</param>
    bool FindKnownTarget(Session s, Request r, string fingerprint, out UserRecord user, out Session? online, bool lastAdminFirst = false)
    {
        user = FindUser(fingerprint)!;
        online = sessions.Values.FirstOrDefault(x => x.Fingerprint == fingerprint);
        if (user is null)
        {
            Fail(s, r, Codes.NotFound);
            return false;
        }
        if (lastAdminFirst && IsLastAdmin(fingerprint))
        {
            Fail(s, r, Codes.LastAdmin);
            return false;
        }
        return CanModerate(s, r, online?.Permissions ?? PermissionRules.Effective(user.GroupIds, data.Groups), fingerprint);
    }

    bool FindTarget(Session s, Request r, uint sessionId, out Session target, bool lastAdminFirst = false)
    {
        if (!sessions.TryGetValue(sessionId, out target!))
        {
            Fail(s, r, Codes.NotFound);
            return false;
        }
        if (lastAdminFirst && IsLastAdmin(target.Fingerprint))
        {
            Fail(s, r, Codes.LastAdmin);
            return false;
        }
        return CanModerate(s, r, target.Permissions, target.Fingerprint);
    }

    /// <summary>Package 84 (A102): the rank rule of every action on another user; fails the request with PermissionDenied.</summary>
    bool CanModerate(Session s, Request r, Permission targetRights, string targetFingerprint)
    {
        if (s.Permissions.CanModerate(s.Fingerprint, targetRights, targetFingerprint)) return true;
        Fail(s, r, Codes.PermissionDenied);
        return false;
    }

    /// <summary>Package 84: online the session's rights, offline those of the stored groups, nothing for an unknown user.</summary>
    Permission RightsOf(string fingerprint) =>
        sessions.Values.FirstOrDefault(x => x.Fingerprint == fingerprint)?.Permissions
        ?? (FindUser(fingerprint) is { } user ? PermissionRules.Effective(user.GroupIds, data.Groups) : Permission.None);

    bool IsLastAdmin(string fingerprint) =>
        PermissionRules.WouldRemoveLastAdmin(data.Users.Select(u => (u.Fingerprint, (IReadOnlyCollection<Guid>)u.GroupIds)), fingerprint, PermissionRules.AdminGroupId);
}
