using System.Net;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.Connection;
using OVS.Shared.Protocol.Messages.LogAccess;
using OVS.Shared.Protocol.Messages.ServerBackups;
using OVS.Shared.Protocol.Messages.ServerInfo;

namespace OVS.Server.State;

public sealed partial class ServerState
{
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
}
