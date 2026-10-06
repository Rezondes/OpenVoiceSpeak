using OVS.Server.Logging;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Server.Logging;

public sealed class LogDownloadTests() : TempDirectoryTest("ovs-logdownload-", quiet: false)
{
    readonly LogDownload download = new();

    string NewFile(string name)
    {
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, "log");
        return path;
    }

    [Fact]
    public void FindPath_NothingPrepared_Null()
    {
        Assert.Null(download.FindPath("a"));
    }

    [Fact]
    public void FindPath_PreparedId_ItsPath()
    {
        var path = NewFile("a.zip");
        Assert.True(download.TrySet("a", path));
        Assert.Equal(path, download.FindPath("a"));
    }

    [Theory]
    [InlineData("b")]
    [InlineData(null)]
    public void FindPath_OtherId_Null(string? id)
    {
        Assert.True(download.TrySet("a", NewFile("a.zip")));
        Assert.Null(download.FindPath(id));
    }

    [Fact]
    public void TrySet_SecondDownload_ReplacesAndDeletesTheFirstFile()
    {
        var first = NewFile("a.zip");
        var second = NewFile("b.zip");
        download.TrySet("a", first);
        Assert.True(download.TrySet("b", second));
        Assert.False(File.Exists(first));
        Assert.Null(download.FindPath("a"));
        Assert.Equal(second, download.FindPath("b"));
    }

    [Fact]
    public void Drop_Prepared_DeletesTheFileAndForgetsIt()
    {
        var path = NewFile("a.zip");
        download.TrySet("a", path);
        download.Drop();
        Assert.False(File.Exists(path));
        Assert.Null(download.FindPath("a"));
    }

    [Fact]
    public void Drop_ThenTrySet_Taken()
    {
        download.Drop();
        Assert.True(download.TrySet("a", NewFile("a.zip")));
    }

    [Fact]
    public void End_Prepared_DeletesTheFileAndForgetsIt()
    {
        var path = NewFile("a.zip");
        download.TrySet("a", path);
        download.End();
        Assert.False(File.Exists(path));
        Assert.Null(download.FindPath("a"));
    }

    [Fact]
    public void TrySet_AfterEnd_RefusedAndTheFileLeftToTheCaller()
    {
        download.End();
        var path = NewFile("a.zip");
        Assert.False(download.TrySet("a", path));
        Assert.True(File.Exists(path));
        Assert.Null(download.FindPath("a"));
    }
}
