using System.Buffers.Binary;
using System.Security.Cryptography;

namespace OVS.Shared.Voice;

/// <summary>AES-GCM with the session's voice key. Not thread-safe; use from one thread.</summary>
public sealed class VoiceCrypto(byte[] key) : IDisposable
{
    readonly AesGcm aes = new(key, VoiceHeader.TagSize);

    public byte[] Seal(PacketDirection direction, VoiceHeader header, ReadOnlySpan<byte> plaintext)
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

    public bool TryOpen(PacketDirection direction, ReadOnlySpan<byte> packet, out VoiceHeader header, out byte[] plaintext)
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

    static void Nonce(PacketDirection direction, uint seq, Span<byte> nonce)
    {
        nonce.Clear();
        nonce[0] = (byte)direction;
        BinaryPrimitives.WriteUInt32BigEndian(nonce[8..], seq);
    }

    public void Dispose() => aes.Dispose();
}
