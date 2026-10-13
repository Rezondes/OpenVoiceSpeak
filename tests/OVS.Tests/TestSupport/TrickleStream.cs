namespace OVS.Tests.TestSupport;

/// <summary>Returns at most one byte per read, like a very slow network.</summary>
internal sealed class TrickleStream(byte[] data) : MemoryStream(data)
{
    public override int Read(byte[] buffer, int offset, int count) => base.Read(buffer, offset, Math.Min(count, 1));

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellation = default) =>
        base.ReadAsync(buffer[..Math.Min(buffer.Length, 1)], cancellation);

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellation) =>
        base.ReadAsync(buffer, offset, Math.Min(count, 1), cancellation);
}
