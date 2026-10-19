using System.Buffers.Binary;
using System.Security.Cryptography;

namespace OVS.Tests.TestSupport;

/// <summary>Package M28: the code an authenticator app shows for a key (RFC 6238: HMAC-SHA1, 30 s, 6 digits).</summary>
internal static class Totp
{
    /// <summary>The code for <paramref name="base32Key"/> at <paramref name="at"/>.</summary>
    public static string Code(string base32Key, DateTimeOffset at)
    {
        var counter = new byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, at.ToUnixTimeSeconds() / 30);
#pragma warning disable CA5350 // RFC 6238 is HMAC-SHA1, as every authenticator app
        var hash = HMACSHA1.HashData(Base32(base32Key), counter);
#pragma warning restore CA5350
        int offset = hash[^1] & 0x0F;
        int value = (BinaryPrimitives.ReadInt32BigEndian(hash.AsSpan(offset)) & 0x7FFFFFFF) % 1_000_000;
        return value.ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    static byte[] Base32(string text)
    {
        const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bytes = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var letter in text.TrimEnd('=').ToUpperInvariant())
        {
            buffer = (buffer << 5) | Alphabet.IndexOf(letter, StringComparison.Ordinal);
            bits += 5;
            if (bits < 8) continue;
            bytes.Add((byte)(buffer >> (bits - 8)));
            bits -= 8;
        }
        return [.. bytes];
    }
}
