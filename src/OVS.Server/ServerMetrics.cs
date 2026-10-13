using System.Diagnostics;
using System.Diagnostics.Metrics;
using OVS.Shared;

namespace OVS.Server;

/// <summary>
/// Package 147 (A157): the server's meter "OVS.Server", one per run (made and disposed with its container). While nobody
/// listens (OpenTelemetry off) every count returns at once and allocates nothing. Chat counts carry the channel id only,
/// never a text, nickname or fingerprint.
/// </summary>
internal sealed class ServerMetrics : IDisposable
{
    /// <summary>The tag of a control message's type.</summary>
    public const string MessageTypeTag = "ovs.message.type";
    /// <summary>The tag of the direction, <c>in</c> or <c>out</c>.</summary>
    public const string DirectionTag = "ovs.direction";
    /// <summary>The tag of a voice packet's path, <c>udp</c> or <c>tcp</c>.</summary>
    public const string VoicePathTag = "ovs.voice.path";
    /// <summary>The tag of why a voice packet was dropped.</summary>
    public const string DropReasonTag = "ovs.drop.reason";
    /// <summary>The tag of why a session's outbox was aborted.</summary>
    public const string AbortReasonTag = "ovs.abort.reason";
    /// <summary>The tag of a channel message's channel.</summary>
    public const string ChannelTag = "ovs.channel.id";

    /// <summary>Drop reasons: a packet from another address than the sender's control connection.</summary>
    public const string ForeignSource = "foreign_source";
    /// <summary>A source over its budget of bad packets, dropped before decryption.</summary>
    public const string PreFilter = "pre_filter";
    /// <summary>A sender over its voice budget.</summary>
    public const string Budget = "budget";
    /// <summary>A packet that is no valid voice of a session.</summary>
    public const string Invalid = "invalid";

    readonly Counter<long> requests, voicePackets, voiceBytes, voiceDrops, outboxAborts, rejections, controlFrames, controlBytes, chatMessages;
    readonly Histogram<int> chatSize;

    /// <summary>Makes the run's meter and its instruments.</summary>
    public ServerMetrics()
    {
        Meter = new Meter(new MeterOptions(ServerTelemetry.SourceName) { Version = BuildInfo.Current.Version });
        requests = Meter.CreateCounter<long>("ovs.requests", "{request}", "Handled requests by type and result code");
        voicePackets = Meter.CreateCounter<long>("ovs.voice.packets", "{packet}", "Voice packets in and out, by path");
        voiceBytes = Meter.CreateCounter<long>("ovs.voice.bytes", "By", "Voice bytes in and out, by path");
        voiceDrops = Meter.CreateCounter<long>("ovs.voice.drops", "{packet}", "Dropped voice packets by reason");
        outboxAborts = Meter.CreateCounter<long>("ovs.outbox.aborts", "{session}", "Sessions ended because their outbox overflowed or stalled");
        rejections = Meter.CreateCounter<long>("ovs.admission.rejections", "{hello}", "Refused connections by reason code");
        controlFrames = Meter.CreateCounter<long>("ovs.control.frames", "{frame}", "Control frames by message type and direction");
        controlBytes = Meter.CreateCounter<long>("ovs.control.bytes", "By", "Control bytes by message type and direction");
        chatMessages = Meter.CreateCounter<long>("ovs.chat.messages", "{message}", "Delivered chat messages; channel messages by channel id");
        chatSize = Meter.CreateHistogram<int>("ovs.chat.message.size", "By", "Size of delivered chat messages in UTF-8 bytes");
    }

    /// <summary>The run's meter; tests listen to exactly this one.</summary>
    public Meter Meter { get; }

    /// <summary>Reports the sessions online through <paramref name="count"/>, read when a reader collects.</summary>
    public void ObserveSessions(Func<int> count) => Meter.CreateObservableGauge("ovs.sessions", count, "{session}", "Sessions online");

    /// <summary>One handled request and its result (<see cref="ServerTelemetry.ResultOk"/> or the error code).</summary>
    public void Request(string type, string result) =>
        requests.Add(1, new KeyValuePair<string, object?>(ServerTelemetry.RequestTypeTag, type), new KeyValuePair<string, object?>(ServerTelemetry.ResultTag, result));

    /// <summary>One refused hello.</summary>
    public void Rejected(string code) => rejections.Add(1, new KeyValuePair<string, object?>(ServerTelemetry.ResultTag, code));

    /// <summary>One voice packet of <paramref name="bytes"/> bytes.</summary>
    public void Voice(bool incoming, bool tcp, int bytes)
    {
        if (!voicePackets.Enabled && !voiceBytes.Enabled) return;
        var tags = new TagList { { DirectionTag, incoming ? "in" : "out" }, { VoicePathTag, tcp ? "tcp" : "udp" } };
        voicePackets.Add(1, tags);
        voiceBytes.Add(bytes, tags);
    }

    /// <summary>One dropped voice packet.</summary>
    public void VoiceDropped(string reason) => voiceDrops.Add(1, new KeyValuePair<string, object?>(DropReasonTag, reason));

    /// <summary>One session whose outbox was aborted.</summary>
    public void OutboxAborted(string reason) => outboxAborts.Add(1, new KeyValuePair<string, object?>(AbortReasonTag, reason));

    /// <summary>One control frame of <paramref name="bytes"/> bytes.</summary>
    public void Control(string type, bool incoming, int bytes)
    {
        if (!controlFrames.Enabled && !controlBytes.Enabled) return;
        var tags = new TagList { { MessageTypeTag, type }, { DirectionTag, incoming ? "in" : "out" } };
        controlFrames.Add(1, tags);
        controlBytes.Add(bytes, tags);
    }

    /// <summary>One delivered chat message of <paramref name="bytes"/> UTF-8 bytes; a channel message names its channel only.</summary>
    public void Chat(Guid? channel, int bytes)
    {
        if (!chatMessages.Enabled && !chatSize.Enabled) return;
        var tags = channel is { } id ? new TagList { { ChannelTag, id.ToString() } } : default;
        chatMessages.Add(1, tags);
        chatSize.Record(bytes, tags);
    }

    /// <summary>Ends the meter with its run.</summary>
    public void Dispose() => Meter.Dispose();
}
