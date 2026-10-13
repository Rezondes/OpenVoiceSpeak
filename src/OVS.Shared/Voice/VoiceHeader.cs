using System.Buffers.Binary;

namespace OVS.Shared.Voice;

/// <summary>
/// UDP voice packet: [type 1][sessionId 4 BE][seq 4 BE][target 1][ciphertext][GCM tag 16].
/// The 10 header bytes are authenticated as associated data.
/// </summary>
public readonly record struct VoiceHeader(PacketType Type, uint SessionId, uint Seq, byte Target)
{
    /// <summary>The header's length in bytes.</summary>
    public const int Size = 10;
    /// <summary>The GCM tag's length in bytes.</summary>
    public const int TagSize = 16;
    /// <summary>The largest UDP packet sent, below a common MTU.</summary>
    public const int MaxPacketSize = 1400;
    /// <summary>The largest Opus frame (RFC 6716).</summary>
    public const int MaxOpusSize = 1275;
    /// <summary>Target: the own channel.</summary>
    public const byte TargetChannel = 0;
    /// <summary>Target: the own channel and its linked channels.</summary>
    public const byte TargetLinked = 1;

    /// <summary>False for a packet shorter than the header.</summary>
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

    /// <summary>Writes the header's bytes into dest.</summary>
    public void Write(Span<byte> dest)
    {
        dest[0] = (byte)Type;
        BinaryPrimitives.WriteUInt32BigEndian(dest[1..], SessionId);
        BinaryPrimitives.WriteUInt32BigEndian(dest[5..], Seq);
        dest[9] = Target;
    }
}
