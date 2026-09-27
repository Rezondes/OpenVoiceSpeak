using OVS.Server.Data;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;

namespace OVS.Server;

public sealed partial class ServerState
{
    void OnKick(Session s, Kick r)
    {
        if (!Require(s, r, Permission.UserKick) || !FindTarget(s, r, r.SessionId, out var target)) return;
        logs.Server($"{target.Nickname} wurde von {s.Nickname} gekickt: {r.Reason}");
        RemoveLocked(target, new Disconnected(Codes.Kicked, r.Reason));
    }

    void OnBan(Session s, Ban r)
    {
        if (!Require(s, r, Permission.UserBan) || !FindTarget(s, r, r.SessionId, out var target)) return;
        if (r.DurationMinutes is <= 0)
        {
            Fail(s, r, Codes.InvalidValue, "Dauer muss positiv sein");
            return;
        }
        var now = time.GetUtcNow();
        data.Bans.Add(new BanRecord
        {
            Id = Guid.NewGuid(),
            Fingerprint = target.Fingerprint,
            Nickname = target.Nickname,
            Ip = r.IncludeIp ? target.Ip.ToString() : null,
            Reason = r.Reason,
            CreatedBy = s.Nickname,
            ExpiresAt = r.DurationMinutes is { } minutes ? now.AddMinutes(minutes) : null,
        });
        Persist();
        var duration = r.DurationMinutes is { } m ? $"für {m} Minuten" : "dauerhaft";
        logs.Server($"{target.Nickname} wurde von {s.Nickname} gebannt {duration}{(r.IncludeIp ? " mit IP" : "")}: {r.Reason}");
        RemoveLocked(target, new Disconnected(Codes.Banned, r.Reason));
    }

    void OnUnban(Session s, Unban r)
    {
        if (!Require(s, r, Permission.UserBan)) return;
        var ban = data.Bans.FirstOrDefault(b => b.Id == r.BanId);
        if (ban is null)
        {
            Fail(s, r, Codes.NotFound);
            return;
        }
        data.Bans.Remove(ban);
        Persist();
        logs.Server($"Bann von {ban.Nickname} aufgehoben von {s.Nickname}");
        SendBanList(s, r.RequestId);
    }

    void OnListBans(Session s, ListBans r)
    {
        if (!Require(s, r, Permission.UserBan)) return;
        SendBanList(s, r.RequestId);
    }

    void OnSetServerMute(Session s, SetServerMute r)
    {
        if (!Require(s, r, Permission.UserMute) || !FindTarget(s, r, r.SessionId, out var target)) return;
        target.ServerMuted = r.Muted;
        logs.Server($"{target.Nickname} serverseitig {(r.Muted ? "stummgeschaltet" : "wieder freigegeben")} von {s.Nickname}");
        Broadcast(new UserUpdated(Info(target)));
    }

    void SendBanList(Session s, string? requestId)
    {
        var now = time.GetUtcNow();
        s.Send(new BanList(requestId, data.Bans.Where(b => b.IsActive(now))
            .Select(b => new BanInfo(b.Id, b.Fingerprint, b.Nickname, b.Ip, b.Reason, b.CreatedBy, b.ExpiresAt))
            .ToList()));
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
