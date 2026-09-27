using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OVS.Shared.Protocol;

public sealed class ProtocolException(string message) : Exception(message);

public static class ProtocolJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
}

/// <summary>Reads frames: 4 byte big-endian length, then UTF-8 JSON.</summary>
public sealed class FrameReader(Stream stream)
{
    // Snapshots, user and ban lists travel in one frame; 1 MiB caps memory per connection.
    public const int MaxFrameSize = 1024 * 1024;
    readonly byte[] header = new byte[4];

    /// <returns>The next message, or null when the stream ended cleanly between frames.</returns>
    public async Task<Message?> ReadAsync(CancellationToken ct = default)
    {
        int read = 0;
        while (read < header.Length)
        {
            int n = await stream.ReadAsync(header.AsMemory(read), ct);
            if (n == 0)
            {
                if (read == 0) return null;
                throw new EndOfStreamException();
            }
            read += n;
        }

        uint length = BinaryPrimitives.ReadUInt32BigEndian(header);
        if (length == 0 || length > MaxFrameSize)
            throw new ProtocolException($"Invalid frame length {length}");

        var payload = new byte[length];
        await stream.ReadExactlyAsync(payload, ct);
        try
        {
            return JsonSerializer.Deserialize<Message>(payload, ProtocolJson.Options)
                ?? throw new ProtocolException("Empty message");
        }
        catch (Exception e) when (e is JsonException or NotSupportedException)
        {
            throw new ProtocolException("Invalid message: " + e.Message);
        }
    }
}

/// <summary>Writes frames. Safe for concurrent callers.</summary>
public sealed class FrameWriter(Stream stream)
{
    readonly SemaphoreSlim gate = new(1, 1);

    public async Task WriteAsync(Message message, CancellationToken ct = default)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(message, ProtocolJson.Options);
        if (payload.Length > FrameReader.MaxFrameSize)
            throw new ProtocolException($"Message too large ({payload.Length} bytes)");

        var frame = new byte[4 + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(frame, (uint)payload.Length);
        payload.CopyTo(frame, 4);

        await gate.WaitAsync(ct);
        try
        {
            await stream.WriteAsync(frame, ct);
            await stream.FlushAsync(ct);
        }
        finally
        {
            gate.Release();
        }
    }
}
