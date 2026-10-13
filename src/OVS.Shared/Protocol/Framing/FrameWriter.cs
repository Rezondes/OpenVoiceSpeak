using System.Buffers.Binary;
using System.Text.Json;
using OVS.Shared.Exceptions;
using OVS.Shared.Protocol.Messages;

namespace OVS.Shared.Protocol.Framing;

/// <summary>Writes frames. Safe for concurrent callers.</summary>
#pragma warning disable CA1001 // its gate hands out no wait handle, so nothing needs freeing
public sealed class FrameWriter(Stream stream)
#pragma warning restore CA1001
{
    readonly SemaphoreSlim gate = new(1, 1);

    /// <summary>Writes the message as one frame.</summary>
    public Task WriteAsync(Message message, CancellationToken cancellation = default) => WriteFrameAsync(Encode(message), cancellation);

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
    public async Task WriteFrameAsync(byte[] frame, CancellationToken cancellation = default)
    {
        await gate.WaitAsync(cancellation);
        try
        {
            await stream.WriteAsync(frame, cancellation);
            await stream.FlushAsync(cancellation);
        }
        finally
        {
            gate.Release();
        }
    }
}
