using OVS.Server;
using OVS.Server.Data;
using OVS.Server.Logging;
using OVS.Server.State;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Server;

public sealed class ServerStoresTests() : TempDirectoryTest("ovs-stores-", quiet: false)
{
    const string UploadId = "0123456789abcdef0123456789abcdef";
    const string DownloadId = "fedcba9876543210fedcba9876543210";
    const string LogId = "server/2026-01-01_12-00-00.log";
    const string BackupName = "2026-01-01_12-00-00" + BackupStore.Extension;

    ServerConfig Config => new(0, dir, 50, "Testserver", "");

    ServerStores Open() => ServerStores.Open(Config, new ManualTimeProvider());

    string Write(string relative)
    {
        var path = Path.Combine(dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
        return path;
    }

    [Fact]
    public void Open_CrashLeftovers_Removed()
    {
        var upload = Write(Path.Combine(BackupStore.FolderName, UploadFolder.Prefix + UploadId));
        var halfWritten = Write(Path.Combine(BackupStore.FolderName, BackupName + ".tmp"));
        var export = Write(Path.Combine(LogExporter.ExportFolder, DownloadId + ".zip"));

        Open();

        Assert.False(File.Exists(upload), "upload left");
        Assert.False(File.Exists(halfWritten), "half written backup left");
        Assert.False(File.Exists(export), "export left");
    }

    [Fact]
    public void Open_FinishedFiles_Kept()
    {
        var backup = Write(Path.Combine(BackupStore.FolderName, BackupName));
        var log = Write(Path.Combine("logs", LogId));

        Open();

        Assert.True(File.Exists(backup), "backup removed");
        Assert.True(File.Exists(log), "log removed");
    }

    [Fact]
    public void Open_DataDir_EveryStorePointsThere()
    {
        TestImages.Write(dir, ServerIconStore.FileName, 128, 128);
        Write(Path.Combine("logs", LogId));

        var stores = Open();

        Assert.Equal(Path.Combine(dir, DataStore.FileName), stores.DataStore.Path);
        Assert.NotNull(stores.IconStore.Hash);
        Assert.Equal(Path.Combine(dir, BackupStore.FolderName), stores.BackupStore.Folder);
        Assert.Equal(Path.Combine(dir, BackupStore.FolderName, UploadFolder.Prefix + UploadId), stores.UploadFolder.PreparePath(UploadId));
        Assert.Equal([LogId], stores.LogReader.List().Select(logFile => logFile.Id));
        var (export, _) = stores.LogExporter.Export(DownloadId, [LogId], new Dictionary<Guid, string>());
        Assert.Equal(Path.Combine(dir, LogExporter.ExportFolder, DownloadId + ".log"), export!.Path);
    }

    [Fact]
    public void ServerState_StoresOfAnotherDataDir_Throws()
    {
        var other = Directory.CreateDirectory(Path.Combine(dir, "other")).FullName;
        var stores = ServerStores.Open(Config with { DataDir = other }, new ManualTimeProvider());

        Assert.Throws<ArgumentException>(() => new ServerState(Config, new ManualTimeProvider(), new ServerLogWriter(dir, 0, new ManualTimeProvider(), _ => { }), stores));
    }

    [Fact]
    public void Open_NoDataFile_NothingCreated()
    {
        Open();

        Assert.Empty(Directory.GetFileSystemEntries(dir));
    }
}
