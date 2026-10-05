using System.Buffers.Binary;
using System.Text.Json;
using OVS.Shared.Exceptions;

namespace OVS.Shared.Protocol.Framing;

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
