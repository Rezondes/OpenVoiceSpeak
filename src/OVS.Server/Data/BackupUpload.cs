using OVS.Shared.Protocol;

namespace OVS.Server.Data;

/// <summary>Package 75: one session's backup upload in progress, its hidden file and the bytes stored; guarded by the ServerState lock.</summary>
internal sealed class BackupUpload(string id, string path)
{
    /// <summary>The id the client chose for the upload.</summary>
    public string Id { get; } = id;
    /// <summary>The file the chunks are appended to.</summary>
    public string Path { get; } = path;
    /// <summary>The bytes stored so far.</summary>
    public long Received { get; private set; }

    /// <summary>A chunk must start where the bytes received end.</summary>
    public bool Continues(long offset) => offset == Received;

    /// <summary>The upload would grow past MaxBackupUploadBytes with length more bytes.</summary>
    public bool Overflows(int length) => Received + length > ProtocolLimits.MaxBackupUploadBytes;

    /// <summary>Writes the chunk (the first one replaces a stale file); counts it only once it is written.</summary>
    public void Append(ReadOnlySpan<byte> bytes)
    {
        using (var file = new FileStream(Path, Received == 0 ? FileMode.Create : FileMode.Append, FileAccess.Write))
            file.Write(bytes);
        Received += bytes.Length;
    }
}
