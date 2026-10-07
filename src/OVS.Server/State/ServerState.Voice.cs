using OVS.Server.Voice;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages.Connection;
using OVS.Shared.Protocol.Messages.VoiceRelay;

namespace OVS.Server.State;

public sealed partial class ServerState
{
    // ---- Voice ----

    /// <summary>Package 88: lock-free, so a UDP flood never waits on or holds up the control channel.</summary>
    public Session? FindSession(uint id) => voiceSessions.GetValueOrDefault(id);

    /// <summary>Package 141 (A150): where the control connection hands VoiceUp, set by the voice server; never under the lock.</summary>
    public Func<Session, VoiceUp, Task>? VoiceOverTcp { get; set; }

    /// <summary>Package 141: a sustained VoiceUp flood ends the session like a request flood (Package 86).</summary>
    public void DisconnectFlood(Session session)
    {
        lock (gate) RemoveLocked(session, new Disconnected(ReasonCodes.RateLimited));
    }

    /// <returns>Package 136: also the sender's channel, read under the lock, as a copy must not cross a move.</returns>
    public (List<Session> Recipients, byte Target, Guid Channel) VoiceRecipients(Session sender, byte requestedTarget)
    {
        lock (gate)
        {
            if (!IsCurrent(sender)) return ([], 0, Guid.Empty);
            return (VoiceRouting.Recipients(sessions.Values, sender, requestedTarget, LinkedChannels, id => FindChannel(id)?.IsMuted == true),
                VoiceRouting.EffectiveTarget(sender, requestedTarget), sender.ChannelId);
        }
    }

    public IReadOnlySet<Guid> LinkedChannels(Guid channelId)
    {
        lock (gate) return data.Links.Where(l => l.Touches(channelId)).Select(l => l.Other(channelId)).ToHashSet();
    }
}
