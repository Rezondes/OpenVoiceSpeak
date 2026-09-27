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

    // Voice state, only touched by the UDP receive loop.
    public VoiceCrypto Crypto { get; } = new(voiceKey);
    public IPEndPoint? UdpEndpoint { get; set; }
    public ReplayWindow Replay { get; } = new();
    public SeqCounter OutSeq { get; } = new();
    public RateLimiter Limiter { get; } = new(time);

    readonly Queue<DateTimeOffset> chatTimes = new();

    /// <summary>At most ChatBurst messages per ChatWindow (A29). Guarded by the ServerState lock.</summary>
    public bool TryChat()
    {
        var now = time.GetUtcNow();
        while (chatTimes.Count > 0 && now - chatTimes.Peek() >= ProtocolInfo.ChatWindow) chatTimes.Dequeue();
        if (chatTimes.Count >= ProtocolInfo.ChatBurst) return false;
        chatTimes.Enqueue(now);
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
