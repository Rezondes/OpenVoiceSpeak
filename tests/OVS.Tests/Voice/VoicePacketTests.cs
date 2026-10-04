using System.Security.Cryptography;
using OVS.Shared.Voice;

namespace OVS.Tests.Voice;

public class VoicePacketTests
{
    static readonly byte[] Key = RandomNumberGenerator.GetBytes(32);
    static readonly byte[] Opus = RandomNumberGenerator.GetBytes(60);

    [Fact]
    public void SealOpen_ClientToServer_RoundTrips()
    {
        using var crypto = new VoiceCrypto(Key);
        var header = new VoiceHeader(PacketType.Voice, 7, 42, VoiceHeader.TargetLinked);
        var packet = crypto.Seal(Direction.ClientToServer, header, Opus);

        Assert.True(crypto.TryOpen(Direction.ClientToServer, packet, out var h, out var plain));
        Assert.Equal(header, h);
        Assert.Equal(Opus, plain);
    }

    [Fact]
    public void SealOpen_ServerToClient_RoundTripsSpeakerSeq()
    {
        using var crypto = new VoiceCrypto(Key);
        var packet = crypto.Seal(Direction.ServerToClient, new VoiceHeader(PacketType.Voice, 3, 1, 0), RelayPayload.Build(999, Opus));

        Assert.True(crypto.TryOpen(Direction.ServerToClient, packet, out _, out var plain));
        Assert.True(RelayPayload.TryParse(plain, out var speakerSeq, out var opus, out var copy));
        Assert.Equal(999u, speakerSeq);
        Assert.Equal(Opus, opus);
        Assert.Null(copy);
    }

    /// <summary>Package 136 (A145): [frameSeq 4][length of current 2][current][previous, optional], both within the limits.</summary>
    [Fact]
    public void RelayPayload_WithCopy_RoundTrip_SizeLimit()
    {
        Assert.Equal(14, OVS.Shared.Protocol.ProtocolInfo.Version);
        var previous = RandomNumberGenerator.GetBytes(50);

        var single = RelayPayload.Build(7, Opus);
        Assert.Equal(6 + Opus.Length, single.Length);
        Assert.True(RelayPayload.TryParse(single, out var seq, out var opus, out var copy));
        Assert.Equal(7u, seq);
        Assert.Equal(Opus, opus);
        Assert.Null(copy);

        var both = RelayPayload.Build(8, Opus, previous);
        Assert.Equal(6 + Opus.Length + previous.Length, both.Length);
        Assert.True(RelayPayload.TryParse(both, out seq, out opus, out copy));
        Assert.Equal(8u, seq);
        Assert.Equal(Opus, opus);
        Assert.Equal(previous, copy);
        // the server takes the parts without copying
        Assert.True(RelayPayload.TryRead(both, out seq, out var current, out var rest));
        Assert.Equal(Opus, both[current]);
        Assert.Equal(previous, both[rest]);

        // the largest frame fits, alone; a copy that would not fit the packet is left out by Build
        var largest = RandomNumberGenerator.GetBytes(VoiceHeader.MaxOpusSize);
        var big = RelayPayload.Build(9, largest, largest);
        Assert.Equal(6 + VoiceHeader.MaxOpusSize, big.Length);
        Assert.True(RelayPayload.TryParse(big, out _, out opus, out copy));
        Assert.Equal(largest, opus);
        Assert.Null(copy);
        using (var crypto = new VoiceCrypto(Key))
            crypto.Seal(Direction.ServerToClient, new VoiceHeader(PacketType.Voice, 1, 1, 0), RelayPayload.Build(9, Opus, RandomNumberGenerator.GetBytes(RelayPayload.MaxSize - 6 - Opus.Length)));
        Assert.Throws<ArgumentException>(() => RelayPayload.Build(1, new byte[VoiceHeader.MaxOpusSize + 1]));
        Assert.Throws<ArgumentException>(() => RelayPayload.Build(1, []));

        // refused: too short, an empty or oversize current frame, a length beyond the payload, an oversize copy or payload
        static byte[] Raw(int length, int current, int rest)
        {
            var raw = new byte[6 + current + rest];
            raw[4] = (byte)(length >> 8);
            raw[5] = (byte)length;
            return raw;
        }
        Assert.False(RelayPayload.TryParse(new byte[5], out _, out _, out _));
        Assert.False(RelayPayload.TryParse(Raw(0, 0, 10), out _, out _, out _));
        Assert.False(RelayPayload.TryParse(Raw(VoiceHeader.MaxOpusSize + 1, VoiceHeader.MaxOpusSize + 1, 0), out _, out _, out _));
        Assert.False(RelayPayload.TryParse(Raw(11, 10, 0), out _, out _, out _));
        Assert.False(RelayPayload.TryParse(Raw(0xFFFF, 10, 0), out _, out _, out _));
        Assert.False(RelayPayload.TryParse(Raw(10, 10, VoiceHeader.MaxOpusSize + 1), out _, out _, out _));
        Assert.False(RelayPayload.TryParse(Raw(VoiceHeader.MaxOpusSize, VoiceHeader.MaxOpusSize, RelayPayload.MaxSize - 6 - VoiceHeader.MaxOpusSize + 1), out _, out _, out _));
        Assert.True(RelayPayload.TryParse(Raw(VoiceHeader.MaxOpusSize, VoiceHeader.MaxOpusSize, RelayPayload.MaxSize - 6 - VoiceHeader.MaxOpusSize), out _, out _, out copy));
        Assert.Equal(RelayPayload.MaxSize - 6 - VoiceHeader.MaxOpusSize, copy!.Length);
    }

