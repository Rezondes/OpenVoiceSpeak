using OVS.Server.Data.Records;
using OVS.Server.Logging;
using OVS.Server.Permissions;
using OVS.Server.Rules;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.Connection;
using OVS.Shared.Protocol.Messages.Moderation;

namespace OVS.Server.State;

internal sealed partial class ServerState
{
    void OnKickSession(Session session, KickSession request)
    {
        if (!Require(session, request, Permission.UserKick) || !FindTarget(session, request, request.SessionId, out var target) || !ValidateReason(session, request, request.Reason)) return;
        logs.Server.Kicked(target.Nickname, session.Nickname, request.Reason);
        RemoveLocked(target, new Disconnected(ReasonCodes.Kicked, request.Reason));
    }

    void OnBanSession(Session session, BanSession request)
    {
        if (!Require(session, request, Permission.UserBan) || !FindTarget(session, request, request.SessionId, out var target, lastAdminFirst: true)
            || !ValidateReason(session, request, request.Reason) || !ValidateDuration(session, request, request.DurationMinutes)) return;
        AddBan(session, target.Fingerprint, target.Nickname, request.IncludeIp ? target.Ip.ToString() : null, request.Reason, request.DurationMinutes, target);
    }

    /// <summary>Package 72: the same ban by fingerprint, for online and offline users alike.</summary>
    void OnBanKnownUser(Session session, BanKnownUser request)
    {
        if (!Require(session, request, Permission.UserBan) || !FindKnownTarget(session, request, request.Fingerprint, out var user, out var online, lastAdminFirst: true)
            || !ValidateReason(session, request, request.Reason) || !ValidateDuration(session, request, request.DurationMinutes)) return;
        var ip = online?.Ip.ToString() ?? user.LastIp;
        if (request.IncludeIp && ip is null)
        {
            Fail(session, request, ReasonCodes.InvalidValue, "No IP address is known for this user.");
            return;
        }
        AddBan(session, user.Fingerprint, online?.Nickname ?? user.LastNickname, request.IncludeIp ? ip : null, request.Reason, request.DurationMinutes, online);
    }

    /// <summary>Package 72 (A88): record, groups, statistics and bans go; the log files stay and age out.</summary>
    void OnDeleteKnownUser(Session session, DeleteKnownUser request)
    {
        if (!Require(session, request, Permission.UserDelete) || !FindKnownTarget(session, request, request.Fingerprint, out var user, out var online, lastAdminFirst: true)) return;
        data.Users.Remove(user);
        int bans = data.Bans.RemoveAll(ban => ban.Fingerprint == user.Fingerprint); // Package 80: the history as well
        saves.Persist();
        logs.Server.UserDataDeleted(user.LastNickname, session.Nickname, user.Fingerprint[..Math.Min(12, user.Fingerprint.Length)], bans);
        if (online is not null) RemoveLocked(online, new Disconnected(ReasonCodes.UserDeleted));
    }

    void AddBan(Session session, string fingerprint, string nickname, string? ip, string reason, int? durationMinutes, Session? online)
    {
        var now = time.GetUtcNow();
        data.Bans.Add(new BanRecord
        {
            Id = Guid.NewGuid(),
            Fingerprint = fingerprint,
            Nickname = nickname,
            Ip = ip,
            Reason = reason,
            CreatedBy = session.Nickname,
            ExpiresAt = durationMinutes is { } minutes ? now.AddMinutes(minutes) : null,
            CreatedAt = now, // Package 80
            CreatedByFingerprint = session.Fingerprint,
            DurationMinutes = durationMinutes,
        });
        saves.Persist();
        var duration = durationMinutes is { } banMinutes ? $"for {banMinutes} minutes" : "permanently";
        logs.Server.Banned(nickname, session.Nickname, duration, (ip is not null ? " with IP" : "") + (online is null ? " (offline)" : ""), reason);
        if (online is not null) RemoveLocked(online, new Disconnected(ReasonCodes.Banned, reason));
    }

    /// <summary>ModerationRule.CheckDuration; false when the request was refused.</summary>
    static bool ValidateDuration(Session session, Request request, int? minutes) => !Refuse(session, request, ModerationRule.CheckDuration(minutes));

    void OnUnban(Session session, Unban request)
    {
        if (!Require(session, request, Permission.UserBan)) return;
        var now = time.GetUtcNow();
        var ban = data.Bans.FirstOrDefault(banRecord => banRecord.Id == request.BanId && banRecord.IsActive(now)); // Package 80: history cannot be lifted again
        if (ban is null)
        {
            Fail(session, request, ReasonCodes.NotFound);
            return;
        }
        // Package 84 (A102): only who could ban the user now; offline the stored groups count, a deleted user has none
        if (!CanModerate(session, request, RightsOf(ban.Fingerprint), ban.Fingerprint)) return;
        ban.LiftedAt = now; // Package 80 (A97): kept as history
        ban.LiftedBy = session.Nickname;
        saves.Persist();
        logs.Server.Unbanned(ban.Nickname, session.Nickname);
        if (session.Permissions.Has(Permission.BansView)) SendBanList(session, request.RequestId); // Package 84: the list only for who may see it
    }

