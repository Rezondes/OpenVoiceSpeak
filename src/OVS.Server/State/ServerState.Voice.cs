using OVS.Server.Voice;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages.Connection;
using OVS.Shared.Protocol.Messages.VoiceRelay;

namespace OVS.Server.State;

internal sealed partial class ServerState
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
    public VoiceRoute VoiceRecipients(Session sender, byte requestedTarget)
    {
        lock (gate)
        {
            if (!IsCurrent(sender) || sender.IsService) return new VoiceRoute([], 0, Guid.Empty); // Package 191: a service session never talks
            sender.LastActivity = time.GetUtcNow(); // Package 169: speaking is not idle
            var afk = ActiveAfkChannel; // Package 169: nobody is heard in the AFK channel, as in a muted one
            return new VoiceRoute(VoiceRouting.Recipients(ServiceSessions.Visible(sessions.Values), sender, requestedTarget, LinkedChannels, id => id == afk || FindChannel(id)?.IsMuted == true),
                VoiceRouting.EffectiveTarget(sender, requestedTarget), sender.ChannelId);
        }
    }

    /// <summary>The channels linked to channelId.</summary>
    public IReadOnlySet<Guid> LinkedChannels(Guid channelId)
    {
        lock (gate) return data.Links.Where(channelLink => channelLink.Touches(channelId)).Select(channelLink => channelLink.Other(channelId)).ToHashSet();
    }
}
