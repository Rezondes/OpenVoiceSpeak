using OVS.Server.Data;
using OVS.Shared.Protocol;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Server.Data;

public sealed class BackupUploadTests() : TempDirectoryTest("ovs-upload-", quiet: false)
{
    BackupUpload NewUpload() => new("id", Path.Combine(dir, ".upload-id"));

    [Fact]
    public void New_NothingAppended_KeepsIdAndPathAndNothingReceived()
    {
        var upload = NewUpload();
        Assert.Equal("id", upload.Id);
        Assert.Equal(Path.Combine(dir, ".upload-id"), upload.Path);
        Assert.Equal(0, upload.Received);
    }

    [Fact]
    public void Append_FirstChunk_ReplacesAStaleFile()
    {
        var upload = NewUpload();
        File.WriteAllBytes(upload.Path, [9, 9, 9, 9]);
        upload.Append([1, 2]);
        Assert.Equal([1, 2], File.ReadAllBytes(upload.Path));
        Assert.Equal(2, upload.Received);
    }

    [Fact]
    public void Append_LaterChunk_AddsToTheFile()
    {
        var upload = NewUpload();
        upload.Append([1, 2]);
        upload.Append([3]);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(upload.Path));
        Assert.Equal(3, upload.Received);
    }

    [Fact]
    public void Append_FileCannotBeWritten_ThrowsAndCountsNothing()
    {
        var upload = new BackupUpload("id", Path.Combine(dir, "missing", ".upload-id"));
        Assert.Throws<DirectoryNotFoundException>(() => upload.Append([1]));
        Assert.Equal(0, upload.Received);
    }

    [Fact]
    public void Continues_OffsetOfTheBytesReceived_True()
    {
        var upload = NewUpload();
        upload.Append([1, 2]);
        Assert.True(upload.Continues(2));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void Continues_OtherOffset_False(long offset)
    {
        var upload = NewUpload();
        upload.Append([1, 2]);
        Assert.False(upload.Continues(offset));
    }

    [Fact]
    public void Overflows_UpToMaxBackupUploadBytes_False()
    {
        var upload = NewUpload();
        upload.Append([1]);
        Assert.False(upload.Overflows((int)ProtocolLimits.MaxBackupUploadBytes - 1));
    }

    [Fact]
    public void Overflows_PastMaxBackupUploadBytes_True()
    {
        var upload = NewUpload();
        upload.Append([1]);
        Assert.True(upload.Overflows((int)ProtocolLimits.MaxBackupUploadBytes));
    }
}
