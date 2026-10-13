using System.Buffers.Binary;

namespace OVS.Shared.Voice;

/// <summary>
/// Package 133 (A142): the plaintext of Hello and Ping in both directions: [time ms 8 BE][expected 4 BE][missed 4 BE]. The
/// client sends its clock and the loss it sees from the server (downlink); the server echoes the time and adds the loss it
/// sees from the client (uplink). An empty Hello or Ping is still answered, with an empty payload.
/// </summary>
public readonly record struct PingPayload(long TimeMs, LossReport Loss)
{
    /// <summary>The payload's length in bytes.</summary>
    public const int Size = 16;

    /// <summary>The payload's bytes.</summary>
    public byte[] Build()
    {
        var payload = new byte[Size];
        BinaryPrimitives.WriteInt64BigEndian(payload, TimeMs);
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(8), Loss.Expected);
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(12), Loss.Missed);
        return payload;
    }

    /// <summary>False when payload is not exactly Size bytes long.</summary>
    public static bool TryParse(ReadOnlySpan<byte> payload, out PingPayload ping)
    {
        ping = default;
        if (payload.Length != Size) return false;
        ping = new PingPayload(BinaryPrimitives.ReadInt64BigEndian(payload),
            new LossReport(BinaryPrimitives.ReadUInt32BigEndian(payload[8..]), BinaryPrimitives.ReadUInt32BigEndian(payload[12..])));
        return true;
    }
}
