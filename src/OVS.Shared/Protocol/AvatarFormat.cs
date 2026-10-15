using System.Buffers.Binary;
using System.IO.Compression;

namespace OVS.Shared.Protocol;

/// <summary>
/// Package 168 (A170): a profile picture as stored and sent, a square PNG of 128 px and at most 64 KB. The server has no
/// image library (A21) and never draws the pixels, so <see cref="Validate"/> proves the file is a well-formed PNG instead:
/// the signature, every chunk with its CRC, only IHDR, PLTE, tRNS, IDAT and IEND (no text or other metadata), nothing
/// after IEND, a non-interlaced header of a known colour type and bit depth, and the joined IDAT data inflating to exactly
/// the size the header promises (one filter byte per row plus the row's bytes). The inflating stops one byte past that
/// size, so a deflate bomb costs no more than a valid picture.
/// </summary>
public static class AvatarFormat
{
    /// <summary>The side length in pixels.</summary>
    public const int Size = 128;

    /// <summary>The largest picture in bytes.</summary>
    public const int MaxBytes = 64 * 1024;

    static ReadOnlySpan<byte> Signature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    static readonly uint[] CrcTable = BuildCrcTable();

    /// <returns>Null when the picture is valid, otherwise the reason in words.</returns>
    public static string? Validate(ReadOnlySpan<byte> png)
    {
        if (png.Length > MaxBytes) return $"The picture is too large (at most {MaxBytes / 1024} KB).";
        if (png.Length < Signature.Length || !png[..Signature.Length].SequenceEqual(Signature)) return "The picture is not a PNG.";
        var rest = png[Signature.Length..];
        int expected = -1;
        bool palette = false, seenPalette = false, ended = false;
        using var data = new MemoryStream();
        while (!rest.IsEmpty)
        {
            if (ended) return "The picture has bytes after its end.";
            if (rest.Length < 12) return "The picture is cut off.";
            int length = BinaryPrimitives.ReadInt32BigEndian(rest);
            if (length < 0 || length > rest.Length - 12) return "The picture is cut off.";
            var type = rest.Slice(4, 4);
            var body = rest.Slice(8, length);
            if (Crc(rest.Slice(4, 4 + length)) != BinaryPrimitives.ReadUInt32BigEndian(rest.Slice(8 + length, 4))) return "A part of the picture is damaged.";
            bool first = expected < 0;
            if (type.SequenceEqual("IHDR"u8))
            {
                if (!first) return "The picture has two headers.";
                if (Header(body) is not { } promised) return "The picture's header is not supported.";
                (expected, palette) = (promised.Expected, promised.Palette);
            }
            else if (first) return "The picture does not start with its header.";
            else if (type.SequenceEqual("PLTE"u8)) seenPalette = true;
            else if (type.SequenceEqual("IDAT"u8)) data.Write(body);
            else if (type.SequenceEqual("IEND"u8))
            {
                if (length != 0) return "The picture's end is damaged.";
                ended = true;
            }
            else if (!type.SequenceEqual("tRNS"u8)) return "The picture holds more than pixels (only IHDR, PLTE, tRNS, IDAT and IEND).";
            rest = rest[(12 + length)..];
        }
        if (!ended) return "The picture is cut off.";
        if (palette && !seenPalette) return "The picture's colours are missing.";
        return Inflated(data, expected + 1) == expected ? null : "The picture's pixels do not match its size.";
    }

    /// <summary>
    /// The picture as the client sends it: <paramref name="rgba"/> (128 x 128 pixels, 4 bytes each, not premultiplied) as a
    /// PNG of IHDR, one IDAT and IEND, nothing else, so it always passes <see cref="Validate"/>.
    /// </summary>
    /// <exception cref="ArgumentException">The pixels are not 128 x 128 x 4 bytes.</exception>
    public static byte[] Encode(ReadOnlySpan<byte> rgba)
    {
        const int Row = Size * 4;
        if (rgba.Length != Size * Row) throw new ArgumentException($"{Size} x {Size} RGBA pixels expected.", nameof(rgba));
        var raw = new byte[Size * (1 + Row)];
        for (int row = 0; row < Size; row++) rgba.Slice(row * Row, Row).CopyTo(raw.AsSpan((row * (1 + Row)) + 1)); // filter 0 per row
        using var deflated = new MemoryStream();
        using (var zlib = new ZLibStream(deflated, CompressionLevel.SmallestSize, leaveOpen: true)) zlib.Write(raw);
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, Size);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), Size);
        header[8] = 8; // bits per channel
        header[9] = 6; // RGBA
        using var png = new MemoryStream();
        png.Write(Signature);
        WriteChunk(png, "IHDR"u8, header);
        WriteChunk(png, "IDAT"u8, deflated.ToArray());
        WriteChunk(png, "IEND"u8, []);
        return png.ToArray();
    }

    static void WriteChunk(MemoryStream png, ReadOnlySpan<byte> type, ReadOnlySpan<byte> body)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, body.Length);
        png.Write(number);
        var typed = new byte[4 + body.Length];
        type.CopyTo(typed);
        body.CopyTo(typed.AsSpan(4));
        png.Write(typed);
        BinaryPrimitives.WriteUInt32BigEndian(number, Crc(typed));
        png.Write(number);
    }

    /// <summary>The raw size the header promises and whether it needs a palette; null for a header that is not supported.</summary>
    static Promise? Header(ReadOnlySpan<byte> body)
    {
        if (body.Length != 13) return null;
        int width = BinaryPrimitives.ReadInt32BigEndian(body), height = BinaryPrimitives.ReadInt32BigEndian(body[4..]);
        byte depth = body[8], colorType = body[9];
        if (width != Size || height != Size || body[10] != 0 || body[11] != 0 || body[12] != 0) return null; // deflate, adaptive filter, no interlace
        int? channels = (colorType, depth) switch
        {
            (0, 1 or 2 or 4 or 8 or 16) => 1,
            (2, 8 or 16) => 3,
            (3, 1 or 2 or 4 or 8) => 1,
            (4, 8 or 16) => 2,
            (6, 8 or 16) => 4,
            _ => null,
        };
        if (channels is not { } count) return null;
        int rowBytes = ((width * count * depth) + 7) / 8;
        return new Promise(height * (1 + rowBytes), colorType == 3);
    }

    /// <summary>How many bytes the zlib data inflates to, read at most up to <paramref name="limit"/>.</summary>
    static int Inflated(MemoryStream data, int limit)
    {
        data.Position = 0;
        try
        {
            using var inflater = new ZLibStream(data, CompressionMode.Decompress);
            var buffer = new byte[4096];
            int total = 0, read;
            while (total < limit && (read = inflater.Read(buffer, 0, Math.Min(buffer.Length, limit - total))) > 0) total += read;
            return total;
        }
        catch (InvalidDataException)
        {
            return -1; // no valid zlib data
        }
    }

    static uint Crc(ReadOnlySpan<byte> bytes)
    {
        uint crc = 0xFFFFFFFF;
        foreach (var value in bytes) crc = CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFF;
    }

    static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint index = 0; index < 256; index++)
        {
            uint value = index;
            for (int bit = 0; bit < 8; bit++) value = (value & 1) != 0 ? 0xEDB88320 ^ (value >> 1) : value >> 1;
            table[index] = value;
        }
        return table;
    }

    /// <summary>What a header promises: the raw size of the filtered rows and whether a palette must come.</summary>
    sealed record Promise(int Expected, bool Palette);
}
