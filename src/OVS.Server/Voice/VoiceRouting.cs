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

    public static List<Session> Recipients(IEnumerable<Session> sessions, Session sender, byte target,
        Func<Guid, IReadOnlySet<Guid>> linkedChannels)
    {
        if (!sender.Permissions.Has(Permission.Speak) || sender.ServerMuted || sender.SelfMuted) return [];

        var channels = new HashSet<Guid> { sender.ChannelId };
        if (EffectiveTarget(sender, target) == VoiceHeader.TargetLinked)
            channels.UnionWith(linkedChannels(sender.ChannelId));

        return sessions
            .Where(s => s != sender && !s.SelfDeafened && s.UdpEndpoint is not null && channels.Contains(s.ChannelId))
            .ToList();
    }
}

/// <summary>Token bucket per session.</summary>
public sealed class RateLimiter(TimeProvider time)
{
    const double PerSecond = 60;
    const double Burst = 10;
    double tokens = Burst;
    long last = time.GetTimestamp();

    public bool TryTake()
    {
        long now = time.GetTimestamp();
        tokens = Math.Min(Burst, tokens + time.GetElapsedTime(last, now).TotalSeconds * PerSecond);
        last = now;
        if (tokens < 1) return false;
        tokens -= 1;
        return true;
    }
}
