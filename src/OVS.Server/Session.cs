using System.Net;
using System.Threading.Channels;
using OVS.Server.Voice;
using OVS.Shared.Exceptions;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Framing;
using OVS.Shared.Voice;

namespace OVS.Server;

/// <summary>One connected, authenticated client.</summary>
public sealed class Session(uint id, string fingerprint, string nickname, IPAddress ip, byte[] voiceKey, TimeProvider time)
    : IDisposable
{
    readonly Channel<byte[]> outbox = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(Limits.MaxOutboxMessages) { SingleReader = true });
    readonly CancellationTokenSource aborted = new();
    long queuedBytes;
    volatile bool closing; // after Close or Abort: later messages are dropped quietly

    public uint Id { get; } = id;
    public string Fingerprint { get; } = fingerprint;
    public string Nickname { get; } = nickname;
    public IPAddress Ip { get; } = ip;
    public byte[] VoiceKey { get; } = voiceKey;

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
    DateTimeOffset voiceFloodSince, voiceLastRefused;

    /// <summary>Package 141: an invalid VoiceUp; true once they went on without a FloodPause for FloodDisconnectAfter, like refused requests.</summary>
    public bool RefuseVoice() => Flooding(ref voiceFloodSince, ref voiceLastRefused);

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

    readonly Queue<DateTimeOffset> chatTimes = new();

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

    /// <summary>At most ChatBurst messages per ChatWindow (A29). Guarded by the ServerState lock.</summary>
    public bool TryChat()
    {
        var now = time.GetUtcNow();
        while (chatTimes.Count > 0 && now - chatTimes.Peek() >= ProtocolInfo.ChatWindow) chatTimes.Dequeue();
        if (chatTimes.Count >= ProtocolInfo.ChatBurst) return false;
        chatTimes.Enqueue(now);
        return true;
    }

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
            if (logDownload is { } old) DeleteQuietly(old.Path);
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
            if (logDownload is { } d) DeleteQuietly(d.Path);
            logDownload = null;
        }
    }

    static void DeleteQuietly(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    // ---- Package 86: request budget, guarded by the ServerState lock ----

    readonly RateLimiter requests = new(time, Limits.RequestsPerSecond, Limits.RequestBurst);
    readonly Dictionary<Type, RateLimiter> costly = [];
    DateTimeOffset floodSince, lastRefused;

    /// <summary>Takes one request from the budget. Null when it may run, else false (refuse) or true (sustained flood, disconnect).</summary>
    public bool? TakeRequest()
    {
        if (requests.TryTake()) return null;
        return Flooding(ref floodSince, ref lastRefused);
    }

    /// <summary>Notes a refusal; true when refusals went on without a pause longer than FloodPause for FloodDisconnectAfter.</summary>
    bool Flooding(ref DateTimeOffset since, ref DateTimeOffset last)
    {
        var now = time.GetUtcNow();
        if (now - last > Limits.FloodPause) since = now;
        last = now;
        return now - since >= Limits.FloodDisconnectAfter;
    }

    /// <summary>Per list kind: when its first page was answered and which later pages were sent since.</summary>
    readonly Dictionary<Type, (DateTimeOffset Start, HashSet<int> Served)> listRounds = [];

    public void StartListRound(Type kind) => listRounds[kind] = (time.GetUtcNow(), []);

    /// <summary>True when this later page follows a recent first page and was not sent in that round yet.</summary>
    public bool TakeFollowPage(Type kind, int offset) =>
        listRounds.TryGetValue(kind, out var round) && time.GetUtcNow() - round.Start <= Limits.ListRoundWindow && round.Served.Add(offset);

    /// <summary>The own bucket of a costly request kind, created on first use.</summary>
    public bool TryTakeCostly(Type kind, double perSecond, double burst)
    {
        if (!costly.TryGetValue(kind, out var limiter)) costly[kind] = limiter = new RateLimiter(time, perSecond, burst);
        return limiter.TryTake();
    }

    // ---- Outbox: encoded frames, limited by count and bytes (Package 86) ----

    /// <summary>Frames for the write loop; call Written after each one.</summary>
    public ChannelReader<byte[]> Outgoing => outbox.Reader;

    /// <summary>Cancelled when the session must go at once (outbox over its limits); the connection closes without draining.</summary>
    public CancellationToken Aborted => aborted.Token;

    /// <summary>Why Aborted fired, for the log.</summary>
    public string? AbortReason { get; private set; }

    public long QueuedBytes => Interlocked.Read(ref queuedBytes);

    public void Written(byte[] frame) => Interlocked.Add(ref queuedBytes, -frame.Length);

    /// <summary>Never blocks, safe from any thread. A client that falls too far behind is aborted.</summary>
    public void Send(Message message) => SendFrame(Encode(message));

    /// <summary>Package 86: Broadcast encodes once for everyone. Null (too large for a frame) aborts like an overflow.</summary>
    public void SendFrame(byte[]? frame)
    {
        if (closing) return;
        if (frame is null)
        {
            Abort("Nachricht zu gross");
            return;
        }
        if (Interlocked.Add(ref queuedBytes, frame.Length) <= Limits.MaxOutboxBytes && outbox.Writer.TryWrite(frame)) return;
        Interlocked.Add(ref queuedBytes, -frame.Length);
        if (!closing) Abort("Sendepuffer voll");
    }

    /// <summary>
    /// Package 141: a VoiceDown frame for a listener on TCP. Never blocks and never aborts: dropped while the outbox already
    /// holds <see cref="Limits.MaxQueuedVoice"/> messages or half its bytes, so a slow reader only loses voice and the
    /// relay never waits for it.
    /// </summary>
    public void SendVoice(byte[]? frame)
    {
        if (closing || frame is null || outbox.Reader.Count >= Limits.MaxQueuedVoice || QueuedBytes + frame.Length > Limits.MaxOutboxBytes / 2) return;
        Interlocked.Add(ref queuedBytes, frame.Length);
        if (!outbox.Writer.TryWrite(frame)) Interlocked.Add(ref queuedBytes, -frame.Length);
    }

    /// <summary>The frame of a message, null when it exceeds the frame limit.</summary>
    public static byte[]? Encode(Message message)
    {
        try
        {
            return FrameWriter.Encode(message);
        }
        catch (ProtocolException)
        {
            return null;
        }
    }

    public void Abort(string reason)
    {
        lock (aborted)
        {
            if (aborted.IsCancellationRequested) return;
            AbortReason = reason;
            closing = true;
            outbox.Writer.TryComplete();
            aborted.Cancel();
        }
    }

    /// <summary>Queues final (even past the byte limit) and ends the outbox; the write loop drains it, then closes.</summary>
    public void Close(Message? final = null)
    {
        if (!closing && final is not null && Encode(final) is { } frame && outbox.Writer.TryWrite(frame))
            Interlocked.Add(ref queuedBytes, frame.Length);
        closing = true;
        outbox.Writer.TryComplete();
    }

    public void Dispose() => Crypto.Dispose(); // aborted stays usable: log jobs may still send after the end
}
