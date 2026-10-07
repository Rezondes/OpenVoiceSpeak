using System.Threading.Channels;
using OVS.Shared.Exceptions;
using OVS.Shared.Protocol.Framing;
using OVS.Shared.Protocol.Messages;

namespace OVS.Server;

/// <summary>
/// The encoded frames waiting for one session's write loop, limited by count and bytes (Package 86). Safe from any thread:
/// handlers under the ServerState lock, the TLS read loop (Pong), log jobs on the thread pool and the voice relay send;
/// the write loop reads and calls Written. The queue is a thread-safe channel, the byte count is Interlocked, the
/// closing flag volatile, and Abort runs under a lock of its own.
/// </summary>
public sealed class SessionOutbox
{
    readonly Channel<byte[]> outbox = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(ServerTuning.MaxOutboxMessages) { SingleReader = true });
    readonly CancellationTokenSource aborted = new();
    long queuedBytes;
    volatile bool closing; // after Close or Abort: later messages are dropped quietly

    /// <summary>Frames for the write loop; call Written after each one.</summary>
    public ChannelReader<byte[]> Outgoing => outbox.Reader;

    /// <summary>Cancelled when the session must go at once (outbox over its limits); the connection closes without draining.</summary>
    public CancellationToken Aborted => aborted.Token;

    /// <summary>Why Aborted fired, for the log.</summary>
    public string? AbortReason { get; private set; }

    /// <summary>Bytes of the frames queued and not yet written.</summary>
    public long QueuedBytes => Interlocked.Read(ref queuedBytes);

    /// <summary>The write loop is done with this frame: takes its bytes off the count.</summary>
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
        if (Interlocked.Add(ref queuedBytes, frame.Length) <= ServerTuning.MaxOutboxBytes && outbox.Writer.TryWrite(frame)) return;
        Interlocked.Add(ref queuedBytes, -frame.Length);
        if (!closing) Abort("Sendepuffer voll");
    }

    /// <summary>
    /// Package 141: a VoiceDown frame for a listener on TCP. Never blocks and never aborts: dropped while the outbox already
    /// holds <see cref="ServerTuning.MaxQueuedVoice"/> messages or half its bytes, so a slow reader only loses voice and the
    /// relay never waits for it.
    /// </summary>
    public void SendVoice(byte[]? frame)
    {
        if (closing || frame is null || outbox.Reader.Count >= ServerTuning.MaxQueuedVoice || QueuedBytes + frame.Length > ServerTuning.MaxOutboxBytes / 2) return;
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

    /// <summary>Ends the outbox at once and cancels <see cref="Aborted"/>; the first reason is kept, later calls do nothing.</summary>
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
}
