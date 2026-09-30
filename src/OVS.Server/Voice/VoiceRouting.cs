using OVS.Shared.Permissions;
using OVS.Shared.Voice;

namespace OVS.Server.Voice;

public static class VoiceRouting
{
    /// <summary>Link transmission needs SpeakLinked; without it the packet only reaches the own channel.</summary>
    public static byte EffectiveTarget(Session sender, byte requested) =>
        requested == VoiceHeader.TargetLinked && sender.Permissions.Has(Permission.SpeakLinked)
            ? VoiceHeader.TargetLinked
            : VoiceHeader.TargetChannel;

    /// <param name="isMuted">Package 34: nobody in a muted channel is heard, not even via link. Link speech into it is.</param>
    public static List<Session> Recipients(IEnumerable<Session> sessions, Session sender, byte target,
        Func<Guid, IReadOnlySet<Guid>> linkedChannels, Func<Guid, bool>? isMuted = null)
    {
        if (!sender.Permissions.Has(Permission.Speak) || sender.ServerMuted || sender.SelfMuted) return [];
        if (isMuted?.Invoke(sender.ChannelId) == true) return [];

        var channels = new HashSet<Guid> { sender.ChannelId };
        if (EffectiveTarget(sender, target) == VoiceHeader.TargetLinked)
            channels.UnionWith(linkedChannels(sender.ChannelId));

        return sessions
            .Where(s => s != sender && !s.SelfDeafened && s.UdpEndpoint is not null && channels.Contains(s.ChannelId))
            .ToList();
    }
}

/// <summary>Token bucket per session: voice packets by default, Package 86 also uses it for requests.</summary>
public sealed class RateLimiter(TimeProvider time, double perSecond = 60, double burst = 10)
{
    readonly double perSecond = perSecond, burst = burst;
    double tokens = burst;
    long last = time.GetTimestamp();

    public bool TryTake()
    {
        long now = time.GetTimestamp();
        tokens = Math.Min(burst, tokens + time.GetElapsedTime(last, now).TotalSeconds * perSecond);
        last = now;
        if (tokens < 1) return false;
        tokens -= 1;
        return true;
    }
}
