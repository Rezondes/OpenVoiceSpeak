using System.Buffers.Binary;

namespace OVS.Shared.Voice;

/// <summary>
/// Voice plaintext in both directions: [frameSeq 4 BE][length of current 2 BE][current opus][previous opus, optional].
/// Package 136 (A145): the previous frame rides along on lossy legs, so a lost frame can be played in full from the next
/// packet. The server takes the parts apart with <see cref="TryReadRanges"/> without copying.
/// </summary>
public static class RelayPayload
{
    public const int HeaderSize = 6;
    /// <summary>The largest plaintext a voice packet can carry; current frame and copy share it.</summary>
    public const int MaxSize = VoiceHeader.MaxPacketSize - VoiceHeader.Size - VoiceHeader.TagSize;
    /// <summary>Package 136 (A145): from this loss on a leg (sender's uplink, a listener's downlink) the copy rides along.</summary>
    public const double CopyFromLossPercent = 2;
    /// <summary>
    /// Package 136 (review): a frame sent or relayed longer ago than this is no copy for the next one. The frame numbers go
    /// on across talk pauses, so the frame before the first of a spurt is the end of the last one. Five frames, so a frame
    /// lost on the way still leaves room for jitter between the two around it.
    /// </summary>
    public static readonly TimeSpan CopyMaxAge = TimeSpan.FromMilliseconds(100);

    /// <summary>A copy that would not fit the packet beside the current frame is left out.</summary>
    public static byte[] Build(uint frameSeq, ReadOnlySpan<byte> current, ReadOnlySpan<byte> copy = default)
    {
        if (current.Length is 0 or > VoiceHeader.MaxOpusSize) throw new ArgumentException("Opus frame empty or too large");
        if (copy.Length > VoiceHeader.MaxOpusSize || HeaderSize + current.Length + copy.Length > MaxSize) copy = default;
        var payload = new byte[HeaderSize + current.Length + copy.Length];
        BinaryPrimitives.WriteUInt32BigEndian(payload, frameSeq);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(4), (ushort)current.Length);
        current.CopyTo(payload.AsSpan(HeaderSize));
        copy.CopyTo(payload.AsSpan(HeaderSize + current.Length));
        return payload;
    }

    /// <summary>Strict bounds: the current frame 1 to MaxOpusSize bytes inside the payload, the copy (the rest, possibly
    /// empty) at most MaxOpusSize, the whole at most <see cref="MaxSize"/>.</summary>
    public static bool TryReadRanges(ReadOnlySpan<byte> payload, out uint frameSeq, out Range current, out Range copy)
    {
        frameSeq = 0;
        current = copy = default;
        if (payload.Length < HeaderSize || payload.Length > MaxSize) return false;
        int length = BinaryPrimitives.ReadUInt16BigEndian(payload[4..]);
        int rest = payload.Length - HeaderSize - length;
        if (length is 0 or > VoiceHeader.MaxOpusSize || rest < 0 || rest > VoiceHeader.MaxOpusSize) return false;
        frameSeq = BinaryPrimitives.ReadUInt32BigEndian(payload);
        current = HeaderSize..(HeaderSize + length);
        copy = (HeaderSize + length)..;
        return true;
    }

    /// <summary>The client's side: the frame, and the copy of the one before it or null.</summary>
    public static bool TryReadArrays(byte[] payload, out uint frameSeq, out byte[] opus, out byte[]? copy)
    {
        opus = [];
        copy = null;
        if (!TryReadRanges(payload, out frameSeq, out var current, out var rest)) return false;
        opus = payload[current];
        if (payload.Length > rest.Start.Value) copy = payload[rest];
        return true;
    }
}
