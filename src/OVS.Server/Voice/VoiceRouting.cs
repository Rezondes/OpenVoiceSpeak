using OVS.Shared.Permissions;
using OVS.Shared.Voice;

namespace OVS.Server.Voice;

/// <summary>Who hears a voice packet.</summary>
internal static class VoiceRouting
{
    /// <summary>Link transmission needs SpeakLinked; without it the packet only reaches the own channel.</summary>
    public static byte EffectiveTarget(Session sender, byte requested) =>
        requested == VoiceHeader.TargetLinked && sender.Permissions.Has(Permission.SpeakLinked)
            ? VoiceHeader.TargetLinked
            : VoiceHeader.TargetChannel;

    /// <summary>Package 141: a listener on voice over TCP is reached like a bound UDP endpoint.</summary>
    /// <param name="isMuted">Package 34: nobody in a muted channel is heard, not even via link. Link speech into it is.</param>
    /// <param name="sessions">Everyone online.</param>
    /// <param name="sender">The one talking.</param>
    /// <param name="target">TargetChannel or TargetLinked.</param>
    /// <param name="linkedChannels">The channels linked to a channel.</param>
    public static List<Session> Recipients(IEnumerable<Session> sessions, Session sender, byte target,
        Func<Guid, IReadOnlySet<Guid>> linkedChannels, Func<Guid, bool>? isMuted = null)
    {
        if (!sender.Permissions.Has(Permission.Speak) || sender.ServerMuted || sender.SelfMuted) return [];
        if (isMuted?.Invoke(sender.ChannelId) == true) return [];

        var channels = new HashSet<Guid> { sender.ChannelId };
        if (EffectiveTarget(sender, target) == VoiceHeader.TargetLinked)
            channels.UnionWith(linkedChannels(sender.ChannelId));

        return sessions
            .Where(session => session != sender && !session.SelfDeafened && (session.Voice.UdpEndpoint is not null || session.Voice.TcpVoice) && channels.Contains(session.ChannelId))
            .ToList();
    }
}
