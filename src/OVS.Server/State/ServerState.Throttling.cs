using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.Connection;
using OVS.Shared.Protocol.Messages.ServerInfo;

namespace OVS.Server.State;

public sealed partial class ServerState
{
    // ---- Package 86: request limits (call under gate) ----

    /// <summary>The per-session budget and the RequestId length; false when the request was answered or the session dropped.</summary>
    bool Admissible(Session s, string? requestId, bool isTransfer)
    {
        var id = requestId is { Length: > Limits.MaxRequestIdLength } tooLong ? tooLong[..Limits.MaxRequestIdLength] : requestId;
        // Transfers are pull-based (one chunk per answer) and need the budget less than their speed does.
        switch (isTransfer ? null : s.Throttle.TakeRequest())
        {
            case true:
                RemoveLocked(s, new Disconnected(Codes.RateLimited));
                return false;
            case false:
                s.Outbox.Send(new Error(id, Codes.RateLimited));
                return false;
        }
        if (id == requestId) return true;
        s.Outbox.Send(new Error(id, Codes.InvalidValue, $"RequestId länger als {Limits.MaxRequestIdLength} Zeichen"));
        return false;
    }

    /// <summary>The own limit of a costly request kind, per session; answers RateLimited when it is used up.</summary>
    static bool Throttle(Session s, Request r, double perSecond, double burst = 1)
    {
        if (s.Throttle.TryTakeCostly(r.GetType(), perSecond, burst)) return true;
        Fail(s, r, Codes.RateLimited);
        return false;
    }

    /// <summary>
    /// Package 87: the first page counts; a later page is free only once per round, within ListRoundWindow
    /// of an answered first page of the same list. Any other page counts like a first page.
    /// </summary>
    static bool ThrottleList(Session s, Request r, int offset)
    {
        if (offset > 0 && s.Throttle.TakeFollowPage(r.GetType(), offset)) return true;
        if (!Throttle(s, r, Limits.ListsPerSecond, Limits.ListBurst)) return false;
        if (offset <= 0) s.Throttle.StartListRound(r.GetType());
        return true;
    }

    static bool ThrottleHeavy(Session s, Request r) => Throttle(s, r, 1 / Limits.HeavyInterval.TotalSeconds);

    /// <summary>
    /// Needs no right, but one answer can be ~700 KB: from the cached string, once per IconInterval.
    /// A logo this session has not been sent yet is always answered, so a change reaches it at once.
    /// </summary>
    void OnGetServerIcon(Session s, GetServerIcon r)
    {
        bool allowed = s.Throttle.TryTakeCostly(r.GetType(), 1 / Limits.IconInterval.TotalSeconds, 1);
        if (!allowed && icon.Hash == s.ServedIconHash)
        {
            Fail(s, r, Codes.RateLimited);
            return;
        }
        s.ServedIconHash = icon.Hash;
        s.Outbox.Send(new ServerIcon(r.RequestId, icon.Hash, icon.Base64));
    }
}
