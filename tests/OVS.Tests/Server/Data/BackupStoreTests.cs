using OVS.Server.Data;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Server.Data;

public sealed class BackupStoreTests() : TempDirectoryTest("ovs-store-", quiet: false)
{
    BackupStore NewStore() => new(dir, new ManualTimeProvider());

    [Fact]
    public void RemoveUnfinished_NoFolder_NothingCreated()
    {
        var store = NewStore();

        store.RemoveUnfinished();

        Assert.False(Directory.Exists(store.Folder));
    }

    [Fact]
    public void RemoveUnfinished_HalfWrittenArchives_Removed()
    {
        var store = NewStore();
        Directory.CreateDirectory(store.Folder);
        foreach (var name in new[] { "2026-01-01_12-00-00.ovsbackup.tmp", "vor-wiederherstellung_2026-01-01_12-00-00_2.ovsbackup.tmp" })
            File.WriteAllText(Path.Combine(store.Folder, name), "x");

        store.RemoveUnfinished();

        Assert.Empty(Directory.GetFiles(store.Folder));
    }

    [Fact]
    public void RemoveUnfinished_OtherFiles_Kept()
    {
        var store = NewStore();
        var kept = new[] { "behalten.ovsbackup", "notiz.tmp", UploadFolder.Prefix + new string('a', 32) };
        Directory.CreateDirectory(store.Folder);
        foreach (var name in kept) File.WriteAllText(Path.Combine(store.Folder, name), "x");

        store.RemoveUnfinished();

        Assert.Equal(kept.Order(StringComparer.Ordinal), Directory.GetFiles(store.Folder).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }
}
