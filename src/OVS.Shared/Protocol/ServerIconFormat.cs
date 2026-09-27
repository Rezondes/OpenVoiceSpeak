using System.Buffers.Binary;
using System.Security.Cryptography;

namespace OVS.Shared.Protocol;

/// <summary>
/// The server logo on the wire and on disk: a square PNG. The client scales any image down to 256 px before
/// uploading, so the server needs no image library: it only checks the PNG header (A21).
/// </summary>
public static class ServerIconFormat
{
    public const int MaxBytes = 512 * 1024; // stays well below the 1 MiB frame limit even as base64
    public const int MinSize = 64, MaxSize = 512;

    static ReadOnlySpan<byte> Signature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Identifies a logo; clients compare it with their cache before downloading.</summary>
    public static string Hash(ReadOnlySpan<byte> png) => Convert.ToHexStringLower(SHA256.HashData(png))[..32];

    /// <returns>Null when valid, otherwise the reason in words.</returns>
    public static string? Validate(ReadOnlySpan<byte> png)
    {
        if (png.Length > MaxBytes) return $"Das Logo ist zu gross (höchstens {MaxBytes / 1024} KB).";
        // signature, then the IHDR chunk: length (4), "IHDR" (4), width (4), height (4), big endian
        if (png.Length < 33 || !png[..8].SequenceEqual(Signature) || !png.Slice(12, 4).SequenceEqual("IHDR"u8))
            return "Das Logo ist kein PNG.";
        int width = BinaryPrimitives.ReadInt32BigEndian(png.Slice(16, 4));
        int height = BinaryPrimitives.ReadInt32BigEndian(png.Slice(20, 4));
        if (width != height) return "Das Logo muss quadratisch sein (1:1).";
        if (width is < MinSize or > MaxSize) return $"Das Logo muss zwischen {MinSize} und {MaxSize} Pixel gross sein.";
        return null;
    }
}
