using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using OVS.Manager.Docker;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using OVS.Manager;
using OVS.Manager.Servers;

namespace OVS.Tests.TestSupport;

/// <summary>Package M1: the manager in memory (TestServer, no port) with its own temporary data folder.</summary>
/// <param name="settings">Package M3: further settings, as the environment would give them (such as the first admin).</param>
internal sealed class ManagerFactory(params (string Key, string Value)[] settings) : WebApplicationFactory<ManagerOptions>
{
    /// <summary>Package M6: Docker in memory unless a test asks for the real adapter (then OVS_MANAGER_DOCKER_HOST decides).</summary>
    public FakeDockerEngine Docker { get; } = new();

    /// <summary>Package M8: the servers' answer to the status check's TLS handshake.</summary>
    public FakeServerProbe Probe { get; } = new();

    /// <summary>Package M6: the real <c>DockerEngine</c> instead of <see cref="Docker"/>.</summary>
    public bool RealDocker { get; init; }

    /// <summary>The manager's data folder, deleted with the factory.</summary>
    public string DataDir { get; } = Path.Combine(Path.GetTempPath(), "ovs-manager-" + Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting(ManagerOptions.DataDirVariable, DataDir);
        foreach (var (key, value) in settings) builder.UseSetting(key, value);
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IServerProbe>(Probe);
            if (!RealDocker) services.AddSingleton<IDockerEngine>(Docker);
        });
    }

    /// <summary>Package M3: the pages answer in German, as for most users; a test of the language sets its own header.</summary>
    protected override void ConfigureClient(HttpClient client)
    {
        base.ConfigureClient(client);
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("de-DE");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;
        SqliteConnection.ClearAllPools(); // the pooled connections keep the database file open on Windows
        if (Directory.Exists(DataDir)) Directory.Delete(DataDir, true);
    }
}
