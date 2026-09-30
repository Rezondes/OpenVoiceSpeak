using System.Net;
using System.Threading.Channels;
using OVS.Server.Voice;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;
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
    /// <summary>Package 75: the backup upload in progress (one per session), its hidden file and the bytes stored.</summary>
    public (string Id, string Path, long Received)? Upload { get; set; }

    // Voice state, only touched by the UDP receive loop.
    public VoiceCrypto Crypto { get; } = new(voiceKey);
    public IPEndPoint? UdpEndpoint { get; set; }
    public ReplayWindow Replay { get; } = new();
    public SeqCounter OutSeq { get; } = new();
    public RateLimiter Limiter { get; } = new(time);

    readonly Queue<DateTimeOffset> chatTimes = new();

    // ---- Package 70: statistics of this session, added to the UserRecord when it ends ----

    public DateTimeOffset ConnectedAt { get; } = time.GetUtcNow();
    /// <summary>Guarded by the ServerState lock.</summary>
    public int ChatMessages { get; set; }
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
        var now = time.GetUtcNow();
        if (now - lastRefused > Limits.FloodPause) floodSince = now;
        lastRefused = now;
        return now - floodSince >= Limits.FloodDisconnectAfter;
    }

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
