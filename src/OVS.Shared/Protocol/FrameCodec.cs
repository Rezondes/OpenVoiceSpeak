using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OVS.Shared.Protocol;

public class ProtocolException(string message) : Exception(message);

/// <summary>
/// Package 83: a frame that is a well-formed JSON object but no valid message (unknown type, wrong field type, unknown enum
/// name). The server answers it like a bad request and keeps the connection; everyone else treats it as a ProtocolException.
/// </summary>
public sealed class InvalidRequestException(string? requestId, string message) : ProtocolException(message)
{
    public string? RequestId { get; } = requestId;
}

/// <remarks>
/// Package 83: RespectNullableAnnotations stays off. It would turn every null in a non-nullable field into a JsonException,
/// in both directions and for the client as well; the server instead checks each request after deserializing (RequestShape)
/// and answers InvalidValue, so a bad field never costs the connection.
/// </remarks>
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

/// <summary>Writes frames. Safe for concurrent callers.</summary>
public sealed class FrameWriter(Stream stream)
{
    readonly SemaphoreSlim gate = new(1, 1);

    public Task WriteAsync(Message message, CancellationToken ct = default) => WriteFrameAsync(Encode(message), ct);

    /// <summary>Length header plus JSON; throws a ProtocolException above the frame limit.</summary>
    public static byte[] Encode(Message message)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(message, ProtocolJson.Options);
        if (payload.Length > FrameReader.MaxFrameSize)
            throw new ProtocolException($"Message too large ({payload.Length} bytes)");

        var frame = new byte[4 + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(frame, (uint)payload.Length);
        payload.CopyTo(frame, 4);
        return frame;
    }

    /// <summary>Writes a frame made by Encode.</summary>
    public async Task WriteFrameAsync(byte[] frame, CancellationToken ct = default)
    {
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
