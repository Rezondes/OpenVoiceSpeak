using System.Buffers.Binary;
using System.Security.Cryptography;

namespace OVS.Shared.Voice;

public enum PacketType : byte { Hello = 0, Voice = 1, Ping = 2 }

public enum Direction : byte { ClientToServer = 0, ServerToClient = 1 }

/// <summary>
/// UDP voice packet: [type 1][sessionId 4 BE][seq 4 BE][target 1][ciphertext][GCM tag 16].
/// The 10 header bytes are authenticated as associated data.
/// </summary>
public readonly record struct VoiceHeader(PacketType Type, uint SessionId, uint Seq, byte Target)
{
    public const int Size = 10;
    public const int TagSize = 16;
    public const int MaxPacketSize = 1400;
    public const int MaxOpusSize = 1275;
    public const byte TargetChannel = 0;
    public const byte TargetLinked = 1;

    public static bool TryRead(ReadOnlySpan<byte> packet, out VoiceHeader header)
    {
        header = default;
        if (packet.Length < Size + TagSize || packet.Length > MaxPacketSize) return false;
        var type = (PacketType)packet[0];
        if (type > PacketType.Ping || packet[9] > TargetLinked) return false;
        header = new VoiceHeader(type,
            BinaryPrimitives.ReadUInt32BigEndian(packet[1..]),
            BinaryPrimitives.ReadUInt32BigEndian(packet[5..]),
            packet[9]);
        return true;
    }

    public void Write(Span<byte> dest)
    {
        dest[0] = (byte)Type;
        BinaryPrimitives.WriteUInt32BigEndian(dest[1..], SessionId);
        BinaryPrimitives.WriteUInt32BigEndian(dest[5..], Seq);
        dest[9] = Target;
    }
}

/// <summary>AES-GCM with the session's voice key. Not thread-safe; use from one thread.</summary>
public sealed class VoiceCrypto(byte[] key) : IDisposable
{
    readonly AesGcm aes = new(key, VoiceHeader.TagSize);

    public byte[] Seal(Direction direction, VoiceHeader header, ReadOnlySpan<byte> plaintext)
    {
        var packet = new byte[VoiceHeader.Size + plaintext.Length + VoiceHeader.TagSize];
        if (packet.Length > VoiceHeader.MaxPacketSize) throw new ArgumentException("Voice payload too large");
        header.Write(packet);
        Span<byte> nonce = stackalloc byte[12];
        Nonce(direction, header.Seq, nonce);
        aes.Encrypt(nonce, plaintext,
            packet.AsSpan(VoiceHeader.Size, plaintext.Length),
            packet.AsSpan(packet.Length - VoiceHeader.TagSize),
            packet.AsSpan(0, VoiceHeader.Size));
        return packet;
    }

    public bool TryOpen(Direction direction, ReadOnlySpan<byte> packet, out VoiceHeader header, out byte[] plaintext)
    {
        plaintext = [];
        if (!VoiceHeader.TryRead(packet, out header)) return false;
        var body = packet[VoiceHeader.Size..^VoiceHeader.TagSize];
        var result = new byte[body.Length];
        Span<byte> nonce = stackalloc byte[12];
        Nonce(direction, header.Seq, nonce);
        try
        {
            aes.Decrypt(nonce, body, packet[^VoiceHeader.TagSize..], result, packet[..VoiceHeader.Size]);
        }
        catch (AuthenticationTagMismatchException)
        {
            return false;
        }
        plaintext = result;
        return true;
    }

    static void Nonce(Direction direction, uint seq, Span<byte> nonce)
    {
        nonce.Clear();
        nonce[0] = (byte)direction;
        BinaryPrimitives.WriteUInt32BigEndian(nonce[8..], seq);
    }

    public void Dispose() => aes.Dispose();
}

/// <summary>Per-direction sequence numbers. They are GCM nonces, so they must never wrap.</summary>
public sealed class SeqCounter
{
    long next;

    public uint Next()
    {
        long value = Interlocked.Increment(ref next) - 1;
        if (value > uint.MaxValue) throw new OverflowException("Voice sequence exhausted, reconnect required");
        return (uint)value;
    }

    public void SetNextForTest(uint value) => next = value;
}

/// <summary>Sliding 64-packet anti-replay window.</summary>
public sealed class ReplayWindow
{
    bool any;
    uint highest;
    ulong seen;

    public bool Accept(uint seq)
    {
        if (!any)
        {
            any = true;
            highest = seq;
            seen = 1;
            return true;
        }
        if (seq > highest)
        {
            uint shift = seq - highest;
            seen = shift >= 64 ? 1 : (seen << (int)shift) | 1;
            highest = seq;
            return true;
        }
        uint age = highest - seq;
        if (age >= 64) return false;
        ulong bit = 1UL << (int)age;
        if ((seen & bit) != 0) return false;
        seen |= bit;
        return true;
    }
}

/// <summary>Server to client voice plaintext: [speakerSeq 4 BE][opus].</summary>
public static class RelayPayload
{
    public static byte[] Build(uint speakerSeq, ReadOnlySpan<byte> opus)
    {
        var payload = new byte[4 + opus.Length];
        BinaryPrimitives.WriteUInt32BigEndian(payload, speakerSeq);
        opus.CopyTo(payload.AsSpan(4));
        return payload;
    }

    public static bool TryParse(byte[] payload, out uint speakerSeq, out byte[] opus)
    {
        speakerSeq = 0;
        opus = [];
        if (payload.Length < 4) return false;
        speakerSeq = BinaryPrimitives.ReadUInt32BigEndian(payload);
        opus = payload[4..];
        return true;
    }
}
