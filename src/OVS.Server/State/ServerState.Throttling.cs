using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.Connection;
using OVS.Shared.Protocol.Messages.ServerInfo;

namespace OVS.Server.State;

public sealed partial class ServerState
{
    // ---- Package 86: request limits (call under gate) ----

    /// <summary>The per-session budget and the RequestId length; false when the request was answered or the session dropped.</summary>
    bool Admissible(Session session, string? requestId, bool isTransfer)
    {
        var id = requestId is { Length: > ServerTuning.MaxRequestIdLength } tooLong ? tooLong[..ServerTuning.MaxRequestIdLength] : requestId;
        // Transfers are pull-based (one chunk per answer) and need the budget less than their speed does.
        switch (isTransfer ? null : session.Throttle.TakeRequest())
        {
            case true:
                RemoveLocked(session, new Disconnected(ReasonCodes.RateLimited));
                return false;
            case false:
                session.Outbox.Send(new Error(id, ReasonCodes.RateLimited));
                return false;
        }
        if (id == requestId) return true;
        session.Outbox.Send(new Error(id, ReasonCodes.InvalidValue, $"RequestId longer than {ServerTuning.MaxRequestIdLength} characters"));
        return false;
    }

    /// <summary>The own limit of a costly request kind, per session; answers RateLimited when it is used up.</summary>
    static bool Throttle(Session session, Request request, double perSecond, double burst = 1)
    {
        if (session.Throttle.TryTakeCostly(request.GetType(), perSecond, burst)) return true;
        Fail(session, request, ReasonCodes.RateLimited);
        return false;
    }

    /// <summary>
    /// Package 87: the first page counts; a later page is free only once per round, within ListRoundWindow
    /// of an answered first page of the same list. Any other page counts like a first page.
    /// </summary>
    static bool ThrottleList(Session session, Request request, int offset)
    {
        if (offset > 0 && session.Throttle.TakeFollowPage(request.GetType(), offset)) return true;
        if (!Throttle(session, request, ServerTuning.ListsPerSecond, ServerTuning.ListBurst)) return false;
        if (offset <= 0) session.Throttle.StartListRound(request.GetType());
        return true;
    }

    static bool ThrottleHeavy(Session session, Request request) => Throttle(session, request, 1 / ServerTuning.HeavyInterval.TotalSeconds);

    /// <summary>
    /// Needs no right, but one answer can be ~700 KB: from the cached string, once per IconInterval.
    /// A logo this session has not been sent yet is always answered, so a change reaches it at once.
    /// </summary>
    void OnGetServerIcon(Session session, GetServerIcon request)
    {
        bool allowed = session.Throttle.TryTakeCostly(request.GetType(), 1 / ServerTuning.IconInterval.TotalSeconds, 1);
        if (!allowed && icon.Hash == session.ServedIconHash)
        {
            Fail(session, request, ReasonCodes.RateLimited);
            return;
        }
        session.ServedIconHash = icon.Hash;
        session.Outbox.Send(new ServerIcon(request.RequestId, icon.Hash, icon.Base64));
    }
}
