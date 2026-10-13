using OVS.Client.Services;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.Services;

/// <summary>Packages 75 and 82: a file pulled chunk by chunk into target.part, moved into place only after the last chunk.</summary>
public sealed class ChunkedDownloadTests() : TempDirectoryTest("ovs-chunked-", quiet: false)
{
    static readonly byte[] Content = [1, 2, 3, 4, 5, 6, 7];

    string Target => Path.Combine(dir, "backup.zip");

    /// <summary>Serves <see cref="Content"/> in chunks of three bytes, as the server would.</summary>
    static Task<DownloadChunk?> ServeAsync(long offset)
    {
        var part = Content.Skip((int)offset).Take(3).ToArray();
        return Task.FromResult<DownloadChunk?>(new DownloadChunk(offset, Content.Length, Convert.ToBase64String(part), offset + part.Length >= Content.Length));
    }

    [Fact]
    public async Task RunAsync_AllChunks_MovesTheFileIntoPlaceAndReportsProgress()
    {
        var progress = new List<(long Done, long Total)>();

        Assert.True(await ChunkedDownload.RunAsync(Target, ServeAsync, (done, total) => progress.Add((done, total))));

        Assert.Equal(Content, await File.ReadAllBytesAsync(Target));
        Assert.False(File.Exists(Target + ".part"));
        Assert.Equal([(3L, 7L), (6L, 7L), (7L, 7L)], progress);
    }

    [Fact]
    public async Task RunAsync_NextStops_KeepsTheOldFileAndLeavesNoPart()
    {
        File.WriteAllBytes(Target, [9, 9]);

        Assert.False(await ChunkedDownload.RunAsync(Target, offset => offset == 0 ? ServeAsync(0) : Task.FromResult<DownloadChunk?>(null), (_, _) => { }));

        Assert.Equal([9, 9], await File.ReadAllBytesAsync(Target));
        Assert.False(File.Exists(Target + ".part"));
    }

    [Fact]
    public async Task RunAsync_ChunkAtTheWrongOffset_Stops()
    {
        Assert.False(await ChunkedDownload.RunAsync(Target, offset => ServeAsync(offset + 1), (_, _) => { }));

        Assert.False(File.Exists(Target));
        Assert.False(File.Exists(Target + ".part"));
    }

    [Fact]
    public async Task RunAsync_EmptyChunkThatIsNotTheLast_StopsInsteadOfAskingForever()
    {
        int asked = 0;

        Assert.False(await ChunkedDownload.RunAsync(Target, offset =>
        {
            asked++;
            return Task.FromResult<DownloadChunk?>(new DownloadChunk(offset, Content.Length, "", IsLast: false));
        }, (_, _) => { }));

        Assert.Equal(1, asked);
        Assert.False(File.Exists(Target + ".part"));
    }

    [Fact]
    public async Task RunAsync_MalformedBase64_ThrowsAndRemovesThePart()
    {
        await Assert.ThrowsAsync<FormatException>(() => ChunkedDownload.RunAsync(Target,
            offset => Task.FromResult<DownloadChunk?>(new DownloadChunk(offset, 3, "not base64!", IsLast: true)), (_, _) => { }));

        Assert.False(File.Exists(Target));
        Assert.False(File.Exists(Target + ".part"));
    }
}
