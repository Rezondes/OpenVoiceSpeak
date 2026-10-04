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

/// <summary>Package 133 (A142): frames expected and missed in a window of the frame numbers.</summary>
public readonly record struct LossReport(uint Expected, uint Missed)
{
    /// <summary>0 when nothing was expected: nobody talked, which is no loss.</summary>
    public double Percent => Expected == 0 ? 0 : 100.0 * Missed / Expected;
}

/// <summary>
/// Package 133 (A142): the plaintext of Hello and Ping in both directions: [time ms 8 BE][expected 4 BE][missed 4 BE]. The
/// client sends its clock and the loss it sees from the server (downlink); the server echoes the time and adds the loss it
/// sees from the client (uplink). An empty Hello or Ping is still answered, with an empty payload.
/// </summary>
public readonly record struct PingPayload(long TimeMs, LossReport Loss)
{
    public const int Size = 16;

    public byte[] Build()
    {
        var payload = new byte[Size];
        BinaryPrimitives.WriteInt64BigEndian(payload, TimeMs);
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(8), Loss.Expected);
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(12), Loss.Missed);
        return payload;
    }

    public static bool TryParse(ReadOnlySpan<byte> payload, out PingPayload ping)
    {
        ping = default;
        if (payload.Length != Size) return false;
        ping = new PingPayload(BinaryPrimitives.ReadInt64BigEndian(payload),
            new LossReport(BinaryPrimitives.ReadUInt32BigEndian(payload[8..]), BinaryPrimitives.ReadUInt32BigEndian(payload[12..])));
        return true;
    }
}

/// <summary>
/// Package 133 (A142): counts the frames of one or more voice streams (one per speaker) and the gaps in their frame
/// numbers, per 5 s window. A sender's frame number only advances while it talks, so a talk pause is no gap. Not
/// thread-safe; nothing is allocated per frame once a stream is known.
/// </summary>
public sealed class LossWindow(TimeProvider time)
{
    public static readonly TimeSpan Length = TimeSpan.FromSeconds(5);

    /// <summary>
    /// A stream silent this long, or jumping ahead by more than <see cref="MaxGap"/> frames, starts afresh without counting
    /// the gap: the server only relays what routing allows (not while deafened, other channels only by link), so the
    /// frame numbers of what reaches a listener skip without anything being lost. ponytail: a real outage longer than
    /// that goes uncounted.
    /// </summary>
    public static readonly TimeSpan FreshAfter = TimeSpan.FromSeconds(1);
    public const uint MaxGap = 50;

    sealed class Stream
    {
        public uint Highest;
        public ulong Seen; // bit i: frame Highest - i arrived, or lies before the stream's start or this window
        public long LastArrival;
    }

    readonly Dictionary<uint, Stream> streams = [];
    long start = time.GetTimestamp();
    long received, missed;
    LossReport? last;

    public void Count(uint id, uint frame)
    {
        Roll();
        long now = time.GetTimestamp();
        if (!streams.TryGetValue(id, out var s)) streams[id] = s = new Stream();
        else if (time.GetElapsedTime(s.LastArrival, now) <= FreshAfter)
        {
            s.LastArrival = now;
            if (frame > s.Highest && frame - s.Highest <= MaxGap)
            {
                uint gap = frame - s.Highest;
                missed += gap - 1;
                s.Seen = (s.Seen << (int)gap) | 1; // the gap's bits are 0: missing, counted in this window
                s.Highest = frame;
                received++;
                return;
            }
            uint age = s.Highest - frame;
            if (frame <= s.Highest && age < 64)
            {
                ulong bit = 1UL << (int)age;
                if ((s.Seen & bit) != 0) return; // a duplicate, or older than what this window counted
                s.Seen |= bit;
                missed--; // late: its gap was counted in this window, so missed is at least 1
                received++;
                return;
            }
            if (frame <= s.Highest) return; // far too old to place
        }
        // first frame, after a silence, or a jump beyond MaxGap: a fresh start
        s.Highest = frame;
        s.Seen = ulong.MaxValue;
        s.LastArrival = now;
        received++;
    }

    /// <summary>The last complete window, or the current one until the first is complete.</summary>
    public LossReport Report()
    {
        Roll();
        return last ?? Current();
    }

    LossReport Current()
    {
        uint expected = (uint)Math.Min(uint.MaxValue, received + missed);
        return new LossReport(expected, (uint)Math.Min(expected, missed));
    }

    void Roll()
    {
        long now = time.GetTimestamp();
        if (time.GetElapsedTime(start, now) < Length) return;
        last = Current();
        received = missed = 0;
        start = now;
        foreach (var s in streams.Values) s.Seen = ulong.MaxValue; // a late frame does not take back a gap of the last window
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
