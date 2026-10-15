using System.Text;

namespace OVS.Client.Settings;

/// <summary>
/// Package 171: the key and value pairs of a LevelDB folder (the local storage of an Electron app such as Discord),
/// read only: the <c>.log</c> and <c>.ldb</c> files are opened for reading with every share mode, so a running app is
/// not disturbed and nothing is written. The newest value of a key wins and a deleted key is gone. A damaged record,
/// block or file is skipped, never thrown; what was read before it stays. Table blocks may be Snappy compressed, which
/// <see cref="Unsnappy"/> decodes.
/// </summary>
internal static class LevelDbReader
{
    const int LogBlockSize = 32768;
    const int LogHeaderSize = 7;
    const int TableFooterSize = 48;
    const int BlockTrailerSize = 5;
    const byte TypeValue = 1;
    const int MaxBlockBytes = 64 * 1024 * 1024; // far above LevelDB's 4 KB blocks: a damaged length never allocates more

    /// <summary>Every key that has a value, as Latin-1 text (keys of the local storage are ASCII), with its newest value.</summary>
    /// <exception cref="IOException">The folder or a file cannot be read.</exception>
    /// <exception cref="UnauthorizedAccessException">The folder or a file cannot be read.</exception>
    public static IReadOnlyDictionary<string, byte[]> Read(string folder)
    {
        var found = new Dictionary<string, Entry>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(folder).Where(IsDataFile).Order(StringComparer.Ordinal))
        {
            var data = ReadShared(path);
            if (path.EndsWith(".log", StringComparison.OrdinalIgnoreCase)) ReadLog(data, found);
            else ReadTable(data, found);
        }
        return found.Where(entry => entry.Value.Value is not null).ToDictionary(entry => entry.Key, entry => entry.Value.Value!, StringComparer.Ordinal);
    }

    static bool IsDataFile(string path) => path.EndsWith(".log", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".ldb", StringComparison.OrdinalIgnoreCase);

    /// <summary>The whole file, opened so the app that owns it can go on writing, renaming and deleting.</summary>
    static byte[] ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    static void Put(Dictionary<string, Entry> found, ReadOnlySpan<byte> key, ulong sequence, byte[]? value)
    {
        var text = Encoding.Latin1.GetString(key);
        if (!found.TryGetValue(text, out var known) || known.Sequence <= sequence) found[text] = new Entry(sequence, value);
    }

    // ---- The write-ahead log: 32 KB blocks of records, each a whole or a fragment of a write batch ----

    static void ReadLog(byte[] data, Dictionary<string, Entry> found)
    {
        int position = 0;
        var pending = new List<byte>();
        while (position + LogHeaderSize <= data.Length)
        {
            int left = LogBlockSize - position % LogBlockSize;
            if (left < LogHeaderSize)
            {
                position += left; // the block's tail is padding
                continue;
            }
            int length = data[position + 4] | data[position + 5] << 8;
            byte type = data[position + 6];
            int start = position + LogHeaderSize;
            position = start + length;
            if (position > data.Length) return; // cut off: the app is still writing it
            var fragment = data.AsSpan(start, length);
            switch (type)
            {
                case 1: // a whole batch
                    ReadBatch(fragment, found);
                    break;
                case 2: // the first fragment
                    pending.Clear();
                    pending.AddRange(fragment);
                    break;
                case 3: // a middle one
                    pending.AddRange(fragment);
                    break;
                case 4: // the last one
                    pending.AddRange(fragment);
                    ReadBatch([.. pending], found);
                    pending.Clear();
                    break;
            }
        }
    }

    static void ReadBatch(ReadOnlySpan<byte> batch, Dictionary<string, Entry> found)
    {
        try
        {
            ulong sequence = BitConverter.ToUInt64(batch[..8]);
            uint count = BitConverter.ToUInt32(batch[8..12]);
            int position = 12;
            for (uint index = 0; index < count; index++)
            {
                byte tag = batch[position++];
                int keyLength = checked((int)ReadVarint(batch, ref position));
                var key = batch.Slice(position, keyLength);
                position += keyLength;
                byte[]? value = null;
                if (tag == TypeValue)
                {
                    int length = checked((int)ReadVarint(batch, ref position));
                    value = batch.Slice(position, length).ToArray();
                    position += length;
                }
                Put(found, key, sequence + index, value);
            }
        }
        catch (Exception exception) when (IsDamage(exception))
        {
            // a damaged batch: its readable entries are kept
        }
    }

    // ---- A sorted table: data blocks, found through the index block named in the footer ----

    static void ReadTable(byte[] data, Dictionary<string, Entry> found)
    {
        byte[] index;
        try
        {
            var footer = data.AsSpan(data.Length - TableFooterSize);
            int position = 0;
            ReadVarint(footer, ref position); // the meta index, not needed
            ReadVarint(footer, ref position);
            ulong indexOffset = ReadVarint(footer, ref position);
            ulong indexSize = ReadVarint(footer, ref position);
            index = Block(data, indexOffset, indexSize);
        }
        catch (Exception exception) when (IsDamage(exception))
        {
            return; // no footer or index: nothing to find in this table
        }
        foreach (var handle in BlockEntries(index))
        {
            try
            {
                int position = 0;
                ulong offset = ReadVarint(handle.Value, ref position);
                ulong size = ReadVarint(handle.Value, ref position);
                foreach (var pair in BlockEntries(Block(data, offset, size)))
                {
                    if (pair.Key.Length < 8) continue;
                    ulong tail = BitConverter.ToUInt64(pair.Key, pair.Key.Length - 8);
                    Put(found, pair.Key.AsSpan(0, pair.Key.Length - 8), tail >> 8, (tail & 0xff) == TypeValue ? pair.Value : null);
                }
            }
            catch (Exception exception) when (IsDamage(exception))
            {
                // a damaged block is skipped, the others are read
            }
        }
    }

    /// <summary>A block's contents, decompressed when its trailer says Snappy.</summary>
    static byte[] Block(byte[] data, ulong offset, ulong size)
    {
        if (size > MaxBlockBytes || offset + size + BlockTrailerSize > (ulong)data.Length) throw new InvalidDataException("The block lies outside the file.");
        var contents = data.AsSpan((int)offset, (int)size);
        return data[(int)(offset + size)] switch
        {
            0 => contents.ToArray(),
            1 => Unsnappy(contents),
            _ => throw new InvalidDataException("Unknown block compression."),
        };
    }

    /// <summary>The key and value pairs of a block: each key shares a prefix with the one before.</summary>
    static List<Pair> BlockEntries(byte[] block)
    {
        int restarts = (int)BitConverter.ToUInt32(block, block.Length - 4);
        int end = checked(block.Length - 4 - 4 * restarts);
        if (end < 0) throw new InvalidDataException("Too many restart points.");
        var entries = new List<Pair>();
        byte[] last = [];
        int position = 0;
        while (position < end)
        {
            int shared = checked((int)ReadVarint(block, ref position));
            int unshared = checked((int)ReadVarint(block, ref position));
            int valueLength = checked((int)ReadVarint(block, ref position));
            var key = new byte[shared + unshared];
            last.AsSpan(0, shared).CopyTo(key);
            block.AsSpan(position, unshared).CopyTo(key.AsSpan(shared));
            position += unshared;
            entries.Add(new Pair(key, block.AsSpan(position, valueLength).ToArray()));
            position += valueLength;
            last = key;
        }
        return entries;
    }

    static ulong ReadVarint(ReadOnlySpan<byte> data, ref int position)
    {
        ulong result = 0;
        for (int shift = 0; shift < 64; shift += 7)
        {
            byte next = data[position++];
            result |= (ulong)(next & 0x7f) << shift;
            if (next < 0x80) return result;
        }
        throw new InvalidDataException("A varint longer than 64 bits.");
    }

    /// <summary>Decodes a raw Snappy block: its length, then literals and copies of what was written before.</summary>
    /// <exception cref="InvalidDataException">The block is damaged.</exception>
    internal static byte[] Unsnappy(ReadOnlySpan<byte> input)
    {
        int position = 0;
        ulong expected = ReadVarint(input, ref position);
        if (expected > MaxBlockBytes) throw new InvalidDataException("A Snappy block larger than any LevelDB block.");
        var output = new byte[expected];
        int written = 0;
        while (position < input.Length)
        {
            byte tag = input[position++];
            int length, offset;
            switch (tag & 3)
            {
                case 0: // a literal
                    length = tag >> 2;
                    if (length >= 60)
                    {
                        int bytes = length - 59;
                        length = 0;
                        for (int index = 0; index < bytes; index++) length |= input[position++] << (8 * index);
                    }
                    length++;
                    input.Slice(position, length).CopyTo(output.AsSpan(written));
                    position += length;
                    written += length;
                    continue;
                case 1:
                    length = ((tag >> 2) & 7) + 4;
                    offset = (tag >> 5) << 8 | input[position++];
                    break;
                case 2:
                    length = (tag >> 2) + 1;
                    offset = input[position] | input[position + 1] << 8;
                    position += 2;
                    break;
                default:
                    length = (tag >> 2) + 1;
                    offset = (int)BitConverter.ToUInt32(input.Slice(position, 4));
                    position += 4;
                    break;
            }
            if (offset <= 0 || offset > written || written + length > output.Length) throw new InvalidDataException("A Snappy copy outside what was written.");
            for (int index = 0; index < length; index++, written++) output[written] = output[written - offset]; // may overlap itself
        }
        if (written != output.Length) throw new InvalidDataException("A Snappy block shorter than it says.");
        return output;
    }

    static bool IsDamage(Exception exception) =>
        exception is IndexOutOfRangeException or ArgumentException or InvalidDataException or OverflowException;

    /// <summary>A key's newest state: its value, or null when deleted.</summary>
    sealed record Entry(ulong Sequence, byte[]? Value);

    /// <summary>One key and value of a block.</summary>
    sealed record Pair(byte[] Key, byte[] Value);
}
