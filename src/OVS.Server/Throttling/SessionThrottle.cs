using OVS.Shared.Protocol;

namespace OVS.Server.Throttling;

/// <summary>
/// The budgets of one session (Package 86, 87, A29) and the flood rule they share with refused voice (Package 141).
/// Requests, list rounds, costly kinds and chat are guarded by the ServerState lock; RefuseVoice runs under the voice gate
/// of UdpVoiceServer and has fields of its own, so the two never touch the same field.
/// </summary>
public sealed class SessionThrottle(TimeProvider time)
{
    readonly RateLimiter requests = new(time, ServerTuning.RequestsPerSecond, ServerTuning.RequestBurst);
    readonly Dictionary<Type, RateLimiter> costly = [];
    DateTimeOffset floodSince, lastRefused;
    DateTimeOffset voiceFloodSince, voiceLastRefused;
    readonly Queue<DateTimeOffset> chatTimes = new();

    /// <summary>Per list kind: when its first page was answered and which later pages were sent since.</summary>
    readonly Dictionary<Type, (DateTimeOffset Start, HashSet<int> Served)> listRounds = [];

    /// <summary>Takes one request from the budget. Null when it may run, else false (refuse) or true (sustained flood, disconnect).</summary>
    public bool? TakeRequest()
    {
        if (requests.TryTake()) return null;
        return Flooding(ref floodSince, ref lastRefused);
    }

    /// <summary>Package 141: an invalid VoiceUp; true once they went on without a FloodPause for FloodDisconnectAfter, like refused requests.</summary>
    public bool RefuseVoice() => Flooding(ref voiceFloodSince, ref voiceLastRefused);

    /// <summary>Notes a refusal; true when refusals went on without a pause longer than FloodPause for FloodDisconnectAfter.</summary>
    bool Flooding(ref DateTimeOffset since, ref DateTimeOffset last)
    {
        var now = time.GetUtcNow();
        if (now - last > ServerTuning.FloodPause) since = now;
        last = now;
        return now - since >= ServerTuning.FloodDisconnectAfter;
    }

    /// <summary>Starts a round for a list kind when its first page is answered; later pages are then free via <see cref="TakeFollowPage"/>.</summary>
    public void StartListRound(Type kind) => listRounds[kind] = (time.GetUtcNow(), []);

    /// <summary>True when this later page follows a recent first page and was not sent in that round yet.</summary>
    public bool TakeFollowPage(Type kind, int offset) =>
        listRounds.TryGetValue(kind, out var round) && time.GetUtcNow() - round.Start <= ServerTuning.ListRoundWindow && round.Served.Add(offset);

    /// <summary>The own bucket of a costly request kind, created on first use.</summary>
    public bool TryTakeCostly(Type kind, double perSecond, double burst)
    {
        if (!costly.TryGetValue(kind, out var limiter)) costly[kind] = limiter = new RateLimiter(time, perSecond, burst);
        return limiter.TryTake();
    }

    /// <summary>At most ChatBurst messages per ChatWindow (A29).</summary>
    public bool TryChat()
    {
        var now = time.GetUtcNow();
        while (chatTimes.Count > 0 && now - chatTimes.Peek() >= ProtocolInfo.ChatWindow) chatTimes.Dequeue();
        if (chatTimes.Count >= ProtocolInfo.ChatBurst) return false;
        chatTimes.Enqueue(now);
        return true;
    }
}
