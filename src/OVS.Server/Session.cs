using System.Net;
using OVS.Server.Throttling;
using OVS.Shared.IO;
using OVS.Shared.Permissions;
using OVS.Shared.Voice;

namespace OVS.Server;

/// <summary>One connected, authenticated client.</summary>
public sealed class Session(uint id, string fingerprint, string nickname, IPAddress ip, byte[] voiceKey, TimeProvider time)
    : IDisposable
{
    public uint Id { get; } = id;
    public string Fingerprint { get; } = fingerprint;
    public string Nickname { get; } = nickname;
    public IPAddress Ip { get; } = ip;
    public byte[] VoiceKey { get; } = voiceKey;

    /// <summary>The frames for the write loop; safe from any thread.</summary>
    public SessionOutbox Outbox { get; } = new();

    /// <summary>The request, list, costly and chat budgets (under the ServerState lock) and the voice flood rule (under the voice gate).</summary>
    public SessionThrottle Throttle { get; } = new(time);

    // Guarded by the ServerState lock.
    public Guid ChannelId { get; set; }
    public bool SelfMuted { get; set; }
    public bool SelfDeafened { get; set; }
    public bool ServerMuted { get; set; }
    public Permission Permissions { get; set; }
    public IReadOnlyList<Guid> GroupIds { get; set; } = [];
    /// <summary>Package 94: wrong channel passwords per channel, the first of the window and the end of a block.</summary>
    public Dictionary<Guid, (int Count, DateTimeOffset First, DateTimeOffset BlockedUntil)> ChannelPasswordFailures { get; } = [];
    /// <summary>Package 75: the backup upload in progress (one per session), its hidden file and the bytes stored.</summary>
    public (string Id, string Path, long Received)? Upload { get; set; }

    // Voice state, only touched under the voice gate of UdpVoiceServer (its UDP loop, and Package 141's VoiceUp).
    public VoiceCrypto Crypto { get; } = new(voiceKey);
    public IPEndPoint? UdpEndpoint { get; set; }
    public ReplayWindow Replay { get; } = new();
    public SeqCounter OutSeq { get; } = new();
    public RateLimiter Limiter { get; } = new(time, Limits.VoicePerSecond, Limits.VoiceBurst);
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
    public (ReadOnlyMemory<byte> Kept, bool Unseen) TrackFrame(uint frame, Guid channel, byte target, ReadOnlyMemory<byte> opus, bool relayed)
    {
        long now = time.GetTimestamp();
        var last = lastRelayed;
        bool close = frame > 0 && last is { } l && l.Channel == channel && l.Target == target
            && time.GetElapsedTime(l.At, now) <= RelayPayload.CopyMaxAge;
        var kept = close && last!.Value.Seq == frame - 1 ? last.Value.Opus : default;
        bool unseen = close && highestSeen < frame - 1;
        if (frame > highestSeen) highestSeen = frame;
        if (relayed && (last is null || frame > last.Value.Seq)) lastRelayed = (frame, channel, target, opus, now);
        return (kept, unseen);
    }

    // ---- Package 70: statistics of this session, added to the UserRecord when it ends ----

    public DateTimeOffset ConnectedAt { get; } = time.GetUtcNow();
    /// <summary>Guarded by the ServerState lock.</summary>
    public int ChatMessages { get; set; }
    /// <summary>Hash of the logo last sent to this session; guarded by the ServerState lock.</summary>
    public string? ServedIconHash { get; set; }
    long voiceFrames;

    /// <summary>Called by the UDP loop for each relayed voice packet; no lock on that path.</summary>
    public void CountVoiceFrame() => Interlocked.Increment(ref voiceFrames);

    /// <summary>20 ms per relayed voice packet.</summary>
    public TimeSpan SpeechTime => TimeSpan.FromMilliseconds(20 * Interlocked.Read(ref voiceFrames));

    // ---- Package 81: log pages and searches, one after the other on the thread pool, never under the state lock ----

    const int MaxQueuedLogJobs = 4;
    readonly object logJobGate = new();
    Task logJobs = Task.CompletedTask;
    int queuedLogJobs;

    /// <summary>Runs job after the session's earlier ones; false when MaxQueuedLogJobs are waiting already.</summary>
    public bool TryQueueLogJob(Action job)
    {
        if (Interlocked.Increment(ref queuedLogJobs) > MaxQueuedLogJobs)
        {
            Interlocked.Decrement(ref queuedLogJobs);
            return false;
        }
        lock (logJobGate)
            logJobs = logJobs.ContinueWith(_ =>
            {
                try
                {
                    job();
                }
                finally
                {
                    Interlocked.Decrement(ref queuedLogJobs);
                }
            }, TaskScheduler.Default);
        return true;
    }

    // ---- Package 82: the prepared log download (one per session), a file in logs-export deleted with the session ----

    (string Id, string Path)? logDownload;
    bool logDownloadsEnded;

    /// <summary>Replaces the prepared download (its file is deleted); false once the session ended, the caller deletes the file then.</summary>
    public bool SetLogDownload(string id, string path)
    {
        lock (logJobGate)
        {
            if (logDownloadsEnded) return false;
            if (logDownload is { } old) SafeFile.DeleteQuietly(old.Path);
            logDownload = (id, path);
            return true;
        }
    }

    public string? LogDownloadPath(string? id)
    {
        lock (logJobGate) return logDownload is { } d && d.Id == id ? d.Path : null;
    }

    /// <summary>After the last chunk (finished only) or when the session ends (ended, later downloads are refused).</summary>
    public void DropLogDownload(bool ended = false)
    {
        lock (logJobGate)
        {
            logDownloadsEnded |= ended;
            if (logDownload is { } d) SafeFile.DeleteQuietly(d.Path);
            logDownload = null;
        }
    }

    public void Dispose() => Crypto.Dispose(); // the outbox stays usable: log jobs may still send after the end
}
