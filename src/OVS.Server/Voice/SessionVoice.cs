using System.Net;
using OVS.Server.Throttling;
using OVS.Shared.Voice;

namespace OVS.Server.Voice;

/// <summary>
/// One session's voice state, only touched under the voice gate of UdpVoiceServer (its UDP loop, and Package 141's
/// VoiceUp); <see cref="TcpVoice"/> and <see cref="DownlinkLoss"/> are also read elsewhere and say how.
/// </summary>
public sealed class SessionVoice(byte[] voiceKey, TimeProvider time) : IDisposable
{
    public VoiceCrypto Crypto { get; } = new(voiceKey);
    public IPEndPoint? UdpEndpoint { get; set; }
    public ReplayWindow Replay { get; } = new();
    public SeqCounter OutSeq { get; } = new();
    public RateLimiter Limiter { get; } = new(time, ServerTuning.VoicePerSecond, ServerTuning.VoiceBurst);
    volatile bool tcpVoice;
    /// <summary>Package 141: the newest frame number this sender sent over UDP, -1 before the first.</summary>
    public long LastUdpFrame { get; set; } = -1;

    /// <summary>Package 141 (A150): this client's voice goes over its TLS connection; set by VoiceUp, cleared by a UDP ping or voice packet. Routing reads it under the ServerState lock.</summary>
    public bool TcpVoice
    {
        get => tcpVoice;
        set => tcpVoice = value;
    }
    /// <summary>Package 133 (A142): the gaps in this sender's frame numbers, told to it in the ping answer.</summary>
    public LossWindow Uplink { get; } = new(time);
    long downlinkLoss; // packed, so another thread never reads half of it

    /// <summary>Package 133 (A142): the loss this client reported from the server (used by Package 136); written by the UDP loop.</summary>
    public LossReport DownlinkLoss
    {
        get
        {
            long packed = Interlocked.Read(ref downlinkLoss);
            return new LossReport((uint)(packed >> 32), (uint)packed);
        }
        set
        {
            Interlocked.Exchange(ref downlinkLoss, (long)value.Expected << 32 | value.Missed);
            // Package 136 (A145): a figure with nothing expected (nobody talked) keeps the last decision
            if (value.Expected > 0) lossyDownlink = value.Percent >= RelayPayload.CopyFromLossPercent;
            downlinkAt = time.GetTimestamp();
        }
    }

    /// <summary>Package 136 (A145): a report older than this (pings stopped) no longer earns copies.</summary>
    public static readonly TimeSpan CopiesStaleAfter = TimeSpan.FromSeconds(10);
    bool lossyDownlink;
    long downlinkAt;

    /// <summary>Package 136 (A145): this listener gets the speaker's previous frame with each one; only the UDP loop asks.</summary>
    public bool WantsCopies => lossyDownlink && time.GetElapsedTime(downlinkAt) <= CopiesStaleAfter;

    // Package 136: this sender's frames; only the UDP loop touches these
    (uint Seq, Guid Channel, byte Target, ReadOnlyMemory<byte> Opus, long At)? lastRelayed;
    long highestSeen = -1;

    /// <summary>Package 136 (review): the frame last sent on its own from the sender's copy; arriving late itself, it is not relayed again.</summary>
    public uint? RecoveredFrame { get; set; }

    /// <summary>
    /// Package 136 (A145): notes a frame of this sender (relayed: it had recipients) and tells what may go along with it
    /// as the frame before. Only a frame relayed from the same channel (a move while talking changes the listeners) to
    /// the same target within <see cref="RelayPayload.CopyMaxAge"/> counts as before it: <c>Kept</c> is that frame when it
    /// is exactly the one before; <c>Unseen</c> means the one before never arrived (lost on the way in), so the sender's
    /// copy of it may be used; a frame that arrived but was not relayed (muted, another target) must never travel as a
    /// copy. A frame counter starting again within one session (not reachable today: a reconnect makes a new session)
    /// would only turn the copies off, as no frame would be newer than those seen.
    /// </summary>
    public TrackedFrame TrackFrame(uint frame, Guid channel, byte target, ReadOnlyMemory<byte> opus, bool relayed)
    {
        long now = time.GetTimestamp();
        var last = lastRelayed;
        bool close = frame > 0 && last is { } l && l.Channel == channel && l.Target == target
            && time.GetElapsedTime(l.At, now) <= RelayPayload.CopyMaxAge;
        var kept = close && last!.Value.Seq == frame - 1 ? last.Value.Opus : default;
        bool unseen = close && highestSeen < frame - 1;
        if (frame > highestSeen) highestSeen = frame;
        if (relayed && (last is null || frame > last.Value.Seq)) lastRelayed = (frame, channel, target, opus, now);
        return new TrackedFrame(kept, unseen);
    }

    public void Dispose() => Crypto.Dispose();
}