    [Fact]
    public void SealOpen_EmptyPayload_ForHelloAndPing()
    {
        using var crypto = new VoiceCrypto(Key);
        var packet = crypto.Seal(Direction.ClientToServer, new VoiceHeader(PacketType.Hello, 1, 0, 0), []);
        Assert.Equal(26, packet.Length);
        Assert.True(crypto.TryOpen(Direction.ClientToServer, packet, out var h, out var plain));
        Assert.Equal(PacketType.Hello, h.Type);
        Assert.Empty(plain);
    }

    [Fact]
    public void TryOpen_AnyByteFlipped_False()
    {
        using var crypto = new VoiceCrypto(Key);
        var packet = crypto.Seal(Direction.ClientToServer, new VoiceHeader(PacketType.Voice, 7, 42, 0), Opus);
        for (int i = 0; i < packet.Length; i++)
        {
            var tampered = (byte[])packet.Clone();
            tampered[i] ^= 0x01;
            Assert.False(crypto.TryOpen(Direction.ClientToServer, tampered, out _, out _), $"byte {i}");
        }
    }

    [Fact]
    public void TryOpen_WrongKey_False()
    {
        using var a = new VoiceCrypto(Key);
        using var b = new VoiceCrypto(RandomNumberGenerator.GetBytes(32));
        var packet = a.Seal(Direction.ClientToServer, new VoiceHeader(PacketType.Voice, 1, 1, 0), Opus);
        Assert.False(b.TryOpen(Direction.ClientToServer, packet, out _, out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(25)]
    [InlineData(1401)]
    public void TryOpen_BadLength_False(int length)
    {
        using var crypto = new VoiceCrypto(Key);
        Assert.False(crypto.TryOpen(Direction.ClientToServer, new byte[length], out _, out _));
    }

    [Fact]
    public void TryOpen_WrongDirection_False()
    {
        using var crypto = new VoiceCrypto(Key);
        var packet = crypto.Seal(Direction.ClientToServer, new VoiceHeader(PacketType.Voice, 1, 5, 0), Opus);
        Assert.False(crypto.TryOpen(Direction.ServerToClient, packet, out _, out _));
    }

    /// <summary>Package 133 (A142): the ping carries the sender's time and the loss it sees from the other side.</summary>
    [Fact]
    public void PingPayload_RoundTrip()
    {
        var ping = new PingPayload(1_234_567_890_123, new LossReport(250, 25));
        var bytes = ping.Build();
        Assert.Equal(PingPayload.Size, bytes.Length);
        Assert.True(PingPayload.TryParse(bytes, out var back));
        Assert.Equal(ping, back);
        Assert.Equal(10.0, back.Loss.Percent);
        Assert.Equal(0.0, new LossReport(0, 0).Percent); // nothing expected (a talk pause) is no loss

        using var crypto = new VoiceCrypto(Key);
        var packet = crypto.Seal(Direction.ClientToServer, new VoiceHeader(PacketType.Ping, 1, 0, 0), bytes);
        Assert.True(crypto.TryOpen(Direction.ClientToServer, packet, out _, out var plain));
        Assert.True(PingPayload.TryParse(plain, out var opened));
        Assert.Equal(ping, opened);

        Assert.False(PingPayload.TryParse([], out _)); // an empty ping is still answered, but carries nothing
        Assert.False(PingPayload.TryParse(new byte[PingPayload.Size - 1], out _));
    }

    /// <summary>Package 133: gaps in a stream's frame numbers are losses, per 5 s window; a talk pause has no gap.</summary>
    [Fact]
    public void LossWindow_CountsGapsPerStream()
    {
        var time = new OVS.Tests.TestSupport.ManualTimeProvider();
        var window = new LossWindow(time);
        foreach (uint n in new uint[] { 0, 1, 3, 4 }) window.Count(7, n); // 2 missing
        foreach (uint n in new uint[] { 10, 11, 12 }) window.Count(8, n); // another speaker, starting anywhere
        Assert.Equal(new LossReport(8, 1), window.Report()); // the window so far, until the first one is complete
        window.Count(7, 2); // late after all
        Assert.Equal(new LossReport(8, 0), window.Report());

        time.Advance(TimeSpan.FromSeconds(5));
        window.Count(7, 5); // after a pause: the frame number goes on, no gap (and a silence of 1 s starts afresh anyway)
        Assert.Equal(new LossReport(8, 0), window.Report()); // the last complete window
        window.Count(7, 9);
        time.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(new LossReport(5, 3), window.Report());
    }

    /// <summary>
    /// Package 133 (review): frames that routing kept away (deafened, another channel) are no loss, a hostile jump never
    /// reads above 100 %, and only a gap counted in this window is taken back by a late frame.
    /// </summary>
    [Fact]
    public void LossWindow_FreshStarts_TakesBackOnlyCountedGaps()
    {
        var time = new OVS.Tests.TestSupport.ManualTimeProvider();
        var window = new LossWindow(time);
        window.Count(1, 0);
        window.Count(1, 1);
        time.Advance(TimeSpan.FromSeconds(2)); // deafened for 2 s: the speaker's frames went to others
        window.Count(1, 101);
        window.Count(1, 102);
        Assert.Equal(new LossReport(4, 0), window.Report());
        window.Count(1, 102 + LossWindow.MaxGap + 1); // a jump beyond the cap
        window.Count(1, uint.MaxValue); // hostile
        window.Count(1, 5); // far behind what it said before: nothing to place
        Assert.Equal(new LossReport(6, 0), window.Report());

        window.Count(2, 10);
        window.Count(2, 12); // 11 missing
        window.Count(2, 12); // a duplicate
        window.Count(2, 9); // before the stream started
        Assert.Equal(new LossReport(9, 1), window.Report());
        window.Count(2, 11); // late after all
        window.Count(2, 11);
        Assert.Equal(new LossReport(9, 0), window.Report());
        Assert.True(window.Report().Percent <= 100);

        var rolled = new LossWindow(time);
        time.Advance(TimeSpan.FromSeconds(4.9));
        rolled.Count(3, 0);
        rolled.Count(3, 2); // 1 missing
        time.Advance(TimeSpan.FromSeconds(0.2));
        rolled.Count(3, 1); // late, but its gap belongs to the last window
        Assert.Equal(new LossReport(3, 1), rolled.Report());
        time.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(new LossReport(0, 0), rolled.Report());
    }

    [Fact]
    public void SeqCounter_CountsFromZero_OverflowThrows()
    {
        var counter = new SeqCounter();
        Assert.Equal(0u, counter.Next());
        Assert.Equal(1u, counter.Next());

        counter.SetNextForTest(uint.MaxValue);
        Assert.Equal(uint.MaxValue, counter.Next());
        Assert.Throws<OverflowException>(() => counter.Next());
    }
}
