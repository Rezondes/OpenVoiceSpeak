using System.Buffers.Binary;
using System.Text;
using OVS.Shared.Protocol;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Protocol;

public class FrameCodecTests
{
    static async Task<byte[]> Encode(params Message[] messages)
    {
        var ms = new MemoryStream();
        var writer = new FrameWriter(ms);
        foreach (var m in messages) await writer.WriteAsync(m);
        return ms.ToArray();
    }

    static byte[] RawFrame(string json)
    {
        var payload = Encoding.UTF8.GetBytes(json);
        var frame = new byte[4 + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(frame, (uint)payload.Length);
        payload.CopyTo(frame, 4);
        return frame;
    }

    [Fact]
    public async Task WriteThenRead_Ping_RoundTrips()
    {
        var reader = new FrameReader(new MemoryStream(await Encode(new Ping())));
        Assert.IsType<Ping>(await reader.ReadAsync());
    }

    [Fact]
    public async Task WriteThenRead_Error_KeepsFields()
    {
        var reader = new FrameReader(new MemoryStream(await Encode(new Error("r1", Codes.NotFound, "x"))));
        Assert.Equal(new Error("r1", Codes.NotFound, "x"), await reader.ReadAsync());
    }

    [Fact]
    public async Task Read_LengthAboveLimit_ThrowsWithoutAllocating()
    {
        var header = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(header, 0x00100001);
        var reader = new FrameReader(new MemoryStream(header));
        await Assert.ThrowsAsync<ProtocolException>(() => reader.ReadAsync());
    }

    [Fact]
    public async Task Read_TrickledBytes_AssemblesFrame()
    {
        var expected = new Error(null, Codes.InvalidName);
        var reader = new FrameReader(new TrickleStream(await Encode(expected)));
        Assert.Equal(expected, await reader.ReadAsync());
    }

    [Fact]
    public async Task Read_EndInsideFrame_ThrowsEndOfStream()
    {
        var bytes = await Encode(new Ping());
        var reader = new FrameReader(new MemoryStream(bytes[..^2]));
        await Assert.ThrowsAsync<EndOfStreamException>(() => reader.ReadAsync());
    }

    [Fact]
    public async Task Read_EndInsideHeader_ThrowsEndOfStream()
    {
        var reader = new FrameReader(new MemoryStream([0, 0]));
        await Assert.ThrowsAsync<EndOfStreamException>(() => reader.ReadAsync());
    }

    [Fact]
    public async Task Read_EndBetweenFrames_ReturnsNull()
    {
        var reader = new FrameReader(new MemoryStream(await Encode(new Ping())));
        await reader.ReadAsync();
        Assert.Null(await reader.ReadAsync());
    }

    [Theory]
    [InlineData("{\"type\":\"doesNotExist\"}")]
    [InlineData("{not json")]
    [InlineData("{}")]
    public async Task Read_UnknownTypeOrInvalidJson_Throws(string json)
    {
        var reader = new FrameReader(new MemoryStream(RawFrame(json)));
        await Assert.ThrowsAsync<ProtocolException>(() => reader.ReadAsync());
    }

    [Fact]
    public async Task Read_ZeroLength_Throws()
    {
        var reader = new FrameReader(new MemoryStream(new byte[4]));
        await Assert.ThrowsAsync<ProtocolException>(() => reader.ReadAsync());
    }

    [Fact]
    public async Task Write_OversizedMessage_ThrowsAndWritesNothing()
    {
        var ms = new MemoryStream();
        var writer = new FrameWriter(ms);
        await Assert.ThrowsAsync<ProtocolException>(() => writer.WriteAsync(new Error(null, "x", new string('a', 1_100_000))));
        Assert.Equal(0, ms.Length);
    }

    [Fact]
    public async Task Write_Concurrent100_AllFramesIntact()
    {
        var ms = new MemoryStream();
        var writer = new FrameWriter(ms);
        await Task.WhenAll(Enumerable.Range(0, 100).Select(i => Task.Run(() => writer.WriteAsync(new Error(i.ToString(), "c")))));

        ms.Position = 0;
        var reader = new FrameReader(ms);
        var ids = new HashSet<string?>();
        while (await reader.ReadAsync() is Error e) ids.Add(e.RequestId);
        Assert.Equal(100, ids.Count);
    }
}
