using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace OVS.Tests.TestSupport;

/// <summary>Package 168: PNG files built chunk by chunk, so a test can make every kind of wrong picture on purpose.</summary>
internal static class TestPng
{
    /// <summary>A non-interlaced RGBA picture of the given size, filled with one colour.</summary>
    public static byte[] Rgba(int size = 128, byte[]? pixels = null, IEnumerable<byte[]>? extraChunks = null, byte[]? trailing = null)
    {
        pixels ??= Raw(size, 4);
        return File([Chunk("IHDR", Header(size, size, 8, 6)), .. extraChunks ?? [], Chunk("IDAT", Deflate(pixels)), Chunk("IEND", [])], trailing);
    }

    /// <summary>The filtered rows of a picture: a filter byte of 0 and the row's bytes, here all 0x80.</summary>
    public static byte[] Raw(int size, int bytesPerPixel)
    {
        var raw = new byte[size * (1 + (size * bytesPerPixel))];
        for (int row = 0; row < size; row++)
            Array.Fill(raw, (byte)0x80, (row * (1 + (size * bytesPerPixel))) + 1, size * bytesPerPixel);
        return raw;
    }

    /// <summary>The 13 bytes of an IHDR chunk.</summary>
    public static byte[] Header(int width, int height, byte depth, byte colorType)
    {
        var body = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(body, width);
        BinaryPrimitives.WriteInt32BigEndian(body.AsSpan(4), height);
        body[8] = depth;
        body[9] = colorType;
        return body;
    }

    /// <summary>One chunk with its length and CRC.</summary>
    public static byte[] Chunk(string type, byte[] body)
    {
        var chunk = new byte[12 + body.Length];
        BinaryPrimitives.WriteInt32BigEndian(chunk, body.Length);
        Encoding.ASCII.GetBytes(type, chunk.AsSpan(4));
        body.CopyTo(chunk, 8);
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(8 + body.Length), Crc(chunk.AsSpan(4, 4 + body.Length)));
        return chunk;
    }

    /// <summary>zlib data of <paramref name="raw"/>.</summary>
    public static byte[] Deflate(byte[] raw)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true)) zlib.Write(raw);
        return output.ToArray();
    }

    /// <summary>The signature, the chunks and what follows them.</summary>
    public static byte[] File(IEnumerable<byte[]> chunks, byte[]? trailing = null) =>
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. chunks.SelectMany(chunk => chunk), .. trailing ?? []];

    static uint Crc(ReadOnlySpan<byte> bytes)
    {
        uint crc = 0xFFFFFFFF;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1;
        }
        return crc ^ 0xFFFFFFFF;
    }
}
