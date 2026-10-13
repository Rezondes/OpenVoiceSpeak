namespace OVS.Server.Throttling;

/// <summary>
/// Package 94: wrong channel passwords per session and channel; the ChannelPasswordFailures-th within ChannelPasswordWindow
/// blocks that channel for ChannelPasswordBlock. A session's counts are forgotten when it ends. Guarded by the ServerState lock.
/// </summary>
public sealed class ChannelPasswordGuessLimit
{
    /// <summary>Per session, then per channel: the count, the first of the window and the end of a block.</summary>
    readonly Dictionary<uint, Dictionary<Guid, (int Count, DateTimeOffset First, DateTimeOffset BlockedUntil)>> failures = [];

    /// <summary>True while this session is blocked from this channel.</summary>
    public bool IsBlocked(uint sessionId, Guid channelId, DateTimeOffset now) =>
        failures.TryGetValue(sessionId, out var channels) && channels.TryGetValue(channelId, out var attempts) && now < attempts.BlockedUntil;

    /// <summary>The right password: the count of this channel starts again.</summary>
    public void Forget(uint sessionId, Guid channelId)
    {
        if (failures.TryGetValue(sessionId, out var channels)) channels.Remove(channelId);
    }

    /// <summary>Counts a wrong one and returns its number in the window.</summary>
    public int RecordFailure(uint sessionId, Guid channelId, DateTimeOffset now)
    {
        if (!failures.TryGetValue(sessionId, out var channels))
            failures[sessionId] = channels = [];
        channels.TryGetValue(channelId, out var attempts);
        if (now - attempts.First > ServerTuning.ChannelPasswordWindow || attempts.Count >= ServerTuning.ChannelPasswordFailures) attempts = (0, now, default);
        attempts.Count++;
        if (attempts.Count >= ServerTuning.ChannelPasswordFailures) attempts.BlockedUntil = now + ServerTuning.ChannelPasswordBlock;
        channels[channelId] = attempts;
        return attempts.Count;
    }

    /// <summary>The session ended: its counts go with it.</summary>
    public void ForgetSession(uint sessionId) => failures.Remove(sessionId);

    internal int SessionCount => failures.Count;
}
