using System.Buffers.Binary;
using System.Text.Json;
using OVS.Shared.Exceptions;
using OVS.Shared.Protocol.Messages;

namespace OVS.Shared.Protocol.Framing;

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
            throw Invalid(payload, "Invalid message: " + e.Message);
        }
    }

    /// <summary>Package 83: an InvalidRequestException when the payload is at least a JSON object, else a plain ProtocolException.</summary>
    static ProtocolException Invalid(byte[] payload, string message)
    {
        try
        {
            using var json = JsonDocument.Parse(payload);
            if (json.RootElement.ValueKind != JsonValueKind.Object) return new ProtocolException(message);
            var id = json.RootElement.TryGetProperty("requestId", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() : null;
            return new InvalidRequestException(id, message);
        }
        catch (JsonException)
        {
            return new ProtocolException(message);
        }
    }
}
