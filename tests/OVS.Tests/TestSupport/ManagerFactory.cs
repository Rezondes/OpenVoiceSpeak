using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using OVS.Manager;

namespace OVS.Tests.TestSupport;

/// <summary>Package M1: the manager in memory (TestServer, no port) with its own temporary data folder.</summary>
internal sealed class ManagerFactory : WebApplicationFactory<ManagerOptions>
{
    /// <summary>The manager's data folder, deleted with the factory.</summary>
    public string DataDir { get; } = Path.Combine(Path.GetTempPath(), "ovs-manager-" + Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseSetting(ManagerOptions.DataDirVariable, DataDir);

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;
        SqliteConnection.ClearAllPools(); // the pooled connections keep the database file open on Windows
        if (Directory.Exists(DataDir)) Directory.Delete(DataDir, true);
    }
}
