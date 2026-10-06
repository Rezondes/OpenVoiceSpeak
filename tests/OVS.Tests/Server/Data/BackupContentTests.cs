using OVS.Server.Data;
using OVS.Server.Tls;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Server.Data;

public sealed class BackupContentTests() : TempDirectoryTest("ovs-content-", quiet: false)
{
    static readonly BackupManifest Manifest = new(BackupArchive.FormatVersion, 6, "1.2.3", PersistedFixtures.Date);

    void WriteLive(string name, byte[] bytes) => File.WriteAllBytes(Path.Combine(dir, name), bytes);

    byte[] ReadLive(string name) => File.ReadAllBytes(Path.Combine(dir, name));

    [Fact]
    public void ApplyTo_WithIcon_EveryLiveFileReplaced()
    {
        foreach (var name in new[] { DataStore.FileName, ServerCertificate.FileName, ServerIconStore.FileName }) WriteLive(name, [9, 9, 9]);

        new BackupContent(Manifest, [1], [2], [3]).ApplyTo(dir);

        Assert.Equal([1], ReadLive(DataStore.FileName));
        Assert.Equal([2], ReadLive(ServerCertificate.FileName));
        Assert.Equal([3], ReadLive(ServerIconStore.FileName));
        Assert.Equal(3, Directory.GetFiles(dir).Length); // no .tmp left
    }

    [Fact]
    public void ApplyTo_CertificateWriteFails_LiveDataUnchanged()
    {
        WriteLive(DataStore.FileName, [9, 9]);
        Directory.CreateDirectory(Path.Combine(dir, ServerCertificate.FileName)); // the first write cannot replace a folder

        var failure = Record.Exception(() => new BackupContent(Manifest, [1], [2], null).ApplyTo(dir));

        Assert.True(failure is IOException or UnauthorizedAccessException, $"{failure}");

        Assert.Equal([9, 9], ReadLive(DataStore.FileName));
    }

    [Fact]
    public void ApplyTo_NoIcon_LiveIconDeleted()
    {
        WriteLive(ServerIconStore.FileName, [9]);

        new BackupContent(Manifest, [1], [2], null).ApplyTo(dir);

        Assert.False(File.Exists(Path.Combine(dir, ServerIconStore.FileName)));
        Assert.Equal([1], ReadLive(DataStore.FileName));
        Assert.Equal([2], ReadLive(ServerCertificate.FileName));
    }

    [Fact]
    public void ApplyTo_NoIconAndNoLiveIcon_FilesWritten()
    {
        new BackupContent(Manifest, [1], [2], null).ApplyTo(dir);

        Assert.Equal(new[] { DataStore.FileName, ServerCertificate.FileName }.Order(StringComparer.Ordinal), Directory.GetFiles(dir).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }
}