    void OnListBans(Session session, ListBans request)
    {
        if (!Require(session, request, Permission.BansView) || !ThrottleList(session, request, request.Offset)) return;
        SendBanList(session, request.RequestId, Math.Max(0, request.Offset));
    }

    void OnSetSessionServerMute(Session session, SetSessionServerMute request)
    {
        if (!Require(session, request, Permission.UserMute) || !FindTarget(session, request, request.SessionId, out var target)) return;
        SetServerMuted(session, FindUser(target.Fingerprint), target, target.Nickname, request.Muted);
    }

    /// <summary>Package 85: the same by fingerprint, for offline users too (the user card).</summary>
    void OnSetKnownUserServerMute(Session session, SetKnownUserServerMute request)
    {
        if (!Require(session, request, Permission.UserMute) || !FindKnownTarget(session, request, request.Fingerprint, out var user, out var online)) return;
        SetServerMuted(session, user, online, online?.Nickname ?? user.LastNickname, request.Muted);
    }

    /// <summary>Package 85: stored on the record so the next login starts muted; a running session follows at once.</summary>
    void SetServerMuted(Session session, UserRecord? user, Session? online, string nickname, bool muted)
    {
        if (user is not null && user.ServerMuted != muted)
        {
            user.ServerMuted = muted;
            saves.Persist();
        }
        logs.Server.ServerMuteChanged(nickname, muted ? "server-muted" : "server-unmuted", session.Nickname, online is null ? " (offline)" : "");
        if (online is null) return;
        online.ServerMuted = muted;
        BroadcastUser(online);
    }

    /// <summary>Package 80: active bans and the history; the client filters. Package 87: one page, the client asks for the rest.</summary>
    void SendBanList(Session session, string? requestId, int offset = 0) =>
        session.Outbox.Send(new BanList(requestId, Page(data.Bans, offset).Select(ToInfo).ToList(), offset, data.Bans.Count));

    static BanInfo ToInfo(BanRecord ban) => new(ban.Id, ban.Fingerprint, ban.Nickname, ban.Ip, ban.Reason, ban.CreatedBy, ban.ExpiresAt,
        ban.CreatedAt, ban.CreatedByFingerprint, ban.DurationMinutes, ban.LiftedAt, ban.LiftedBy, ban.BlockedAttempts, ban.LastAttempt, ban.LastAttemptIp);

    /// <summary>ModerationRule.CheckReason; false when the request was refused.</summary>
    static bool ValidateReason(Session session, Request request, string? reason) => !Refuse(session, request, ModerationRule.CheckReason(reason));

    /// <summary>
    /// Package 72: a stored user, never the actor, and only one whose rights are a strict subset of the actor's (Package 84).
    /// Online the session's rights count, offline those of the stored groups.
    /// </summary>
    /// <param name="lastAdminFirst">Package 84: ban and delete refuse the last member of the Admin group as such (LastAdmin).</param>
    /// <param name="session">The actor.</param>
    /// <param name="request">The request, for the error answer.</param>
    /// <param name="fingerprint">The target.</param>
    /// <param name="user">The target's record.</param>
    /// <param name="online">The target's session while online.</param>
    bool FindKnownTarget(Session session, Request request, string fingerprint, out UserRecord user, out Session? online, bool lastAdminFirst = false)
    {
        user = FindUser(fingerprint)!;
        online = sessions.Values.FirstOrDefault(otherSession => otherSession.Fingerprint == fingerprint);
        if (user is null)
        {
            Fail(session, request, ReasonCodes.NotFound);
            return false;
        }
        if (lastAdminFirst && IsLastAdmin(fingerprint))
        {
            Fail(session, request, ReasonCodes.LastAdmin);
            return false;
        }
        return CanModerate(session, request, online?.Permissions ?? PermissionRules.Effective(user.GroupIds, data.Groups), fingerprint);
    }

    bool FindTarget(Session session, Request request, uint sessionId, out Session target, bool lastAdminFirst = false)
    {
        if (!sessions.TryGetValue(sessionId, out target!))
        {
            Fail(session, request, ReasonCodes.NotFound);
            return false;
        }
        if (lastAdminFirst && IsLastAdmin(target.Fingerprint))
        {
            Fail(session, request, ReasonCodes.LastAdmin);
            return false;
        }
        return CanModerate(session, request, target.Permissions, target.Fingerprint);
    }

    /// <summary>Package 84 (A102): the rank rule of every action on another user; fails the request with PermissionDenied.</summary>
    bool CanModerate(Session session, Request request, Permission targetRights, string targetFingerprint)
    {
        if (session.Permissions.CanModerate(session.Fingerprint, targetRights, targetFingerprint)) return true;
        Fail(session, request, ReasonCodes.PermissionDenied);
        return false;
    }

    /// <summary>Package 84: online the session's rights, offline those of the stored groups, nothing for an unknown user.</summary>
    Permission RightsOf(string fingerprint) =>
        sessions.Values.FirstOrDefault(session => session.Fingerprint == fingerprint)?.Permissions
        ?? (FindUser(fingerprint) is { } user ? PermissionRules.Effective(user.GroupIds, data.Groups) : Permission.None);

    bool IsLastAdmin(string fingerprint) =>
        PermissionRules.WouldRemoveLastAdmin(data.Users, fingerprint, PermissionRules.AdminGroupId);
}
