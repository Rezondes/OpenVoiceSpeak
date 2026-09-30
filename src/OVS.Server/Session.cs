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
    readonly Channel<Message> outbox = Channel.CreateBounded<Message>(new BoundedChannelOptions(1024) { SingleReader = true });

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

    public ChannelReader<Message> Outgoing => outbox.Reader;

    /// <summary>Never blocks. A client too slow to drain 1024 queued messages gets disconnected.</summary>
    public void Send(Message message)
    {
        if (!outbox.Writer.TryWrite(message)) outbox.Writer.TryComplete();
    }

    public void Close(Message? final = null)
    {
        if (final is not null) outbox.Writer.TryWrite(final);
        outbox.Writer.TryComplete();
    }

    public void Dispose() => Crypto.Dispose();
}
