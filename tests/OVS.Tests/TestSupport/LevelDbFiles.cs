using System.Text;

namespace OVS.Tests.TestSupport;

/// <summary>Package 171: writes small LevelDB files (a log, a table) like Chromium's local storage, so no real app data is needed.</summary>
static class LevelDbFiles
{
    /// <summary>A Chromium local storage key: origin, a zero, a one, the name.</summary>
    public static string Key(string origin, string name) => $"_{origin}\0\u0001{name}";

    /// <summary>A local storage value as Chromium writes Latin-1 text: a one, then the text.</summary>
    public static byte[] Text(string text) => [1, .. Encoding.Latin1.GetBytes(text)];

    /// <summary>A log of one batch per entry; a null value deletes the key.</summary>
    public static void WriteLog(string path, IReadOnlyList<LevelDbEntry> entries)
    {
        using var file = new MemoryStream();
        foreach (var entry in entries)
        {
            using var batch = new MemoryStream();
            batch.Write(BitConverter.GetBytes(entry.Sequence));
            batch.Write(BitConverter.GetBytes(1u));
            batch.WriteByte(entry.Value is null ? (byte)0 : (byte)1);
            WriteBytes(batch, Encoding.Latin1.GetBytes(entry.Key));
            if (entry.Value is not null) WriteBytes(batch, entry.Value);
            var payload = batch.ToArray();
            file.Write(new byte[4]); // the checksum, which the reader does not check
            file.Write(BitConverter.GetBytes((ushort)payload.Length));
            file.WriteByte(1); // a whole record
            file.Write(payload);
        }
        File.WriteAllBytes(path, file.ToArray());
    }

    /// <summary>A table of one data block; <paramref name="compression"/> is the trailer's type byte (1 = Snappy).</summary>
    public static void WriteTable(string path, byte compression, IReadOnlyList<LevelDbEntry> entries)
    {
        using var file = new MemoryStream();
        var block = Block(entries.Select(entry => new BlockPair(InternalKey(entry), entry.Value ?? [])));
        var stored = compression == 1 ? Snappy(block) : block;
        file.Write(stored);
        file.WriteByte(compression);
        file.Write(new byte[4]);
        var index = Block([new BlockPair([0], Handle(0, stored.Length))]);
        long indexOffset = file.Position;
        file.Write(index);
        file.WriteByte(0);
        file.Write(new byte[4]);
        var footer = new byte[48];
        byte[] footerHandles = [.. Handle(0, 0), .. Handle(indexOffset, index.Length)];
        footerHandles.CopyTo(footer, 0);
        file.Write(footer);
        File.WriteAllBytes(path, file.ToArray());
    }

    /// <summary>A raw Snappy block of one literal.</summary>
    public static byte[] Snappy(byte[] data)
    {
        using var output = new MemoryStream();
        WriteVarint(output, (ulong)data.Length);
        output.WriteByte(61 << 2); // a literal whose length follows in two bytes
        output.Write(BitConverter.GetBytes((ushort)(data.Length - 1)));
        output.Write(data);
        return output.ToArray();
    }

    static byte[] InternalKey(LevelDbEntry entry) =>
        [.. Encoding.Latin1.GetBytes(entry.Key), .. BitConverter.GetBytes(entry.Sequence << 8 | (entry.Value is null ? 0ul : 1ul))];

    static byte[] Block(IEnumerable<BlockPair> pairs)
    {
        using var block = new MemoryStream();
        foreach (var pair in pairs)
        {
            WriteVarint(block, 0);
            WriteVarint(block, (ulong)pair.Key.Length);
            WriteVarint(block, (ulong)pair.Value.Length);
            block.Write(pair.Key);
            block.Write(pair.Value);
        }
        block.Write(BitConverter.GetBytes(0u)); // one restart point at the start
        block.Write(BitConverter.GetBytes(1u));
        return block.ToArray();
    }

    static byte[] Handle(long offset, int size)
    {
        using var handle = new MemoryStream();
        WriteVarint(handle, (ulong)offset);
        WriteVarint(handle, (ulong)size);
        return handle.ToArray();
    }

    static void WriteBytes(Stream stream, byte[] bytes)
    {
        WriteVarint(stream, (ulong)bytes.Length);
        stream.Write(bytes);
    }

    static void WriteVarint(Stream stream, ulong value)
    {
        while (value >= 0x80)
        {
            stream.WriteByte((byte)(value | 0x80));
            value >>= 7;
        }
        stream.WriteByte((byte)value);
    }

    /// <summary>A key and value of a block.</summary>
    sealed record BlockPair(byte[] Key, byte[] Value);
}
