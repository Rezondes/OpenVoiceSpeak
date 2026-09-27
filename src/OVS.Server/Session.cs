using System.Net;
using System.Threading.Channels;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;

namespace OVS.Server;

/// <summary>One connected, authenticated client.</summary>
public sealed class Session(uint id, string fingerprint, string nickname, IPAddress ip, byte[] voiceKey)
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

    public void Dispose() { }
}
