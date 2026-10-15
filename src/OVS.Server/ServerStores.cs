using OVS.Server.Data;
using OVS.Server.Logging;

namespace OVS.Server;

/// <summary>
/// The file stores of one run, all in the data folder. <see cref="Open"/> makes them and removes what a crash left behind
/// before anything reads the folder; <see cref="State.ServerState"/> receives them and creates none.
/// </summary>
internal sealed class ServerStores
{
    ServerStores(string dataDir, TimeProvider time)
    {
        DataDir = dataDir;
        DataStore = new DataStore(Path.Combine(dataDir, DataStore.FileName));
        IconStore = new ServerIconStore(dataDir);
        AvatarStore = new AvatarStore(dataDir); // Package 168
        BackupStore = new BackupStore(dataDir, time);
        UploadFolder = new UploadFolder(BackupStore.Folder);
        LogReader = new LogReader(dataDir, time);
        LogExporter = new LogExporter(dataDir, LogReader, time);
    }

    /// <summary>The data folder every store lives in.</summary>
    public string DataDir { get; }
    /// <summary>server-data.json.</summary>
    public DataStore DataStore { get; }
    /// <summary>The server logo.</summary>
    public ServerIconStore IconStore { get; }
    /// <summary>Package 168: the users' profile pictures.</summary>
    public AvatarStore AvatarStore { get; }
    /// <summary>The backups folder.</summary>
    public BackupStore BackupStore { get; }
    /// <summary>The unfinished uploads inside the backups folder.</summary>
    public UploadFolder UploadFolder { get; }
    /// <summary>Lists, reads and searches the log files.</summary>
    public LogReader LogReader { get; }
    /// <summary>Prepares log downloads.</summary>
    public LogExporter LogExporter { get; }

    /// <summary>The stores of <paramref name="config"/>'s data folder, after the crash cleanups.</summary>
    public static ServerStores Open(ServerConfig config, TimeProvider time)
    {
        var stores = new ServerStores(config.DataDir, time);
        stores.UploadFolder.RemoveLeftovers(); // Package 75: what a crash left behind
        stores.BackupStore.RemoveUnfinished();
        stores.LogExporter.RemoveExports(); // Package 82: what a crash left behind
        return stores;
    }
}
