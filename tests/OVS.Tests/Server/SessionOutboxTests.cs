using OVS.Server;
using OVS.Shared.Protocol.Framing;
using OVS.Shared.Protocol.Messages.Connection;
using OVS.Shared.Protocol.Messages.ServerInfo;
using OVS.Shared.Protocol.Messages.VoiceRelay;

namespace OVS.Tests.Server;

public sealed class SessionOutboxTests
{
    static readonly ServerIcon Big = new(null, null, new string('A', 700_000));

    [Fact]
    public void Send_Message_QueuedWithItsBytesUntilWritten()
    {
        var outbox = new SessionOutbox();
        outbox.Send(new Pong());

        Assert.True(outbox.Outgoing.TryRead(out var frame));
        Assert.Equal(FrameWriter.Encode(new Pong()), frame);
        Assert.Equal(frame.Length, outbox.QueuedBytes);
        outbox.Written(frame);
        Assert.Equal(0, outbox.QueuedBytes);
    }

    [Fact]
    public void SendFrame_NullFrame_AbortsAsTooLarge()
    {
        var outbox = new SessionOutbox();
        outbox.SendFrame(null);

        Assert.True(outbox.Aborted.IsCancellationRequested);
        Assert.Equal("Nachricht zu gross", outbox.AbortReason);
        Assert.True(outbox.Outgoing.Completion.IsCompleted);
    }

    [Fact]
    public void Send_OverByteLimit_AbortsWithFullBuffer()
    {
        var outbox = new SessionOutbox();
        for (int index = 0; index < 20; index++) outbox.Send(Big);

        Assert.True(outbox.Aborted.IsCancellationRequested);
        Assert.Equal("Sendepuffer voll", outbox.AbortReason);
        Assert.True(outbox.QueuedBytes <= ServerTuning.MaxOutboxBytes, $"{outbox.QueuedBytes} bytes queued");
    }

    [Fact]
    public void Send_OverMessageLimit_Aborts()
    {
        var outbox = new SessionOutbox();
        for (int index = 0; index < ServerTuning.MaxOutboxMessages; index++) outbox.Send(new Pong());
        Assert.False(outbox.Aborted.IsCancellationRequested);

        outbox.Send(new Pong());
        Assert.True(outbox.Aborted.IsCancellationRequested);
    }

    [Fact]
    public void Send_AfterAbort_DroppedQuietly()
    {
        var outbox = new SessionOutbox();
        outbox.Abort("erster Grund");
        outbox.Send(new Pong());
        outbox.Abort("zweiter Grund");

        Assert.Equal(0, outbox.QueuedBytes);
        Assert.False(outbox.Outgoing.TryRead(out _));
        Assert.Equal("erster Grund", outbox.AbortReason);
    }

    [Fact]
    public void SendVoice_SlowReader_DroppedNotAborted()
    {
        var outbox = new SessionOutbox();
        var frame = SessionOutbox.Encode(new VoiceDown(2, 0, 0, Convert.ToBase64String(new byte[1275])));
        for (int index = 0; index < 1000; index++) outbox.SendVoice(frame);

        Assert.Null(outbox.AbortReason);
        Assert.Equal(ServerTuning.MaxQueuedVoice, outbox.Outgoing.Count);
        outbox.Send(new Pong());
        Assert.Equal(ServerTuning.MaxQueuedVoice + 1, outbox.Outgoing.Count);
    }

    [Fact]
    public void SendVoice_OneFrame_CountsItsBytes()
    {
        var outbox = new SessionOutbox();
        var frame = SessionOutbox.Encode(new Pong())!;
        outbox.SendVoice(frame);

        Assert.Equal(frame.Length, outbox.QueuedBytes);
        Assert.Equal(1, outbox.Outgoing.Count);
    }

    [Fact]
    public void SendVoice_NullFrame_DroppedNotAborted()
    {
        var outbox = new SessionOutbox();
        outbox.SendVoice(null);

        Assert.Equal(0, outbox.Outgoing.Count);
        Assert.False(outbox.Aborted.IsCancellationRequested);
    }

    [Fact]
    public void Close_WithFinalMessage_CountsItsBytes()
    {
        var outbox = new SessionOutbox();
        outbox.Close(new Pong());

        Assert.Equal(FrameWriter.Encode(new Pong()).Length, outbox.QueuedBytes);
    }

    [Fact]
    public void SendVoice_OverHalfTheBytes_Dropped()
    {
        var outbox = new SessionOutbox();
        for (int index = 0; index < 6; index++) outbox.Send(Big); // about 4.2 MB, past half of the byte limit
        long queued = outbox.QueuedBytes;

        outbox.SendVoice(SessionOutbox.Encode(new Pong()));
        Assert.Equal(queued, outbox.QueuedBytes);
        Assert.Null(outbox.AbortReason);
    }

    [Fact]
    public void Close_WithFinalMessage_QueuedThenCompleted()
    {
        var outbox = new SessionOutbox();
        outbox.Close(new Pong());
        outbox.Send(new Pong());

        Assert.True(outbox.Outgoing.TryRead(out var frame));
        Assert.Equal(FrameWriter.Encode(new Pong()), frame);
        Assert.False(outbox.Outgoing.TryRead(out _));
        Assert.True(outbox.Outgoing.Completion.IsCompleted);
        Assert.False(outbox.Aborted.IsCancellationRequested);
    }

    [Fact]
    public void Encode_OverFrameLimit_Null() =>
        Assert.Null(SessionOutbox.Encode(new ServerIcon(null, null, new string('A', FrameReader.MaxFrameSize))));
}
