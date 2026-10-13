using OVS.Shared.Protocol;

namespace OVS.Shared.IO;

/// <summary>File helpers the client and the server share: a delete that never throws, an atomic write and a chunk read.</summary>
public static class SafeFile
{
    /// <summary>Deletes the file; a file in use or without access stays and nothing is thrown.</summary>
    public static void DeleteQuietly(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Writes to path.tmp and then moves it over path, so a reader sees the old or the new file, never half of one.</summary>
    /// <param name="flushToDisk">True writes through to the disk before the move; each caller keeps what it did before.</param>
    public static void WriteAtomic(string path, byte[] content, bool flushToDisk)
    {
        if (flushToDisk)
        {
            WriteAtomic(path, stream => stream.Write(content), flushToDisk: true);
            return;
        }
        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, content);
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>Writes to path.tmp through write and then moves it over path.</summary>
    /// <param name="flushToDisk">True writes through to the disk before the move.</param>
    public static void WriteAtomic(string path, Action<Stream> write, bool flushToDisk)
    {
        var tmp = path + ".tmp";
        using (var stream = File.Create(tmp))
        {
            write(stream);
            if (flushToDisk) stream.Flush(flushToDisk: true);
        }
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>At most BackupChunkBytes from offset; null when offset is outside the file.</summary>
    public static FileChunk? ReadChunk(string path, long offset)
    {
        using var file = File.OpenRead(path);
        if (offset < 0 || offset > file.Length) return null;
        file.Position = offset;
        var bytes = new byte[Math.Min(ProtocolLimits.BackupChunkBytes, file.Length - offset)];
        file.ReadExactly(bytes);
        return new FileChunk(bytes, file.Length);
    }
}
