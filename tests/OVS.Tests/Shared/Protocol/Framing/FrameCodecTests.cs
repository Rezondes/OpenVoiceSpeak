using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using OVS.Shared.Exceptions;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Framing;
using OVS.Shared.Protocol.Messages;
using OVS.Shared.Protocol.Messages.Connection;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Shared.Protocol.Framing;

public class FrameCodecTests
{
    static async Task<byte[]> EncodeAsync(params Message[] messages)
    {
        var milliseconds = new MemoryStream();
        var writer = new FrameWriter(milliseconds);
        foreach (var message in messages) await writer.WriteAsync(message);
        return milliseconds.ToArray();
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
        var reader = new FrameReader(new MemoryStream(await EncodeAsync(new Ping())));
        Assert.IsType<Ping>(await reader.ReadAsync());
    }

    [Fact]
    public async Task WriteThenRead_Error_KeepsFields()
    {
        var reader = new FrameReader(new MemoryStream(await EncodeAsync(new Error("r1", ReasonCodes.NotFound, "x"))));
        Assert.Equal(new Error("r1", ReasonCodes.NotFound, "x"), await reader.ReadAsync());
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
        var expected = new Error(null, ReasonCodes.InvalidName);
        var reader = new FrameReader(new TrickleStream(await EncodeAsync(expected)));
        Assert.Equal(expected, await reader.ReadAsync());
    }

    [Fact]
    public async Task Read_EndInsideFrame_ThrowsEndOfStream()
    {
        var bytes = await EncodeAsync(new Ping());
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
        var reader = new FrameReader(new MemoryStream(await EncodeAsync(new Ping())));
        await reader.ReadAsync();
        Assert.Null(await reader.ReadAsync());
    }

    [Theory]
    [InlineData("{\"type\":\"doesNotExist\"}", true)]
    [InlineData("{not json", false)]
    [InlineData("{}", true)]
    [InlineData("[1]", false)]
    public async Task Read_UnknownTypeOrInvalidJson_Throws(string json, bool jsonObject)
    {
        var reader = new FrameReader(new MemoryStream(RawFrame(json)));
        var exception = await Assert.ThrowsAnyAsync<ProtocolException>(() => reader.ReadAsync());
        Assert.Equal(jsonObject, exception is InvalidRequestException); // Package 83: an object is a bad request, anything else a broken frame
    }

    /// <summary>Package 83: the server answers a bad request under its own RequestId.</summary>
    [Fact]
    public async Task Read_InvalidRequest_CarriesRequestId()
    {
        var reader = new FrameReader(new MemoryStream(RawFrame("{\"type\":\"sendChat\",\"requestId\":\"r1\",\"target\":\"Nirgends\",\"text\":\"x\"}")));
        Assert.Equal("r1", (await Assert.ThrowsAsync<InvalidRequestException>(() => reader.ReadAsync())).RequestId);
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
        var milliseconds = new MemoryStream();
        var writer = new FrameWriter(milliseconds);
        await Assert.ThrowsAsync<ProtocolException>(() => writer.WriteAsync(new Error(null, "x", new string('a', 1_100_000))));
        Assert.Equal(0, milliseconds.Length);
    }

    [Fact]
    public async Task Write_Concurrent100_AllFramesIntact()
    {
        var milliseconds = new MemoryStream();
        var writer = new FrameWriter(milliseconds);
        await Task.WhenAll(Enumerable.Range(0, 100).Select(index => Task.Run(() => writer.WriteAsync(new Error(index.ToString(CultureInfo.InvariantCulture), "c")))));

        milliseconds.Position = 0;
        var reader = new FrameReader(milliseconds);
        var ids = new HashSet<string?>();
        while (await reader.ReadAsync() is Error error) ids.Add(error.RequestId);
        Assert.Equal(100, ids.Count);
    }
}
