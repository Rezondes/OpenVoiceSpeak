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
        Assert.True(RelayPayload.TryParse(plain, out var speakerSeq, out var opus));
        Assert.Equal(999u, speakerSeq);
        Assert.Equal(Opus, opus);
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
